using System.Data;
using Dapper;
using ModernWMS.Core.JWT;
using ModernWMS.Core.Utility;
using ModernWMS.WMS.Entities.Models;
using ModernWMS.WMS.Entities.ViewModels.DispatchWorkflow;
using ModernWMS.WMS.IServices.StockAllocation;

namespace ModernWMS.WMS.Services.DispatchWorkflow;

public partial class DispatchWorkflowService
{
    private async Task<PackingPlanViewModel> ConfirmActualPackingCoreAsync(
        int orderId,int taskId,ConfirmActualPackingRequest request,CurrentUser user,CancellationToken ct)
    {
        ValidatePackingPlanCommand(orderId,taskId,request.request_id,request.row_version,request.task_row_version);
        var guard=await EnsurePostPickSourceCurrentAsync(orderId,user,ct);
        if(guard.source_change_pending)throw DispatchWorkflowCommandException.SourceChangePending();
        await using var connection=await _connectionFactory.OpenConnectionAsync(ct);
        await using var transaction=await connection.BeginTransactionAsync(IsolationLevel.Serializable,ct);
        try
        {
            var previous=await FindOperationAsync(connection,transaction,orderId,
                DispatchWorkflowOperation.ConfirmActualPacking,request.request_id,ct);
            if(previous!=null)
            {
                await transaction.CommitAsync(ct);
                return await GetPackingPlanAsync(orderId,taskId,user,ct);
            }

            var aggregate=await LoadPackingPlanForUpdateAsync(connection,transaction,orderId,taskId,ct);
            await _warehouseAccessService.EnsureAllowedAsync(aggregate.Order.warehouse_id,user);
            if(aggregate.Order.status!=DispatchOrderStatus.Weighing
                ||aggregate.Order.row_version!=request.row_version
                ||aggregate.Task.row_version!=request.task_row_version)
                throw DispatchWorkflowCommandException.ConcurrencyConflict();
            if(aggregate.Task.packing_plan_status!="PACKING_CONFIRMED")
                throw DispatchWorkflowCommandException.StatusNotAllowedForWeighing();
            if(aggregate.Boxes.Count==0||aggregate.BoxItems.Count==0
                ||aggregate.Boxes.Any(box=>!aggregate.BoxItems.Any(item=>item.weighing_box_id==box.id)))
                throw DispatchWorkflowCommandException.WeighingIncomplete("每个箱必须填写实际装箱商品");

            var stockIds=aggregate.BoxItems.Select(x=>x.erp_stock_id).Distinct().Order().ToArray();
            var identities=await LoadActualPackingStockIdentitiesAsync(connection,transaction,stockIds,ct);
            foreach(var box in aggregate.Boxes)
                ActualPackingLinePolicy.ValidateBox(aggregate.BoxItems.Where(x=>x.weighing_box_id==box.id)
                    .Select(x=>new ActualPackingDraftLine(x.client_line_key,x.packing_task_item_id,
                        x.erp_stock_id,x.actual_qty)).ToArray(),
                    aggregate.Items.Select(x=>x.id).ToHashSet(),identities,aggregate.Order.warehouse_id);

            var details=(await connection.QueryAsync<DispatchlistEntity>(new CommandDefinition("""
                SELECT * FROM `wms_dispatchlist`
                 WHERE `dispatch_order_id`=@orderId AND `packing_task_id`=@taskId FOR UPDATE;
                """,new{orderId,taskId},transaction,cancellationToken:ct))).AsList();
            var detailIds=details.Select(x=>x.id).ToArray();
            var picks=detailIds.Length==0?[]:(await connection.QueryAsync<DispatchpicklistEntity>(new CommandDefinition("""
                SELECT * FROM `wms_dispatchpicklist`
                 WHERE `dispatchlist_id` IN @detailIds ORDER BY `erp_stock_id`,`id` FOR UPDATE;
                """,new{detailIds},transaction,cancellationToken:ct))).AsList();
            if(picks.Any(x=>x.is_update_stock||x.erp_stock_id is null or <=0))
                throw DispatchWorkflowCommandException.StockAlreadyDeducted();

            var groups=aggregate.BoxItems.GroupBy(x=>new ActualPackingKey(
                    x.packing_task_item_id,x.erp_stock_id))
                .OrderBy(x=>x.Key)
                .Select(group=>new ActualPackingGroup(group.Key,group.OrderBy(x=>x.id).ToList(),
                    checked(group.Sum(x=>x.actual_qty))))
                .ToList();
            var materialization=ActualPackingMaterializationPolicy.Build(
                picks.Select(x=>new ActualPackingCurrentPick(x.id,x.packing_task_item_id,
                    x.erp_stock_id!.Value,x.picked_qty)).ToArray(),
                groups.Select(x=>new ActualPackingTarget(x.BusinessKey,x.Key.PackingTaskItemId,
                    x.Key.ErpStockId,x.Quantity)).ToArray());

            var mutation=RequirePackingStockMutationService();
            var picksById=picks.ToDictionary(x=>x.id);
            var reserveOwners=groups.ToDictionary(x=>x.Key,x=>picks
                .Where(p=>Matches(p,x.Key)).OrderBy(p=>p.id).FirstOrDefault());
            var prelocks=new List<PackingStockPrelockRequest>();
            foreach(var release in materialization.Releases)
            {
                var pick=picksById[release.PickId];
                prelocks.Add(new PackingStockPrelockRequest(
                    DispatchStockMutationContext(user,aggregate.Order.warehouse_id,"DISPATCH_RELEASE",orderId,pick.id,
                        pick.erp_stock_id!.Value,release.Quantity,
                        $"ACTUAL_PACKING:{taskId}:{request.request_id}",pick.reservation_id,pick.reservation_item_id),
                    pick.erp_stock_id.Value,"UNLOCK"));
            }
            foreach(var reserve in materialization.Reserves)
            {
                var key=new ActualPackingKey(reserve.PackingTaskItemId,reserve.ErpStockId);
                var owner=reserveOwners[key];var bizItemId=groups.Single(x=>x.Key==key).Lines[0].id;
                prelocks.Add(new PackingStockPrelockRequest(
                    DispatchStockMutationContext(user,aggregate.Order.warehouse_id,"DISPATCH_RESERVE",orderId,bizItemId,
                        reserve.ErpStockId,reserve.Quantity,
                        $"ACTUAL_PACKING:{taskId}:{reserve.BusinessKey}:{request.request_id}",
                        owner?.reservation_id,owner?.reservation_item_id),reserve.ErpStockId,
                    "LOCK"));
            }
            if(prelocks.Count>0)
                await mutation.PrelockAsync(connection,transaction,
                    [aggregate.Order.warehouse_id],prelocks,ct);

            foreach(var release in materialization.Releases.OrderBy(x=>picksById[x.PickId].erp_stock_id)
                        .ThenBy(x=>x.PickId))
            {
                var pick=picksById[release.PickId];
                await mutation.ReleaseAsync(connection,transaction,
                    DispatchStockMutationContext(user,aggregate.Order.warehouse_id,"DISPATCH_RELEASE",orderId,pick.id,
                        pick.erp_stock_id!.Value,release.Quantity,
                        $"ACTUAL_PACKING:{taskId}:{request.request_id}",pick.reservation_id,pick.reservation_item_id),
                    pick.erp_stock_id.Value,release.Quantity,ct);
                if(pick.stock_allocation_id is >0)
                    await RequireLegacyPackingReleaseAdapter().SettleReleaseAsync(
                        connection,transaction,pick.erp_stock_id.Value,pick.stock_allocation_id.Value,
                        pick.reservation_item_id!.Value,release.Quantity,user.user_name??string.Empty,ct);
                pick.pick_qty-=release.Quantity;pick.picked_qty-=release.Quantity;
            }

            var newReservationResults=new Dictionary<ActualPackingKey,PackingStockMutationResult>();
            foreach(var reserve in materialization.Reserves.OrderBy(x=>x.ErpStockId)
                        .ThenBy(x=>x.BusinessKey,StringComparer.Ordinal))
            {
                var key=new ActualPackingKey(reserve.PackingTaskItemId,reserve.ErpStockId);
                var owner=reserveOwners[key];var bizItemId=groups.Single(x=>x.Key==key).Lines[0].id;
                var result=await mutation.ReserveAsync(connection,transaction,
                    DispatchStockMutationContext(user,aggregate.Order.warehouse_id,"DISPATCH_RESERVE",orderId,bizItemId,
                        reserve.ErpStockId,reserve.Quantity,
                        $"ACTUAL_PACKING:{taskId}:{reserve.BusinessKey}:{request.request_id}",
                        owner?.reservation_id,owner?.reservation_item_id),reserve.ErpStockId,
                    reserve.Quantity,ct);
                if(owner==null)newReservationResults[key]=result;
                else
                {
                    owner.pick_qty+=reserve.Quantity;owner.picked_qty+=reserve.Quantity;
                    owner.reservation_id=result.ReservationId;owner.reservation_item_id=result.ReservationItemId;
                }
            }

            var now=DateTime.Now;
            foreach(var pick in picks)
            {
                if(pick.picked_qty==0)
                    await connection.ExecuteAsync(new CommandDefinition(
                        "DELETE FROM `wms_dispatchpicklist` WHERE `id`=@id;",new{pick.id},transaction,cancellationToken:ct));
                else
                    await connection.ExecuteAsync(new CommandDefinition("""
                        UPDATE `wms_dispatchpicklist`
                           SET `pick_qty`=@pick_qty,`picked_qty`=@picked_qty,
                               `reservation_id`=@reservation_id,`reservation_item_id`=@reservation_item_id,
                               `last_update_time`=@now WHERE `id`=@id;
                        """,new{pick.pick_qty,pick.picked_qty,pick.reservation_id,pick.reservation_item_id,now,pick.id},
                        transaction,cancellationToken:ct));
            }

            var targetDetailIds=new HashSet<int>();
            foreach(var detailGroup in groups.GroupBy(x=>new ActualPackingDetailKey(
                         x.Key.PackingTaskItemId)).OrderBy(x=>x.Key))
            {
                var detail=details.Where(x=>x.packing_task_item_id==detailGroup.Key.PackingTaskItemId
                        &&x.sku_id==0).OrderBy(x=>x.id).FirstOrDefault();
                var quantity=checked(detailGroup.Sum(x=>x.Quantity));
                if(detail==null)
                {
                    detail=new DispatchlistEntity{id=await InsertActualPackingDetailAsync(connection,transaction,
                        aggregate.Order,taskId,detailGroup.Key.PackingTaskItemId,0,
                        quantity,user,now,ct)};
                    details.Add(detail);
                }
                else
                    await connection.ExecuteAsync(new CommandDefinition("""
                        UPDATE `wms_dispatchlist` SET `sku_id`=@skuId,`qty`=@quantity,
                          `lock_qty`=@quantity,`picked_qty`=@quantity,`last_update_time`=@now WHERE `id`=@id;
                        """,new{skuId=0,quantity,now,detail.id},transaction,cancellationToken:ct));
                targetDetailIds.Add(detail.id);

                foreach(var group in detailGroup)
                {
                    var groupPicks=picks.Where(x=>x.picked_qty>0&&Matches(x,group.Key)).OrderBy(x=>x.id).ToList();
                    if(groupPicks.Count==0)
                    {
                        var result=newReservationResults[group.Key];
                        var pickId=await InsertActualPackingPickAsync(connection,transaction,detail.id,group,
                            result,user,now,ct);
                        var created=new DispatchpicklistEntity{id=pickId,dispatchlist_id=detail.id,
                            packing_task_item_id=group.Key.PackingTaskItemId,erp_stock_id=group.Key.ErpStockId,
                            stock_allocation_id=null,sku_id=0,
                            pick_qty=group.Quantity,picked_qty=group.Quantity,reservation_id=result.ReservationId,
                            reservation_item_id=result.ReservationItemId};
                        picks.Add(created);groupPicks.Add(created);
                    }
                    else
                    {
                        foreach(var pick in groupPicks)
                            await connection.ExecuteAsync(new CommandDefinition(
                                "UPDATE `wms_dispatchpicklist` SET `dispatchlist_id`=@detailId WHERE `id`=@id;",
                                new{detailId=detail.id,pick.id},transaction,cancellationToken:ct));
                    }
                    var tracePickId=groupPicks[0].id;
                    await connection.ExecuteAsync(new CommandDefinition("""
                        UPDATE `wms_weighing_box_item` SET `dispatchpicklist_id`=@tracePickId,
                          `last_update_time`=@now,`row_version`=`row_version`+1 WHERE `id` IN @lineIds;
                        """,new{tracePickId,now,lineIds=group.Lines.Select(x=>x.id).ToArray()},transaction,cancellationToken:ct));
                }
            }
            var orphanDetailIds=details.Where(x=>!targetDetailIds.Contains(x.id)).Select(x=>x.id).ToArray();
            if(orphanDetailIds.Length>0)
                await connection.ExecuteAsync(new CommandDefinition(
                    "DELETE FROM `wms_dispatchlist` WHERE `id` IN @orphanDetailIds;",
                    new{orphanDetailIds},transaction,cancellationToken:ct));

            foreach(var item in aggregate.Items)
            {
                var actual=aggregate.BoxItems.Where(x=>x.packing_task_item_id==item.id).Sum(x=>x.actual_qty);
                await connection.ExecuteAsync(new CommandDefinition("""
                    UPDATE `wms_dispatch_packing_task_item`
                       SET `actual_packed_task_qty`=@actual,`actual_packed_required_qty`=@actual,
                           `last_update_time`=@now,`row_version`=`row_version`+1 WHERE `id`=@id;
                    """,new{actual,now,item.id},transaction,cancellationToken:ct));
            }
            await connection.ExecuteAsync(new CommandDefinition("""
                UPDATE `wms_dispatch_packing_task` SET `packing_plan_status`='ACTUAL_CONFIRMED',
                  `actual_confirmed_at`=@now,`actual_confirmed_by`=@userId,
                  `actual_confirmed_by_name`=@name,`last_update_time`=@now,
                  `row_version`=`row_version`+1 WHERE `id`=@taskId;
                UPDATE `wms_dispatch_order` SET `last_update_time`=@now,`row_version`=`row_version`+1
                 WHERE `id`=@orderId AND `row_version`=@expected;
                """,new{now,userId=user.user_id,name=user.user_name,taskId,orderId,
                    expected=aggregate.Order.row_version},transaction,cancellationToken:ct));
            await InsertOperationAsync(connection,transaction,orderId,
                DispatchWorkflowOperation.ConfirmActualPacking,request.request_id,aggregate.Order.status,
                aggregate.Order.row_version+1,user,now,ct);
            await transaction.CommitAsync(ct);
            return await GetPackingPlanAsync(orderId,taskId,user,ct);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private static bool Matches(DispatchpicklistEntity pick,ActualPackingKey key) =>
        pick.packing_task_item_id==key.PackingTaskItemId&&pick.erp_stock_id==key.ErpStockId;

    private static async Task<int> InsertActualPackingDetailAsync(IDbConnection connection,IDbTransaction transaction,
        DispatchOrderEntity order,int taskId,int? taskItemId,int skuId,int quantity,CurrentUser user,DateTime now,
        CancellationToken ct)=>await connection.ExecuteScalarAsync<int>(new CommandDefinition("""
            INSERT INTO `wms_dispatchlist` (`dispatch_order_id`,`packing_task_id`,`packing_task_item_id`,
              `dispatch_no`,`dispatch_status`,`sku_id`,`qty`,`weight`,`volume`,`creator`,`create_time`,
              `damage_qty`,`lock_qty`,`picked_qty`,`intrasit_qty`,`package_qty`,`weighing_qty`,`actual_qty`,
              `sign_qty`,`package_no`,`package_person`,`package_time`,`weighing_no`,`weighing_person`,
              `weighing_weight`,`weighing_length`,`weighing_width`,`weighing_height`,`weighing_volume`,
              `waybill_no`,`carrier`,`carrier_unit`,`freightfee`,`last_update_time`,`pick_checker_id`,`pick_checker`)
            VALUES (@orderId,@taskId,@taskItemId,@dispatchNo,3,@skuId,@quantity,0,0,@name,@now,
              0,@quantity,@quantity,0,0,0,0,0,'','',@minDate,'','',0,0,0,0,0,'','','',0,@now,@userId,@name);
            SELECT LAST_INSERT_ID();
            """,new{orderId=order.id,taskId,taskItemId,dispatchNo=order.dispatch_no,skuId,quantity,
                name=user.user_name,now,minDate=UtilConvert.MinDate,userId=user.user_id},transaction,cancellationToken:ct));

    private static async Task<int> InsertActualPackingPickAsync(IDbConnection connection,IDbTransaction transaction,
        int detailId,ActualPackingGroup group,PackingStockMutationResult reservation,
        CurrentUser user,DateTime now,CancellationToken ct)=>
        await connection.ExecuteScalarAsync<int>(new CommandDefinition("""
            INSERT INTO `wms_dispatchpicklist` (`dispatchlist_id`,`packing_task_item_id`,`stock_id`,
              `erp_stock_id`,`stock_allocation_id`,`reservation_id`,`reservation_item_id`,`goods_owner_id`,
              `goods_location_id`,`sku_id`,`pick_qty`,`picked_qty`,`is_update_stock`,`last_update_time`,
              `series_number`,`picker_id`,`picker`,`expiry_date`,`price`,`putaway_date`)
            VALUES (@detailId,@taskItemId,NULL,@erpStockId,NULL,@reservationId,@reservationItemId,
              NULL,NULL,NULL,@quantity,@quantity,0,@now,'',@userId,@name,@minDate,0,@minDate);
            SELECT LAST_INSERT_ID();
            """,new{detailId,taskItemId=group.Key.PackingTaskItemId,erpStockId=group.Key.ErpStockId,
                reservationId=reservation.ReservationId,reservationItemId=reservation.ReservationItemId,
                quantity=group.Quantity,now,userId=user.user_id,name=user.user_name,
                minDate=UtilConvert.MinDate},transaction,cancellationToken:ct));

    private readonly record struct ActualPackingKey(int? PackingTaskItemId,long ErpStockId):IComparable<ActualPackingKey>
    {
        public int CompareTo(ActualPackingKey other)
        {
            var comparison=Nullable.Compare(PackingTaskItemId,other.PackingTaskItemId);
            return comparison!=0?comparison:ErpStockId.CompareTo(other.ErpStockId);
        }
    }
    private readonly record struct ActualPackingDetailKey(int? PackingTaskItemId):IComparable<ActualPackingDetailKey>
    {
        public int CompareTo(ActualPackingDetailKey other)
            =>Nullable.Compare(PackingTaskItemId,other.PackingTaskItemId);
    }
    private sealed record ActualPackingGroup(ActualPackingKey Key,List<WeighingBoxItemEntity> Lines,int Quantity)
    {public string BusinessKey=>$"{PackingTaskItemKey}:{Key.ErpStockId}";private string PackingTaskItemKey=>Key.PackingTaskItemId?.ToString()??"EXTRA";}
}
