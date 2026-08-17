using System.Data;
using Dapper;
using ModernWMS.Core.JWT;
using ModernWMS.WMS.Entities.Models;
using ModernWMS.WMS.Entities.ViewModels.DispatchWorkflow;
using ModernWMS.WMS.Entities.ViewModels.PackingTask;
using MySqlConnector;

namespace ModernWMS.WMS.Services.DispatchWorkflow;

public partial class DispatchWorkflowService
{
    public async Task<CompletePickingResult> CompletePickingAsync(int orderId,CompletePickingRequest request,CurrentUser user,
        CancellationToken ct=default)
    {
        if(orderId<=0||string.IsNullOrWhiteSpace(request.request_id)||request.request_id.Trim().Length>64||request.row_version<0)
            throw new ArgumentException("order id, request_id and row_version are required",nameof(request));
        var requestId=request.request_id.Trim();
        await using var c=await _connectionFactory.OpenConnectionAsync(ct);
        await using var tx=await c.BeginTransactionAsync(IsolationLevel.Serializable,ct);
        try
        {
            var previous=await FindOperationAsync(c,tx,orderId,DispatchWorkflowOperation.CompletePicking,requestId,ct);
            if(previous?.result_status==DispatchWorkflowOperationResultStatus.Succeeded){await tx.CommitAsync(ct);return FromLedger(previous);}
            var order=await LoadReconcileOrderAsync(c,tx,orderId,ct);
            await _warehouseAccessService.EnsureAllowedAsync(order.warehouse_id,user);
            if(order.status!=DispatchOrderStatus.PendingPick)throw DispatchWorkflowCommandException.StatusNotAllowed();
            if(order.row_version!=request.row_version)throw DispatchWorkflowCommandException.ConcurrencyConflict();
            var tasks=order.packing_tasks.Where(x=>x.is_active).ToList();
            var snapshots=await _sourceReader.ReadAsync(tasks.Select(x=>x.source_task_id).ToArray(),ct);
            if(snapshots.Count!=tasks.Count||snapshots.Where(x=>!x.IsCancelled).Any(x=>x.WarehouseId!=order.warehouse_id))throw DispatchWorkflowCommandException.SourceChanged();
            var mappings=await ResolveCurrentSkuMappingsAsync(snapshots,ct); var now=DateTime.Now;
            foreach(var task in tasks)
            {
                var snap=snapshots.Single(x=>x.SourceTaskId==task.source_task_id);
                if(snap.IsCancelled){await CancelTaskAsync(c,tx,task,now,ct);task.is_active=false;continue;}
                if(!string.Equals(task.source_version,snap.SourceVersion,StringComparison.Ordinal))
                {await RemoveTaskAllocationsAsync(c,tx,task,ct);await RebuildTaskItemsAsync(c,tx,task,snap,now,ct);}
                foreach(var item in snap.Items)
                    await c.ExecuteAsync(new CommandDefinition("""
                        UPDATE `wms_dispatch_packing_task_item` SET `wms_sku_id`=@skuId,`last_update_time`=@now,`row_version`=`row_version`+1
                        WHERE `packing_task_id`=@taskId AND `source_item_id`=@sourceItemId AND `is_active`=1;
                        """,new{skuId=MappedSkuId(item,mappings),now,taskId=task.id,sourceItemId=item.SourceItemId},tx,cancellationToken:ct));
            }
            var active=tasks.Where(x=>x.is_active).ToList();
            if(active.Count==0)
            {
                await EnsurePickingSourceAsync(tasks,snapshots,ct);
                var version=order.row_version+1;
                await UpdatePickedOrderAsync(c,tx,orderId,request.row_version,DispatchOrderStatus.SourceCancelled,snapshots,now,ct);
                await InsertOperationAsync(c,tx,orderId,DispatchWorkflowOperation.CompletePicking,requestId,DispatchOrderStatus.SourceCancelled,version,user,now,ct);
                await tx.CommitAsync(ct);return new CompletePickingResult{order_id=orderId,request_id=requestId,status=ToApiStatus(DispatchOrderStatus.SourceCancelled),row_version=version};
            }
            var items=(await c.QueryAsync<DispatchPackingTaskItemEntity>(new CommandDefinition("""
                SELECT i.* FROM `wms_dispatch_packing_task_item` i JOIN `wms_dispatch_packing_task` t ON t.`id`=i.`packing_task_id`
                WHERE t.`dispatch_order_id`=@orderId AND t.`is_active`=1 AND i.`is_active`=1 ORDER BY i.`packing_task_id`,i.`source_item_id`;
                """,new{orderId},tx,cancellationToken:ct))).AsList();
            if(items.Count==0)throw DispatchWorkflowCommandException.SourceChanged("packing task has no active item to pick");
            if(items.Any(x=>x.wms_sku_id is null or <=0))throw DispatchWorkflowCommandException.SkuMappingMissing("packing task item has no valid WMS SKU mapping");
            if(items.Any(x=>x.required_qty is null or <=0))throw DispatchWorkflowCommandException.SourceChanged("packing task item has invalid required quantity");
            var allocations=await BuildAllocationPlanAsync(c,tx,order.warehouse_id,items,ct);
            await EnsurePickingSourceAsync(tasks,snapshots,ct);
            var details=new Dictionary<int,int>();
            foreach(var item in items)
            {
                var detailId=await c.ExecuteScalarAsync<int>(new CommandDefinition("""
                    INSERT INTO `wms_dispatchlist` (`dispatch_order_id`,`packing_task_id`,`packing_task_item_id`,`dispatch_no`,`dispatch_status`,
                      `sku_id`,`qty`,`weight`,`volume`,`creator`,`create_time`,`damage_qty`,`lock_qty`,`picked_qty`,`intrasit_qty`,`package_qty`,`weighing_qty`,`actual_qty`,`sign_qty`,
                      `package_no`,`package_person`,`package_time`,`weighing_no`,`weighing_person`,`weighing_weight`,`weighing_length`,`weighing_width`,`weighing_height`,`weighing_volume`,
                      `waybill_no`,`carrier`,`carrier_unit`,`freightfee`,`last_update_time`,`tenant_id`,`pick_checker_id`,`pick_checker`)
                    VALUES (@orderId,@taskId,@itemId,@dispatchNo,3,@skuId,@qty,0,0,@name,@now,0,@qty,@qty,0,0,0,0,0,'','',@minDate,'','',0,0,0,0,0,'','','',0,@now,@tenantId,@userId,@name);
                    SELECT LAST_INSERT_ID();
                    """,new{orderId,taskId=item.packing_task_id,itemId=item.id,dispatchNo=order.dispatch_no,skuId=item.wms_sku_id,qty=item.required_qty,
                        name=user.user_name,now,minDate=ModernWMS.Core.Utility.UtilConvert.MinDate,tenantId=order.tenant_id,userId=user.user_id},tx,cancellationToken:ct));
                details[item.id]=detailId;
            }
            foreach(var a in allocations)
                await c.ExecuteAsync(new CommandDefinition("""
                    INSERT INTO `wms_dispatchpicklist` (`dispatchlist_id`,`packing_task_item_id`,`stock_id`,`goods_owner_id`,`goods_location_id`,`sku_id`,`pick_qty`,`picked_qty`,
                      `is_update_stock`,`last_update_time`,`series_number`,`picker_id`,`picker`,`expiry_date`,`price`,`putaway_date`)
                    VALUES (@detailId,@itemId,@stockId,@ownerId,@locationId,@skuId,@quantity,@quantity,0,@now,@series,@userId,@name,@expiry,@price,@putaway);
                    """,new{detailId=details[a.Item.id],itemId=a.Item.id,stockId=a.Stock.id,ownerId=a.Stock.goods_owner_id,locationId=a.Stock.goods_location_id,
                        skuId=a.Stock.sku_id,a.Quantity,now,series=a.Stock.series_number,userId=user.user_id,name=user.user_name,expiry=a.Stock.expiry_date,a.Stock.price,putaway=a.Stock.putaway_date},tx,cancellationToken:ct));
            await c.ExecuteAsync(new CommandDefinition("""
                UPDATE `wms_dispatch_packing_task` SET `status`=@status,`last_update_time`=@now,`row_version`=`row_version`+1
                WHERE `dispatch_order_id`=@orderId AND `is_active`=1;
                """,new{status=DispatchOrderStatus.Picked,now,orderId},tx,cancellationToken:ct));
            var resultVersion=order.row_version+1;
            await UpdatePickedOrderAsync(c,tx,orderId,request.row_version,DispatchOrderStatus.Picked,snapshots,now,ct);
            await InsertOperationAsync(c,tx,orderId,DispatchWorkflowOperation.CompletePicking,requestId,DispatchOrderStatus.Picked,resultVersion,user,now,ct);
            await tx.CommitAsync(ct);
            return new CompletePickingResult{order_id=orderId,request_id=requestId,status=ToApiStatus(DispatchOrderStatus.Picked),row_version=resultVersion};
        }
        catch(Exception ex) when(IsDatabaseConcurrency(ex))
        {await tx.RollbackAsync(CancellationToken.None);var winner=await FindOperationAsync(c,null,orderId,DispatchWorkflowOperation.CompletePicking,requestId,CancellationToken.None);if(winner?.result_status==DispatchWorkflowOperationResultStatus.Succeeded)return FromLedger(winner);throw DispatchWorkflowCommandException.ConcurrencyConflict();}
        catch{await tx.RollbackAsync(CancellationToken.None);throw;}
    }

    private async Task EnsurePickingSourceAsync(IReadOnlyCollection<DispatchPackingTaskEntity> tasks,IReadOnlyList<PackingTaskSourceSnapshot> snapshots,CancellationToken ct)
    {var commit=await _sourceReader.ReadAsync(tasks.Select(x=>x.source_task_id).ToArray(),ct);if(!string.Equals(SnapshotJson(commit),SnapshotJson(snapshots),StringComparison.Ordinal))throw DispatchWorkflowCommandException.SourceChanged();}

    private static async Task UpdatePickedOrderAsync(System.Data.IDbConnection c,IDbTransaction tx,int id,long expected,DispatchOrderStatus status,
        IReadOnlyList<PackingTaskSourceSnapshot> snapshots,DateTime now,CancellationToken ct)
    {var n=await c.ExecuteAsync(new CommandDefinition("""
        UPDATE `wms_dispatch_order` SET `status`=@status,`source_version`=@sourceVersion,`source_snapshot`=@sourceSnapshot,
          `last_update_time`=@now,`row_version`=`row_version`+1 WHERE `id`=@id AND `row_version`=@expected;
        """,new{id,expected,status,sourceVersion=CombinedVersion(snapshots),sourceSnapshot=SnapshotJson(snapshots),now},tx,cancellationToken:ct));if(n!=1)throw DispatchWorkflowCommandException.ConcurrencyConflict();}

    private static bool IsDatabaseConcurrency(Exception exception)
    {for(Exception? x=exception;x!=null;x=x.InnerException)if(x is MySqlException m&&(m.Number is 1062 or 1205 or 1213||m.ErrorCode is MySqlErrorCode.DuplicateKeyEntry or MySqlErrorCode.LockWaitTimeout or MySqlErrorCode.LockDeadlock||m.SqlState=="40001"))return true;return false;}

    private static async Task<DispatchWorkflowOperationEntity?> FindOperationAsync(System.Data.IDbConnection c,IDbTransaction? tx,int orderId,DispatchWorkflowOperation operation,string requestId,CancellationToken ct)=>
        await c.QuerySingleOrDefaultAsync<DispatchWorkflowOperationEntity>(new CommandDefinition("SELECT * FROM `wms_dispatch_workflow_operation` WHERE `dispatch_order_id`=@orderId AND `operation`=@operation AND `request_id`=@requestId LIMIT 1;",new{orderId,operation,requestId},tx,cancellationToken:ct));

    private static async Task InsertOperationAsync(System.Data.IDbConnection c,IDbTransaction tx,int orderId,DispatchWorkflowOperation operation,string requestId,DispatchOrderStatus status,long version,CurrentUser user,DateTime now,CancellationToken ct)=>
        await c.ExecuteAsync(new CommandDefinition("""
            INSERT INTO `wms_dispatch_workflow_operation` (`dispatch_order_id`,`operation`,`request_id`,`result_status`,`result_order_status`,`result_row_version`,`create_operator`,`create_operator_name`,`create_time`)
            VALUES (@orderId,@operation,@requestId,@succeeded,@status,@version,@userId,@name,@now);
            """,new{orderId,operation,requestId,succeeded=DispatchWorkflowOperationResultStatus.Succeeded,status,version,userId=user.user_id,name=user.user_name,now},tx,cancellationToken:ct));

    private static CompletePickingResult FromLedger(DispatchWorkflowOperationEntity x)
    {if(x.result_order_status==null||x.result_row_version==null)throw DispatchWorkflowCommandException.ConcurrencyConflict();return new(){order_id=x.dispatch_order_id,request_id=x.request_id,status=ToApiStatus(x.result_order_status.Value),row_version=x.result_row_version.Value};}

    private static async Task<List<PickingAllocation>> BuildAllocationPlanAsync(System.Data.IDbConnection c,IDbTransaction tx,long erpWarehouseId,IReadOnlyList<DispatchPackingTaskItemEntity> items,CancellationToken ct)
    {
        var warehouseIds=(await c.QueryAsync<int>(new CommandDefinition("SELECT `id` FROM `wms_warehouse` WHERE `erp_warehouse_id`=@erpWarehouseId AND `is_valid`=1;",new{erpWarehouseId},tx,cancellationToken:ct))).AsList();
        if(warehouseIds.Count!=1)throw DispatchWorkflowCommandException.StockShortage("ERP warehouse has no unique WMS warehouse mapping");
        var skuIds=items.Select(x=>x.wms_sku_id!.Value).Distinct().ToArray();
        var stocks=(await c.QueryAsync<AvailableStockRow>(new CommandDefinition("""
            SELECT s.*,(CASE WHEN s.`is_freeze`=1 THEN 0 ELSE GREATEST(0,s.`qty`
              -COALESCE((SELECT SUM(p.`pick_qty`) FROM `wms_dispatchpicklist` p JOIN `wms_dispatchlist` d ON d.`id`=p.`dispatchlist_id` WHERE d.`dispatch_status`>1 AND d.`dispatch_status`<6 AND p.`sku_id`=s.`sku_id` AND p.`goods_location_id`=s.`goods_location_id` AND p.`goods_owner_id`=s.`goods_owner_id` AND p.`series_number`<=>s.`series_number` AND p.`expiry_date`<=>s.`expiry_date` AND p.`price`<=>s.`price` AND p.`putaway_date`<=>s.`putaway_date`),0)
              -COALESCE((SELECT SUM(p.`qty`) FROM `wms_stockprocessdetail` p WHERE p.`is_update_stock`=0 AND p.`sku_id`=s.`sku_id` AND p.`goods_location_id`=s.`goods_location_id` AND p.`goods_owner_id`=s.`goods_owner_id` AND p.`series_number`<=>s.`series_number` AND p.`expiry_date`<=>s.`expiry_date` AND p.`price`<=>s.`price` AND p.`putaway_date`<=>s.`putaway_date`),0)
              -COALESCE((SELECT SUM(m.`qty`) FROM `wms_stockmove` m WHERE m.`move_status`=0 AND m.`sku_id`=s.`sku_id` AND m.`orig_goods_location_id`=s.`goods_location_id` AND m.`goods_owner_id`=s.`goods_owner_id` AND m.`series_number`<=>s.`series_number` AND m.`expiry_date`<=>s.`expiry_date` AND m.`price`<=>s.`price` AND m.`putaway_date`<=>s.`putaway_date`),0)) END) available_qty
            FROM `wms_stock` s JOIN `wms_goodslocation` gl ON gl.`id`=s.`goods_location_id`
            WHERE gl.`warehouse_id`=@warehouseId AND gl.`is_valid`=1 AND gl.`warehouse_area_property`<>5 AND s.`sku_id` IN @skuIds
            ORDER BY s.`putaway_date`,s.`expiry_date`,s.`id` FOR UPDATE;
            """,new{warehouseId=warehouseIds[0],skuIds},tx,cancellationToken:ct))).AsList();
        var available=stocks.Where(x=>x.available_qty>0).Select(x=>new AvailableStock(x,x.available_qty)).ToList();var plan=new List<PickingAllocation>();
        foreach(var item in items){var candidates=available.Where(x=>x.Stock.sku_id==item.wms_sku_id).ToList();var owners=candidates.Select(x=>x.Stock.goods_owner_id).Distinct().ToList();if(owners.Count!=1)throw DispatchWorkflowCommandException.StockShortage(owners.Count==0?$"insufficient stock for SKU {item.commodity_sku}":$"multiple goods owners match SKU {item.commodity_sku}");var remaining=item.required_qty!.Value;foreach(var x in candidates){var q=Math.Min(remaining,x.Quantity);if(q<=0)continue;plan.Add(new(item,x.Stock,q));x.Quantity-=q;remaining-=q;if(remaining==0)break;}if(remaining!=0)throw DispatchWorkflowCommandException.StockShortage($"insufficient stock for SKU {item.commodity_sku}");}return plan;
    }
    private sealed record PickingAllocation(DispatchPackingTaskItemEntity Item,StockEntity Stock,int Quantity);
    private sealed class AvailableStock(StockEntity stock,int quantity){public StockEntity Stock{get;}=stock;public int Quantity{get;set;}=quantity;}
    private sealed class AvailableStockRow:StockEntity{public int available_qty{get;set;}}
}

public sealed partial class DispatchWorkflowCommandException:InvalidOperationException
{
    private DispatchWorkflowCommandException(string code,string detail):base(string.IsNullOrWhiteSpace(detail)?code:$"{code}: {detail}")=>ErrorCode=code;
    public string ErrorCode{get;}
    public static DispatchWorkflowCommandException SourceChanged(string detail="packing task source changed during picking completion")=>new("SOURCE_CHANGED",detail);
    public static DispatchWorkflowCommandException StockShortage(string detail)=>new("STOCK_SHORTAGE",detail);
    public static DispatchWorkflowCommandException SkuMappingMissing(string detail)=>new("SKU_MAPPING_MISSING",detail);
    public static DispatchWorkflowCommandException SkuMappingConflict(string detail)=>new("SKU_MAPPING_CONFLICT",detail);
    public static DispatchWorkflowCommandException ConcurrencyConflict()=>new("CONCURRENCY_CONFLICT","row version does not match");
    public static DispatchWorkflowCommandException StatusNotAllowed()=>new("STATUS_NOT_ALLOWED","only a pending-pick order can be completed");
}
