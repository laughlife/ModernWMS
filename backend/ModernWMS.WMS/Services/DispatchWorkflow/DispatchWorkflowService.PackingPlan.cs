using System.Data;
using System.Text.Json;
using Dapper;
using ModernWMS.Core.JWT;
using ModernWMS.WMS.Entities.Models;
using ModernWMS.WMS.Entities.ViewModels.DispatchWorkflow;
using ModernWMS.WMS.Services.PackingTask;

namespace ModernWMS.WMS.Services.DispatchWorkflow;

/// <summary>
/// 表示 DispatchWorkflowService 类型。
/// </summary>
public partial class DispatchWorkflowService
{
    /// <summary>
    /// 执行 GetPackingPlanAsync 操作。
    /// </summary>
    public async Task<PackingPlanViewModel> GetPackingPlanAsync(int orderId,int taskId,CurrentUser user,CancellationToken ct=default)
    {
        await using var c=await _connectionFactory.OpenConnectionAsync(ct);
        var order=await c.QuerySingleOrDefaultAsync<DispatchOrderEntity>(new CommandDefinition("SELECT * FROM `wms_dispatch_order` WHERE `id`=@orderId;",new{orderId},cancellationToken:ct))??throw new KeyNotFoundException($"dispatch order not found: {orderId}");
        await _warehouseAccessService.EnsureAllowedAsync(order.warehouse_id,user);
        var task=await c.QuerySingleOrDefaultAsync<DispatchPackingTaskEntity>(new CommandDefinition("SELECT * FROM `wms_dispatch_packing_task` WHERE `id`=@taskId AND `dispatch_order_id`=@orderId AND `is_active`=1;",new{taskId,orderId},cancellationToken:ct))??throw new KeyNotFoundException($"packing task not found: {taskId}");
        var items=(await c.QueryAsync<DispatchPackingTaskItemEntity>(new CommandDefinition("SELECT * FROM `wms_dispatch_packing_task_item` WHERE `packing_task_id`=@taskId AND `is_active`=1 ORDER BY `id`;",new{taskId},cancellationToken:ct))).AsList();
        var boxes=(await c.QueryAsync<WeighingBoxEntity>(new CommandDefinition("SELECT * FROM `wms_weighing_box` WHERE `packing_task_id`=@taskId AND `is_invalidated`=0 ORDER BY `box_sequence`,`id`;",new{taskId},cancellationToken:ct))).AsList();
        var boxIds=boxes.Select(x=>x.id).ToArray();
        var boxItems=boxIds.Length==0?new List<WeighingBoxItemEntity>():(await c.QueryAsync<WeighingBoxItemEntity>(new CommandDefinition("SELECT * FROM `wms_weighing_box_item` WHERE `weighing_box_id` IN @boxIds ORDER BY `id`;",new{boxIds},cancellationToken:ct))).AsList();
        return ToPackingPlan(order,task,items,boxes,boxItems);
    }

    /// <summary>
    /// 执行 SavePackingPlanAsync 操作。
    /// </summary>
    public async Task<PackingPlanViewModel> SavePackingPlanAsync(int orderId,int taskId,SavePackingPlanRequest r,CurrentUser user,CancellationToken ct=default)
    {
        ValidatePackingPlanCommand(orderId,taskId,r.request_id,r.row_version,r.task_row_version);
        var guard=await EnsurePostPickSourceCurrentAsync(orderId,user,ct);if(guard.source_change_pending)throw DispatchWorkflowCommandException.SourceChangePending();
        await using var c=await _connectionFactory.OpenConnectionAsync(ct);await using var tx=await c.BeginTransactionAsync(IsolationLevel.Serializable,ct);
        try
        {
            var previous=await FindOperationAsync(c,tx,orderId,DispatchWorkflowOperation.SavePackingDraft,r.request_id,ct);
            if(previous!=null){await tx.CommitAsync(ct);return await GetPackingPlanAsync(orderId,taskId,user,ct);}
            var aggregate=await LoadPackingPlanForUpdateAsync(c,tx,orderId,taskId,ct);var order=aggregate.Order;var task=aggregate.Task;
            await _warehouseAccessService.EnsureAllowedAsync(order.warehouse_id,user);
            if(order.status!=DispatchOrderStatus.Weighing||order.source_change_pending)throw DispatchWorkflowCommandException.StatusNotAllowedForWeighing();
            if(task.packing_plan_status!="DRAFT"&&task.packing_plan_status!="PACKING_CONFIRMED")throw DispatchWorkflowCommandException.StatusNotAllowedForWeighing();
            if(order.row_version!=r.row_version||task.row_version!=r.task_row_version)throw DispatchWorkflowCommandException.ConcurrencyConflict();
            ValidateDraft(r.boxes,aggregate.Items);
            var now=DateTime.Now;var retained=new HashSet<int>();var sequence=0;
            foreach(var draft in r.boxes)
            {
                sequence++;
                int boxId;
                if(draft.id is >0)
                {
                    var existing=aggregate.Boxes.SingleOrDefault(x=>x.id==draft.id.Value)??throw DispatchWorkflowCommandException.BoxNotAvailable("箱子不属于当前装箱任务");
                    if(existing.row_version!=draft.row_version)throw DispatchWorkflowCommandException.ConcurrencyConflict();
                    boxId=existing.id;await c.ExecuteAsync(new CommandDefinition("""
                        UPDATE `wms_weighing_box` SET `box_sequence`=@sequence,`weight`=@weight,`length`=@length,`width`=@width,`height`=@height,
                          `measurement_status`=@status,`last_update_time`=@now,`row_version`=`row_version`+1 WHERE `id`=@boxId;
                        """,new{sequence,draft.weight,draft.length,draft.width,draft.height,status=MeasurementStatus(draft),now,boxId},tx,cancellationToken:ct));
                }
                else
                {
                    boxId=await c.ExecuteScalarAsync<int>(new CommandDefinition("""
                        INSERT INTO `wms_weighing_box` (`packing_task_id`,`box_identity`,`source_box_identity`,`box_sequence`,`weight`,`length`,`width`,`height`,`measurement_status`,`measured_by_name`,`source_snapshot`,`is_invalidated`,`create_time`,`last_update_time`,`row_version`)
                        VALUES (@taskId,@identity,@sourceIdentity,@sequence,@weight,@length,@width,@height,@status,'','{}',0,@now,@now,0);SELECT LAST_INSERT_ID();
                        """,new{taskId,identity=HashText($"WMS_BOX:{taskId}:{draft.client_key}"),sourceIdentity=$"{task.source_task_no}-箱{sequence}",sequence,draft.weight,draft.length,draft.width,draft.height,status=MeasurementStatus(draft),now},tx,cancellationToken:ct));
                }
                retained.Add(boxId);await c.ExecuteAsync(new CommandDefinition("DELETE FROM `wms_weighing_box_item` WHERE `weighing_box_id`=@boxId;",new{boxId},tx,cancellationToken:ct));
                foreach(var item in draft.items)await c.ExecuteAsync(new CommandDefinition("""
                    INSERT INTO `wms_weighing_box_item` (`weighing_box_id`,`packing_task_item_id`,`goods_owner_id`,`task_qty`,`create_time`,`last_update_time`,`row_version`)
                    VALUES (@boxId,@itemId,@goodsOwnerId,@qty,@now,@now,0);
                    """,new{boxId,itemId=item.packing_task_item_id,goodsOwnerId=item.goods_owner_id,qty=item.task_qty,now},tx,cancellationToken:ct));
            }
            var removed=aggregate.Boxes.Where(x=>!retained.Contains(x.id)).Select(x=>x.id).ToArray();
            if(removed.Length>0)await c.ExecuteAsync(new CommandDefinition("UPDATE `wms_weighing_box` SET `is_invalidated`=1,`invalidated_at`=@now,`last_update_time`=@now,`row_version`=`row_version`+1 WHERE `id` IN @removed;",new{now,removed},tx,cancellationToken:ct));
            var boxCount=r.boxes.Count;await c.ExecuteAsync(new CommandDefinition("""
                UPDATE `wms_dispatch_packing_task` SET `expected_box_count`=@boxCount,`measured_box_count`=@measured,`last_update_time`=@now,`row_version`=`row_version`+1 WHERE `id`=@taskId;
                UPDATE `wms_dispatch_order` SET `last_update_time`=@now,`row_version`=`row_version`+1 WHERE `id`=@orderId AND `row_version`=@expected;
                """,new{boxCount,measured=r.boxes.Count(x=>MeasurementStatus(x)=="MEASURED"),now,taskId,orderId,expected=order.row_version},tx,cancellationToken:ct));
            await InsertOperationAsync(c,tx,orderId,DispatchWorkflowOperation.SavePackingDraft,r.request_id,order.status,order.row_version+1,user,now,ct);await tx.CommitAsync(ct);
            return await GetPackingPlanAsync(orderId,taskId,user,ct);
        }catch{await tx.RollbackAsync(CancellationToken.None);throw;}
    }

    /// <summary>
    /// 执行 ConfirmPackingAsync 操作。
    /// </summary>
    public async Task<PackingPlanViewModel> ConfirmPackingAsync(int orderId,int taskId,ConfirmActualPackingRequest r,CurrentUser user,CancellationToken ct=default)
    {
        ValidatePackingPlanCommand(orderId,taskId,r.request_id,r.row_version,r.task_row_version);
        var guard=await EnsurePostPickSourceCurrentAsync(orderId,user,ct);if(guard.source_change_pending)throw DispatchWorkflowCommandException.SourceChangePending();
        await using var c=await _connectionFactory.OpenConnectionAsync(ct);await using var tx=await c.BeginTransactionAsync(IsolationLevel.Serializable,ct);
        try
        {
            var previous=await FindOperationAsync(c,tx,orderId,DispatchWorkflowOperation.ConfirmPacking,r.request_id,ct);if(previous!=null){await tx.CommitAsync(ct);return await GetPackingPlanAsync(orderId,taskId,user,ct);}
            var a=await LoadPackingPlanForUpdateAsync(c,tx,orderId,taskId,ct);await _warehouseAccessService.EnsureAllowedAsync(a.Order.warehouse_id,user);
            if(a.Order.status!=DispatchOrderStatus.Weighing||a.Order.source_change_pending||a.Task.packing_plan_status!="DRAFT")throw DispatchWorkflowCommandException.StatusNotAllowedForWeighing();
            if(a.Order.row_version!=r.row_version||a.Task.row_version!=r.task_row_version)throw DispatchWorkflowCommandException.ConcurrencyConflict();
            var now=DateTime.Now;
            var taskUpdated=await c.ExecuteAsync(new CommandDefinition("UPDATE `wms_dispatch_packing_task` SET `packing_plan_status`='PACKING_CONFIRMED',`last_update_time`=@now,`row_version`=`row_version`+1 WHERE `id`=@taskId AND `row_version`=@taskVersion;",new{now,taskId,taskVersion=r.task_row_version},tx,cancellationToken:ct));
            var orderUpdated=await c.ExecuteAsync(new CommandDefinition("UPDATE `wms_dispatch_order` SET `last_update_time`=@now,`row_version`=`row_version`+1 WHERE `id`=@orderId AND `row_version`=@orderVersion;",new{now,orderId,orderVersion=r.row_version},tx,cancellationToken:ct));
            if(taskUpdated!=1||orderUpdated!=1)throw DispatchWorkflowCommandException.ConcurrencyConflict();
            await InsertOperationAsync(c,tx,orderId,DispatchWorkflowOperation.ConfirmPacking,r.request_id,a.Order.status,a.Order.row_version+1,user,now,ct);await tx.CommitAsync(ct);
            return await GetPackingPlanAsync(orderId,taskId,user,ct);
        }catch{await tx.RollbackAsync(CancellationToken.None);throw;}
    }

    /// <summary>
    /// 执行 ConfirmActualPackingAsync 操作。
    /// </summary>
    public async Task<PackingPlanViewModel> ConfirmActualPackingAsync(int orderId,int taskId,ConfirmActualPackingRequest r,CurrentUser user,CancellationToken ct=default)
    {
        ValidatePackingPlanCommand(orderId,taskId,r.request_id,r.row_version,r.task_row_version);
        var guard=await EnsurePostPickSourceCurrentAsync(orderId,user,ct);if(guard.source_change_pending)throw DispatchWorkflowCommandException.SourceChangePending();
        await using var c=await _connectionFactory.OpenConnectionAsync(ct);await using var tx=await c.BeginTransactionAsync(IsolationLevel.Serializable,ct);
        var committed=false;
        try
        {
            var previous=await FindOperationAsync(c,tx,orderId,DispatchWorkflowOperation.ConfirmActualPacking,r.request_id,ct);if(previous!=null){await tx.CommitAsync(ct);committed=true;return await GetPackingPlanAsync(orderId,taskId,user,ct);}
            var a=await LoadPackingPlanForUpdateAsync(c,tx,orderId,taskId,ct);if(a.Order.status!=DispatchOrderStatus.Weighing||a.Order.row_version!=r.row_version||a.Task.row_version!=r.task_row_version)throw DispatchWorkflowCommandException.ConcurrencyConflict();
            if(a.Task.packing_plan_status!="PACKING_CONFIRMED")throw DispatchWorkflowCommandException.StatusNotAllowedForWeighing();
            if(a.Boxes.Count==0)throw DispatchWorkflowCommandException.WeighingIncomplete("至少建立一个装箱");
            var plans=new List<ActualPackingItemPlan>();
            foreach(var item in a.Items)
            {
                if(item.variant_qty is null or <=0||item.source_quantity_shipped is null or <=0)throw DispatchWorkflowCommandException.WeighingIncomplete($"商品 {item.commodity_sku} 变体数据无效");
                var packed=a.BoxItems.Where(x=>x.packing_task_item_id==item.id).Sum(x=>x.task_qty);if(packed<0||packed>item.source_quantity_shipped)throw DispatchWorkflowCommandException.WeighingIncomplete($"{item.commodity_name}（sku：{item.commodity_sku}）总任务量{item.source_quantity_shipped}，实际任务量{packed}");
                var actual=checked(packed*item.variant_qty.Value);var detail=await c.QuerySingleOrDefaultAsync<DispatchlistEntity>(new CommandDefinition("SELECT * FROM `wms_dispatchlist` WHERE `packing_task_item_id`=@id FOR UPDATE;",new{item.id},tx,cancellationToken:ct));
                if(detail==null)throw DispatchWorkflowCommandException.StockConflict("商品拣货分配不存在");var allocations=(await c.QueryAsync<DispatchpicklistEntity>(new CommandDefinition("SELECT * FROM `wms_dispatchpicklist` WHERE `dispatchlist_id`=@id ORDER BY `id` DESC FOR UPDATE;",new{detail.id},tx,cancellationToken:ct))).AsList();
                if(allocations.Any(x=>x.is_update_stock))throw DispatchWorkflowCommandException.StockAlreadyDeducted();
                var actualByOwner=a.BoxItems.Where(x=>x.packing_task_item_id==item.id)
                    .GroupBy(x=>x.goods_owner_id).ToDictionary(group=>group.Key,
                        group=>checked((long)group.Sum(x=>x.task_qty)*item.variant_qty.Value));
                var reservedByOwner=allocations.GroupBy(x=>x.goods_owner_id)
                    .ToDictionary(group=>group.Key,group=>group.Sum(x=>(long)x.picked_qty));
                if(actualByOwner.Sum(x=>x.Value)!=actual||
                   actualByOwner.Any(x=>x.Key<=0||!reservedByOwner.TryGetValue(x.Key,out var reserved)||x.Value<0||x.Value>reserved))
                    throw DispatchWorkflowCommandException.WeighingIncomplete("实装货主与ERP预占货主或数量不一致");
                var reductions=new List<ActualPackingAllocationReduction>();
                foreach(var owner in reservedByOwner)
                {
                    var release=checked((int)(owner.Value-actualByOwner.GetValueOrDefault(owner.Key)));
                    foreach(var allocation in allocations.Where(x=>x.goods_owner_id==owner.Key))
                    {
                        var reduce=Math.Min(release,allocation.picked_qty);var remain=allocation.picked_qty-reduce;release-=reduce;
                        if(reduce>0)reductions.Add(new ActualPackingAllocationReduction(allocation,reduce,remain));
                    }
                    if(release!=0)throw DispatchWorkflowCommandException.StockConflict("库存释放数量不一致");
                }
                plans.Add(new ActualPackingItemPlan(item,packed,actual,detail,allocations,reductions,actualByOwner));
            }
            if(plans.All(x=>x.Packed==0))throw DispatchWorkflowCommandException.WeighingIncomplete("实际装箱总量不能为零");
            var consumeNow=DateTime.Now;
            foreach(var plan in plans)
            {
                var reservedByOwner=plan.Allocations.GroupBy(x=>x.goods_owner_id)
                    .ToDictionary(group=>group.Key,group=>group.Sum(x=>(long)x.picked_qty));
                // 每个预占货主都必须进入结算；0 表示该货主本次未实装，Ruoyi 会释放其全部余量。
                var contributions=reservedByOwner.Keys.OrderBy(x=>x)
                    .Select(ownerId=>new PackingConsumeContribution(ownerId,plan.ActualByOwner.GetValueOrDefault(ownerId))).ToList();
                if(contributions.Count==0||contributions.Any(x=>x.GoodsOwnerId<=0||x.ActualPackedQty<0))
                    throw DispatchWorkflowCommandException.WeighingIncomplete("每项实装必须携带完整货主结算");
                var frozenBindings=plan.Allocations.Select(x=>new PackingConsumeBinding(
                        x.erp_stock_id??throw DispatchWorkflowCommandException.StockConflict("拣货分配缺少ERP库存标识"),
                        x.stock_allocation_id??throw DispatchWorkflowCommandException.StockConflict("拣货分配缺少库位分配标识"),
                        x.goods_owner_id,x.picked_qty))
                    .OrderBy(x=>x.ErpStockId).ThenBy(x=>x.AllocationId).ThenBy(x=>x.GoodsOwnerId).ToList();
                if(frozenBindings.Count==0||frozenBindings.Any(x=>x.GoodsOwnerId<=0||x.RemainingQty<=0))
                    throw DispatchWorkflowCommandException.StockConflict("拣货分配缺少完整的ERP绑定快照");
                var requestId=HashText($"PACKING_CONSUME|{r.request_id}|{taskId}|{plan.Item.source_item_id}");
                await c.ExecuteAsync(new CommandDefinition("""
                    INSERT INTO `wms_packing_consume_outbox` (`dispatch_order_id`,`packing_task_id`,`sellfox_task_id`,`sellfox_item_id`,`request_id`,`payload_json`,`status`,`attempt_count`,`last_error`,`create_time`,`last_update_time`,`row_version`)
                    VALUES (@orderId,@taskId,@taskSourceId,@itemSourceId,@requestId,@payload,'PENDING',0,'',@now,@now,0);
                    """,new{orderId,taskId,taskSourceId=a.Task.source_task_id,itemSourceId=plan.Item.source_item_id,requestId,
                    payload=JsonSerializer.Serialize(new PackingConsumePayload(contributions,
                        plan.Item.erp_stock_plan_row_version??throw DispatchWorkflowCommandException.StockConflict("拣货明细缺少冻结的ERP库存计划版本"),
                        frozenBindings)),now=consumeNow},tx,cancellationToken:ct));
            }
            // Ruoyi 在 consume 命令中同时结算实装量并释放未实装余量；WMS 只更新本地装箱投影。
            var now=DateTime.Now;
            foreach(var plan in plans)
            {
                foreach(var reduction in plan.Reductions)
                {
                    if(reduction.Remain==0)await c.ExecuteAsync(new CommandDefinition("DELETE FROM `wms_dispatchpicklist` WHERE `id`=@id;",new{id=reduction.Allocation.id},tx,cancellationToken:ct));
                    else await c.ExecuteAsync(new CommandDefinition("UPDATE `wms_dispatchpicklist` SET `pick_qty`=@remain,`picked_qty`=@remain,`last_update_time`=@now WHERE `id`=@id;",new{remain=reduction.Remain,now,id=reduction.Allocation.id},tx,cancellationToken:ct));
                }
                if(plan.Actual==0)await c.ExecuteAsync(new CommandDefinition("DELETE FROM `wms_dispatchlist` WHERE `id`=@id;",new{id=plan.Detail.id},tx,cancellationToken:ct));
                else await c.ExecuteAsync(new CommandDefinition("UPDATE `wms_dispatchlist` SET `qty`=@actual,`lock_qty`=@actual,`picked_qty`=@actual,`last_update_time`=@now WHERE `id`=@id;",new{actual=plan.Actual,now,id=plan.Detail.id},tx,cancellationToken:ct));
                await c.ExecuteAsync(new CommandDefinition("UPDATE `wms_dispatch_packing_task_item` SET `actual_packed_task_qty`=@packed,`actual_packed_required_qty`=@actual,`last_update_time`=@now,`row_version`=`row_version`+1 WHERE `id`=@id;",new{packed=plan.Packed,actual=plan.Actual,now,id=plan.Item.id},tx,cancellationToken:ct));
            }
            await c.ExecuteAsync(new CommandDefinition("""
                UPDATE `wms_dispatch_packing_task` SET `packing_plan_status`='ACTUAL_CONFIRMED',`consume_status`='PENDING',`actual_confirmed_at`=@now,`actual_confirmed_by`=@userId,`actual_confirmed_by_name`=@name,`last_update_time`=@now,`row_version`=`row_version`+1 WHERE `id`=@taskId;
                UPDATE `wms_dispatch_order` SET `last_update_time`=@now,`row_version`=`row_version`+1 WHERE `id`=@orderId AND `row_version`=@expected;
                """,new{now,userId=user.user_id,name=user.user_name,taskId,orderId,expected=a.Order.row_version},tx,cancellationToken:ct));
            await InsertOperationAsync(c,tx,orderId,DispatchWorkflowOperation.ConfirmActualPacking,r.request_id,a.Order.status,a.Order.row_version+1,user,now,ct);await tx.CommitAsync(ct);committed=true;
        }catch{if(!committed)await tx.RollbackAsync(CancellationToken.None);throw;}
        await TryConsumeOutboxAsync(taskId,user,ct);
        return await GetPackingPlanAsync(orderId,taskId,user,ct);
    }

    public async Task<PackingPlanViewModel> RetryPackingConsumeAsync(int orderId,int taskId,CurrentUser user,CancellationToken ct=default)
    {
        await using var c=await _connectionFactory.OpenConnectionAsync(ct);
        var order=await c.QuerySingleOrDefaultAsync<DispatchOrderEntity>(new CommandDefinition("SELECT * FROM `wms_dispatch_order` WHERE `id`=@orderId;",new{orderId},cancellationToken:ct))??throw new KeyNotFoundException("dispatch order not found");
        await _warehouseAccessService.EnsureAllowedAsync(order.warehouse_id,user);
        var task=await c.QuerySingleOrDefaultAsync<DispatchPackingTaskEntity>(new CommandDefinition("SELECT * FROM `wms_dispatch_packing_task` WHERE `id`=@taskId AND `dispatch_order_id`=@orderId AND `is_active`=1;",new{taskId,orderId},cancellationToken:ct))??throw new KeyNotFoundException("packing task not found");
        if(task.consume_status is not ("PENDING" or "FAILED"))return await GetPackingPlanAsync(orderId,taskId,user,ct);
        var blocked=await RefreshFailedConsumeVersionsAsync(taskId,user,ct);
        await TryConsumeOutboxAsync(taskId,user,ct,blocked);
        return await GetPackingPlanAsync(orderId,taskId,user,ct);
    }

    private static async Task<PackingPlanAggregate> LoadPackingPlanForUpdateAsync(IDbConnection c,IDbTransaction tx,int orderId,int taskId,CancellationToken ct)
    {
        using var grid=await c.QueryMultipleAsync(new CommandDefinition("SELECT * FROM `wms_dispatch_order` WHERE `id`=@orderId FOR UPDATE;SELECT * FROM `wms_dispatch_packing_task` WHERE `id`=@taskId AND `dispatch_order_id`=@orderId AND `is_active`=1 FOR UPDATE;SELECT * FROM `wms_dispatch_packing_task_item` WHERE `packing_task_id`=@taskId AND `is_active`=1 FOR UPDATE;SELECT * FROM `wms_weighing_box` WHERE `packing_task_id`=@taskId AND `is_invalidated`=0 FOR UPDATE;",new{orderId,taskId},tx,cancellationToken:ct));
        var order=await grid.ReadSingleAsync<DispatchOrderEntity>();var task=await grid.ReadSingleAsync<DispatchPackingTaskEntity>();var items=(await grid.ReadAsync<DispatchPackingTaskItemEntity>()).AsList();var boxes=(await grid.ReadAsync<WeighingBoxEntity>()).AsList();var ids=boxes.Select(x=>x.id).ToArray();var boxItems=ids.Length==0?new List<WeighingBoxItemEntity>():(await c.QueryAsync<WeighingBoxItemEntity>(new CommandDefinition("SELECT * FROM `wms_weighing_box_item` WHERE `weighing_box_id` IN @ids FOR UPDATE;",new{ids},tx,cancellationToken:ct))).AsList();return new(order,task,items,boxes,boxItems);
    }
    private static void ValidateDraft(IReadOnlyCollection<PackingPlanBoxViewModel> boxes,IReadOnlyCollection<DispatchPackingTaskItemEntity> items)
    {var ids=items.Select(x=>x.id).ToHashSet();if(boxes.SelectMany(x=>x.items).Any(x=>x.task_qty<=0||x.goods_owner_id<=0||!ids.Contains(x.packing_task_item_id)))throw DispatchWorkflowCommandException.WeighingIncomplete("箱内商品、货主或任务量无效");}
    private static string MeasurementStatus(PackingPlanBoxViewModel b)=>b.weight>0&&b.length>0&&b.width>0&&b.height>0?"MEASURED":"UNMEASURED";
    private static void ValidatePackingPlanCommand(int orderId,int taskId,string requestId,long orderVersion,long taskVersion){if(orderId<=0||taskId<=0||string.IsNullOrWhiteSpace(requestId)||requestId.Length>64||orderVersion<0||taskVersion<0)throw new ArgumentException("order, task, request id and versions are required");}
    private static PackingPlanViewModel ToPackingPlan(DispatchOrderEntity o,DispatchPackingTaskEntity t,IReadOnlyCollection<DispatchPackingTaskItemEntity> items,IReadOnlyCollection<WeighingBoxEntity> boxes,IReadOnlyCollection<WeighingBoxItemEntity> boxItems)=>new(){order_id=o.id,packing_task_id=t.id,packing_task_no=t.source_task_no,packing_plan_status=t.packing_plan_status,consume_status=t.consume_status,row_version=o.row_version,task_row_version=t.row_version,items=items.Select(i=>new PackingPlanItemViewModel{id=i.id,commodity_sku=i.commodity_sku,commodity_name=i.commodity_name,fn_sku=i.fn_sku,msku=i.msku,main_image=SourceMainImage(i.source_snapshot),task_qty=i.source_quantity_shipped??0,variant_qty=i.variant_qty??0,required_qty=i.required_qty??0,actual_packed_task_qty=i.actual_packed_task_qty,actual_packed_required_qty=i.actual_packed_required_qty}).ToList(),boxes=boxes.Select(b=>new PackingPlanBoxViewModel{id=b.id,client_key=$"box-{b.id}",box_sequence=b.box_sequence,weight=b.weight,length=b.length,width=b.width,height=b.height,row_version=b.row_version,items=boxItems.Where(x=>x.weighing_box_id==b.id).Select(x=>new PackingPlanBoxItemViewModel{packing_task_item_id=x.packing_task_item_id,goods_owner_id=x.goods_owner_id,task_qty=x.task_qty}).ToList()}).ToList()};
    private sealed record PackingPlanAggregate(DispatchOrderEntity Order,DispatchPackingTaskEntity Task,List<DispatchPackingTaskItemEntity> Items,List<WeighingBoxEntity> Boxes,List<WeighingBoxItemEntity> BoxItems);
    private sealed record ActualPackingItemPlan(DispatchPackingTaskItemEntity Item,int Packed,int Actual,
        DispatchlistEntity Detail,List<DispatchpicklistEntity> Allocations,List<ActualPackingAllocationReduction> Reductions,
        Dictionary<int,long> ActualByOwner);
    private sealed record ActualPackingAllocationReduction(DispatchpicklistEntity Allocation,int Reduce,int Remain);
    private sealed record PackingConsumePayload(List<PackingConsumeContribution> Contributions,long PlanRowVersion,
        List<PackingConsumeBinding>? FrozenBindings);
    private sealed record PackingConsumeContribution(int GoodsOwnerId,long ActualPackedQty);
    private sealed record PackingConsumeBinding(long ErpStockId,long AllocationId,int GoodsOwnerId,long RemainingQty);

    private async Task<HashSet<int>> RefreshFailedConsumeVersionsAsync(int packingTaskId,CurrentUser user,CancellationToken ct)
    {
        var blocked=new HashSet<int>();var client=_erpPackingStockClient;if(client==null)return blocked;
        await using var c=await _connectionFactory.OpenConnectionAsync(ct);
        var rows=(await c.QueryAsync<PackingConsumeOutboxEntity>(new CommandDefinition(
            "SELECT * FROM `wms_packing_consume_outbox` WHERE `packing_task_id`=@packingTaskId AND `status`='FAILED' ORDER BY `id`;",
            new{packingTaskId},cancellationToken:ct))).AsList();
        var actorId=user.user_id.ToString(System.Globalization.CultureInfo.InvariantCulture);
        foreach(var row in rows)
        {
            PackingConsumePayload? payload;
            try{payload=JsonSerializer.Deserialize<PackingConsumePayload>(row.payload_json);}
            catch(JsonException){payload=null;}
            if(payload?.FrozenBindings is not {Count:>0})
            {
                blocked.Add(row.id);
                await SetConsumeRecoveryErrorAsync(c,row,"消费命令缺少冻结绑定快照，必须人工核对ERP绑定后处理",ct);
                continue;
            }
            var result=await client.GetPlanAsync(new ErpPackingStockPlanQuery(row.sellfox_task_id,row.sellfox_item_id,actorId,user.user_name),ct);
            if(!result.IsSuccess||result.Data==null)continue;
            var plan=result.Data;
            if(plan.status!="SELECTED")continue; // 已消费命令仍以原 request_id 重放，让 Ruoyi 幂等返回成功。
            if(!FrozenBindingsMatch(payload,plan))
            {
                blocked.Add(row.id);
                await SetConsumeRecoveryErrorAsync(c,row,"ERP库存绑定已变化，必须撤销实际装箱并重新拣货",ct);
                continue;
            }
            if(plan.rowVersion==payload.PlanRowVersion)continue;
            var refreshed=JsonSerializer.Serialize(payload with{PlanRowVersion=plan.rowVersion});
            await c.ExecuteAsync(new CommandDefinition("UPDATE `wms_packing_consume_outbox` SET `payload_json`=@refreshed,`last_error`='',`last_update_time`=@now,`row_version`=`row_version`+1 WHERE `id`=@id AND `status`='FAILED' AND `row_version`=@rowVersion;",
                new{refreshed,now=DateTime.Now,id=row.id,rowVersion=row.row_version},cancellationToken:ct));
        }
        return blocked;
    }

    private static bool FrozenBindingsMatch(PackingConsumePayload payload,ErpPackingStockPlan plan)
    {
        if(plan.shortageQty!=0||plan.requiredQty!=plan.reservedQty||
           plan.activeBindings.Sum(x=>x.remainingQty)!=plan.requiredQty)return false;
        var frozen=payload.FrozenBindings!.OrderBy(x=>x.ErpStockId).ThenBy(x=>x.AllocationId).ThenBy(x=>x.GoodsOwnerId)
            .Select(x=>(x.ErpStockId,x.AllocationId,x.GoodsOwnerId,x.RemainingQty));
        var current=plan.activeBindings.OrderBy(x=>x.erpStockId).ThenBy(x=>x.allocationId).ThenBy(x=>x.goodsOwnerId)
            .Select(x=>(x.erpStockId,x.allocationId,x.goodsOwnerId,x.remainingQty));
        return frozen.SequenceEqual(current);
    }

    private static async Task SetConsumeRecoveryErrorAsync(IDbConnection c,PackingConsumeOutboxEntity row,string error,CancellationToken ct)=>
        await c.ExecuteAsync(new CommandDefinition("UPDATE `wms_packing_consume_outbox` SET `last_error`=@error,`last_update_time`=@now,`row_version`=`row_version`+1 WHERE `id`=@id AND `status`='FAILED' AND `row_version`=@rowVersion;",
            new{error,now=DateTime.Now,id=row.id,rowVersion=row.row_version},cancellationToken:ct));

    private async Task TryConsumeOutboxAsync(int packingTaskId,CurrentUser user,CancellationToken ct,
        IReadOnlySet<int>? excluded=null)
    {
        await using var c=await _connectionFactory.OpenConnectionAsync(ct);
        var leaseBefore=DateTime.Now.AddMinutes(-5);
        var rows=(await c.QueryAsync<PackingConsumeOutboxEntity>(new CommandDefinition("""
            SELECT * FROM `wms_packing_consume_outbox`
             WHERE `packing_task_id`=@packingTaskId
               AND (`status` IN ('PENDING','FAILED') OR (`status`='PROCESSING' AND `last_update_time`<@leaseBefore))
             ORDER BY `id`;
            """,new{packingTaskId,leaseBefore},cancellationToken:ct))).AsList();
        var client=_erpPackingStockClient;
        if(client==null)
        {
            await c.ExecuteAsync(new CommandDefinition("""
                UPDATE `wms_packing_consume_outbox`
                   SET `status`='FAILED',`attempt_count`=`attempt_count`+1,
                       `last_error`='ERP 装箱库存客户端未注册，已保留待消费命令',
                       `last_update_time`=@now,`row_version`=`row_version`+1
                 WHERE `packing_task_id`=@packingTaskId
                   AND (`status` IN ('PENDING','FAILED') OR (`status`='PROCESSING' AND `last_update_time`<@leaseBefore));
                """,new{packingTaskId,leaseBefore,now=DateTime.Now},cancellationToken:ct));
            await SyncPackingConsumeStatusAsync(c,packingTaskId,ct);
            return;
        }
        foreach(var row in rows.Where(row=>excluded==null||!excluded.Contains(row.id)))
        {
            var claimed=await c.ExecuteAsync(new CommandDefinition("""
                UPDATE `wms_packing_consume_outbox`
                   SET `status`='PROCESSING',`attempt_count`=`attempt_count`+1,`last_error`='',
                       `last_update_time`=@now,`row_version`=`row_version`+1
                 WHERE `id`=@id AND `row_version`=@rowVersion
                   AND (`status` IN ('PENDING','FAILED') OR (`status`='PROCESSING' AND `last_update_time`<@leaseBefore));
                """,new{id=row.id,rowVersion=row.row_version,leaseBefore,now=DateTime.Now},cancellationToken:ct));
            if(claimed!=1)continue;
            var claimVersion=checked(row.row_version+1);
            try
            {
                var payload=JsonSerializer.Deserialize<PackingConsumePayload>(row.payload_json)??throw new InvalidOperationException("消费命令载荷无效");
                if(payload.PlanRowVersion<0)throw new InvalidOperationException("消费命令缺少冻结的ERP库存计划版本");
                var actorId=user.user_id.ToString(System.Globalization.CultureInfo.InvariantCulture);var actorName=string.IsNullOrWhiteSpace(user.user_name)?$"用户{user.user_id}":user.user_name.Trim();
                var result=await client.ConsumeAsync(new ErpPackingStockConsumeCommand(row.sellfox_task_id,row.sellfox_item_id,row.request_id,payload.PlanRowVersion,actorId,actorName,payload.Contributions.Select(x=>new ErpPackingStockOwnerConsumption(x.GoodsOwnerId,x.ActualPackedQty)).ToList()),ct);
                if(!result.IsSuccess||result.Data is not true)throw new InvalidOperationException(
                    string.IsNullOrWhiteSpace(result.ErrorMessage)?"ERP 装箱库存服务未确认消费成功":result.ErrorMessage);
                await c.ExecuteAsync(new CommandDefinition("UPDATE `wms_packing_consume_outbox` SET `status`='CONSUMED',`last_error`='',`consumed_at`=@now,`last_update_time`=@now,`row_version`=`row_version`+1 WHERE `id`=@id AND `status`='PROCESSING' AND `row_version`=@claimVersion;",new{id=row.id,claimVersion,now=DateTime.Now},cancellationToken:ct));
            }
            catch(Exception ex)
            {await c.ExecuteAsync(new CommandDefinition("UPDATE `wms_packing_consume_outbox` SET `status`='FAILED',`last_error`=@error,`last_update_time`=@now,`row_version`=`row_version`+1 WHERE `id`=@id AND `status`='PROCESSING' AND `row_version`=@claimVersion;",new{id=row.id,claimVersion,error=ex.Message[..Math.Min(ex.Message.Length,500)],now=DateTime.Now},cancellationToken:ct));}
        }
        await SyncPackingConsumeStatusAsync(c,packingTaskId,ct);
    }

    private static async Task SyncPackingConsumeStatusAsync(IDbConnection c,int packingTaskId,CancellationToken ct)
    {
        var pending=await c.ExecuteScalarAsync<long>(new CommandDefinition("SELECT COUNT(*) FROM `wms_packing_consume_outbox` WHERE `packing_task_id`=@packingTaskId AND `status`<>'CONSUMED';",new{packingTaskId},cancellationToken:ct));
        var failed=await c.ExecuteScalarAsync<long>(new CommandDefinition("SELECT COUNT(*) FROM `wms_packing_consume_outbox` WHERE `packing_task_id`=@packingTaskId AND `status`='FAILED';",new{packingTaskId},cancellationToken:ct));
        var status=pending==0?"CONSUMED":failed>0?"FAILED":"PENDING";
        await c.ExecuteAsync(new CommandDefinition("UPDATE `wms_dispatch_packing_task` SET `consume_status`=@status,`last_update_time`=@now,`row_version`=`row_version`+1 WHERE `id`=@packingTaskId AND `consume_status`<>@status;",new{packingTaskId,status,now=DateTime.Now},cancellationToken:ct));
    }
}
