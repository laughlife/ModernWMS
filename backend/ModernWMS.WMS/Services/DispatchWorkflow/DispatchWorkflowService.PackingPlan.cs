using System.Data;
using Dapper;
using ModernWMS.Core.JWT;
using ModernWMS.WMS.Entities.Models;
using ModernWMS.WMS.Entities.ViewModels.DispatchWorkflow;

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
    /// 获取任务创建人在当前ERP仓库可用于实际装箱的库存。
    /// </summary>
    public async Task<List<ActualPackingStockViewModel>> GetActualPackingStockAsync(
        int orderId,int taskId,string keyword,CurrentUser user,CancellationToken ct=default)
    {
        await using var c=await _connectionFactory.OpenConnectionAsync(ct);
        var order=await c.QuerySingleOrDefaultAsync<DispatchOrderEntity>(new CommandDefinition(
            "SELECT * FROM `wms_dispatch_order` WHERE `id`=@orderId;",new{orderId},cancellationToken:ct))
            ??throw new KeyNotFoundException($"dispatch order not found: {orderId}");
        await _warehouseAccessService.EnsureAllowedAsync(order.warehouse_id,user);
        var taskExists=await c.ExecuteScalarAsync<bool>(new CommandDefinition("""
            SELECT EXISTS(SELECT 1 FROM `wms_dispatch_packing_task`
             WHERE `id`=@taskId AND `dispatch_order_id`=@orderId AND `is_active`=1);
            """,new{taskId,orderId},cancellationToken:ct));
        if(!taskExists)throw new KeyNotFoundException($"packing task not found: {taskId}");
        var normalized=(keyword??string.Empty).Trim();if(normalized.Length>100)normalized=normalized[..100];
        var ownerCandidates=(await c.QueryAsync<PackingTaskOwnerCandidate>(new CommandDefinition("""
            SELECT users.`id` UserId,users.`nickname` Name
              FROM `wms_dispatch_packing_task` dispatch_task
              JOIN `ruiyi_sellfox_packing_task` source_task
                ON source_task.`sellfox_task_id`=dispatch_task.`source_task_id`
              JOIN `system_users` users ON users.`nickname`=source_task.`create_name`
               AND users.`deleted`=0 AND users.`status`=0
             WHERE dispatch_task.`id`=@taskId AND dispatch_task.`dispatch_order_id`=@orderId
             ORDER BY users.`id` LIMIT 2;
            """,new{taskId,orderId},cancellationToken:ct))).AsList();
        var ownerId=PackingTaskOwnerPolicy.Resolve(
            ownerCandidates.FirstOrDefault()?.Name,ownerCandidates);
        return (await c.QueryAsync<ActualPackingStockViewModel>(new CommandDefinition("""
            SELECT stock.`id` erp_stock_id,stock.`commodity_id`,stock.`order_user_id`,
                   @ownerName order_user_name,COALESCE(stock.`commodity_sku`,'') sku_code,
                   COALESCE(stock.`commodity_name`,'') commodity_name,stock.`available_qty`
              FROM `trk_stock` stock
             WHERE stock.`warehouse_id`=@warehouseId AND stock.`order_user_id`=@ownerId
               AND stock.`deleted`=b'0'
               AND (@keyword='' OR stock.`commodity_sku` LIKE CONCAT('%',@keyword,'%')
                 OR stock.`commodity_name` LIKE CONCAT('%',@keyword,'%'))
             ORDER BY stock.`id`
             LIMIT 100;
            """,new{warehouseId=order.warehouse_id,ownerId,
                ownerName=ownerCandidates.Single().Name,keyword=normalized},cancellationToken:ct))).AsList();
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
            var stockIds=r.boxes.SelectMany(x=>x.items).Select(x=>x.erp_stock_id)
                .Where(x=>x>0).Distinct().Order().ToArray();
            var identities=await LoadActualPackingStockIdentitiesAsync(c,tx,stockIds,ct);
            ValidateDraft(r.boxes,aggregate.Items,identities,order.warehouse_id);
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
                foreach(var item in draft.items)
                {
                    var stock=identities[item.erp_stock_id];
                    await c.ExecuteAsync(new CommandDefinition("""
                        INSERT INTO `wms_weighing_box_item`
                          (`weighing_box_id`,`client_line_key`,`packing_task_item_id`,`wms_sku_id`,
                           `erp_stock_id`,`stock_allocation_id`,`goods_owner_id`,`goods_location_id`,
                           `sku_code`,`commodity_name`,`actual_qty`,`dispatchpicklist_id`,
                           `create_time`,`last_update_time`,`row_version`)
                        VALUES (@boxId,@clientLineKey,@itemId,NULL,@erpStockId,NULL,
                           NULL,NULL,@skuCode,@commodityName,@actualQty,NULL,@now,@now,0);
                        """,new{boxId,clientLineKey=item.client_line_key,itemId=item.packing_task_item_id,
                            erpStockId=stock.ErpStockId,skuCode=stock.SkuCode,
                            commodityName=stock.CommodityName,actualQty=item.actual_qty,now},tx,cancellationToken:ct));
                }
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
        =>await ConfirmActualPackingCoreAsync(orderId,taskId,r,user,ct);

    private static async Task<PackingPlanAggregate> LoadPackingPlanForUpdateAsync(IDbConnection c,IDbTransaction tx,int orderId,int taskId,CancellationToken ct)
    {
        using var grid=await c.QueryMultipleAsync(new CommandDefinition("SELECT * FROM `wms_dispatch_order` WHERE `id`=@orderId FOR UPDATE;SELECT * FROM `wms_dispatch_packing_task` WHERE `id`=@taskId AND `dispatch_order_id`=@orderId AND `is_active`=1 FOR UPDATE;SELECT * FROM `wms_dispatch_packing_task_item` WHERE `packing_task_id`=@taskId AND `is_active`=1 FOR UPDATE;SELECT * FROM `wms_weighing_box` WHERE `packing_task_id`=@taskId AND `is_invalidated`=0 FOR UPDATE;",new{orderId,taskId},tx,cancellationToken:ct));
        var order=await grid.ReadSingleAsync<DispatchOrderEntity>();var task=await grid.ReadSingleAsync<DispatchPackingTaskEntity>();var items=(await grid.ReadAsync<DispatchPackingTaskItemEntity>()).AsList();var boxes=(await grid.ReadAsync<WeighingBoxEntity>()).AsList();var ids=boxes.Select(x=>x.id).ToArray();var boxItems=ids.Length==0?new List<WeighingBoxItemEntity>():(await c.QueryAsync<WeighingBoxItemEntity>(new CommandDefinition("SELECT * FROM `wms_weighing_box_item` WHERE `weighing_box_id` IN @ids FOR UPDATE;",new{ids},tx,cancellationToken:ct))).AsList();return new(order,task,items,boxes,boxItems);
    }
    private static async Task<IReadOnlyDictionary<long,ActualPackingStockIdentity>> LoadActualPackingStockIdentitiesAsync(
        IDbConnection c,IDbTransaction tx,IReadOnlyCollection<long> stockIds,CancellationToken ct)
    {
        if(stockIds.Count==0)return new Dictionary<long,ActualPackingStockIdentity>();
        var rows=(await c.QueryAsync<ActualPackingStockIdentity>(new CommandDefinition("""
            SELECT stock.`id` ErpStockId,stock.`commodity_id` CommodityId,
                   stock.`order_user_id` OrderUserId,stock.`warehouse_id` WarehouseId,
                   COALESCE(stock.`commodity_sku`,'') SkuCode,
                   COALESCE(stock.`commodity_name`,'') CommodityName,
                   stock.`available_qty` AvailableQty
              FROM `trk_stock` stock
             WHERE stock.`id` IN @stockIds AND stock.`deleted`=b'0'
             ORDER BY stock.`id` FOR UPDATE;
            """,new{stockIds},tx,cancellationToken:ct))).AsList();
        return rows.GroupBy(x=>x.ErpStockId).ToDictionary(x=>x.Key,x=>x.First());
    }
    private static void ValidateDraft(IReadOnlyCollection<PackingPlanBoxViewModel> boxes,
        IReadOnlyCollection<DispatchPackingTaskItemEntity> items,
        IReadOnlyDictionary<long,ActualPackingStockIdentity> identities,long warehouseId)
    {
        var itemIds=items.Select(x=>x.id).ToHashSet();
        foreach(var box in boxes)
            ActualPackingLinePolicy.ValidateBox(box.items.Select(x=>new ActualPackingDraftLine(
                x.client_line_key,x.packing_task_item_id,x.erp_stock_id,x.actual_qty)).ToArray(),
                itemIds,identities,warehouseId);
    }
    private static string MeasurementStatus(PackingPlanBoxViewModel b)=>b.weight>0&&b.length>0&&b.width>0&&b.height>0?"MEASURED":"UNMEASURED";
    private static void ValidatePackingPlanCommand(int orderId,int taskId,string requestId,long orderVersion,long taskVersion){if(orderId<=0||taskId<=0||string.IsNullOrWhiteSpace(requestId)||requestId.Length>64||orderVersion<0||taskVersion<0)throw new ArgumentException("order, task, request id and versions are required");}
    private static PackingPlanViewModel ToPackingPlan(DispatchOrderEntity o,DispatchPackingTaskEntity t,IReadOnlyCollection<DispatchPackingTaskItemEntity> items,IReadOnlyCollection<WeighingBoxEntity> boxes,IReadOnlyCollection<WeighingBoxItemEntity> boxItems)=>new(){order_id=o.id,packing_task_id=t.id,packing_task_no=t.source_task_no,packing_plan_status=t.packing_plan_status,row_version=o.row_version,task_row_version=t.row_version,items=items.Select(i=>new PackingPlanItemViewModel{id=i.id,commodity_sku=i.commodity_sku,commodity_name=i.commodity_name,fn_sku=i.fn_sku,msku=i.msku,main_image=SourceMainImage(i.source_snapshot),task_qty=i.source_quantity_shipped??0,variant_qty=i.variant_qty??0,required_qty=i.required_qty??0,actual_packed_task_qty=i.actual_packed_task_qty,actual_packed_required_qty=i.actual_packed_required_qty}).ToList(),boxes=boxes.Select(b=>new PackingPlanBoxViewModel{id=b.id,client_key=$"box-{b.id}",box_sequence=b.box_sequence,weight=b.weight,length=b.length,width=b.width,height=b.height,row_version=b.row_version,items=boxItems.Where(x=>x.weighing_box_id==b.id).Select(x=>new PackingPlanBoxItemViewModel{client_line_key=x.client_line_key,packing_task_item_id=x.packing_task_item_id,erp_stock_id=x.erp_stock_id,sku_code=x.sku_code,commodity_name=x.commodity_name,actual_qty=x.actual_qty,dispatchpicklist_id=x.dispatchpicklist_id}).ToList()}).ToList()};
    private sealed record PackingPlanAggregate(DispatchOrderEntity Order,DispatchPackingTaskEntity Task,List<DispatchPackingTaskItemEntity> Items,List<WeighingBoxEntity> Boxes,List<WeighingBoxItemEntity> BoxItems);
}
