using System.Data;
using Dapper;
using ModernWMS.Core.JWT;
using ModernWMS.WMS.Entities.Models;
using ModernWMS.WMS.Entities.ViewModels.DispatchWorkflow;
using ModernWMS.WMS.Entities.ViewModels.PackingTask;
using MySqlConnector;

namespace ModernWMS.WMS.Services.DispatchWorkflow;

/// <summary>
/// 表示 DispatchWorkflowService 类型。
/// </summary>
public partial class DispatchWorkflowService
{
    /// <summary>
    /// 执行 CompletePickingAsync 操作。
    /// </summary>
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
            var now=DateTime.Now;
            foreach(var task in tasks)
            {
                var snap=snapshots.Single(x=>x.SourceTaskId==task.source_task_id);
                if(snap.IsCancelled){await CancelTaskAsync(c,tx,task,order,user,now,ct);task.is_active=false;continue;}
                if(!string.Equals(task.source_version,snap.SourceVersion,StringComparison.Ordinal))
                {await RemoveTaskAllocationsAsync(c,tx,task,order,user,"PICK_REBUILD",ct);await RebuildTaskItemsAsync(c,tx,task,snap,now,ct);}
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
            if(items.Any(x=>x.required_qty is null or <=0))throw DispatchWorkflowCommandException.SourceChanged("packing task item has invalid required quantity");
            var allocations=await BuildStockPlanAsync(c,tx,items,ct);
            foreach(var item in items)
                await c.ExecuteAsync(new CommandDefinition("""
                    UPDATE `wms_dispatch_packing_task_item`
                    SET `wms_sku_id`=@wms_sku_id,`last_update_time`=@now,`row_version`=`row_version`+1
                    WHERE `id`=@id AND `is_active`=1;
                    """,new{wms_sku_id=(int?)null,now,item.id},tx,cancellationToken:ct));
            await EnsurePickingSourceAsync(tasks,snapshots,ct);
            var details=new Dictionary<int,int>();
            foreach(var item in items)
            {
                var detailId=await c.ExecuteScalarAsync<int>(new CommandDefinition("""
                    INSERT INTO `wms_dispatchlist` (`dispatch_order_id`,`packing_task_id`,`packing_task_item_id`,`dispatch_no`,`dispatch_status`,
                      `sku_id`,`qty`,`weight`,`volume`,`creator`,`create_time`,`damage_qty`,`lock_qty`,`picked_qty`,`intrasit_qty`,`package_qty`,`weighing_qty`,`actual_qty`,`sign_qty`,
                      `package_no`,`package_person`,`package_time`,`weighing_no`,`weighing_person`,`weighing_weight`,`weighing_length`,`weighing_width`,`weighing_height`,`weighing_volume`,
                      `waybill_no`,`carrier`,`carrier_unit`,`freightfee`,`last_update_time`,`pick_checker_id`,`pick_checker`)
                    VALUES (@orderId,@taskId,@itemId,@dispatchNo,3,@skuId,@qty,0,0,@name,@now,0,@qty,@qty,0,0,0,0,0,'','',@minDate,'','',0,0,0,0,0,'','','',0,@now,@userId,@name);
                    SELECT LAST_INSERT_ID();
                    """,new{orderId,taskId=item.packing_task_id,itemId=item.id,dispatchNo=order.dispatch_no,skuId=0,qty=item.required_qty,
                        userId=user.user_id,name=user.user_name,now,minDate=ModernWMS.Core.Utility.UtilConvert.MinDate},tx,cancellationToken:ct));
                details[item.id]=detailId;
            }
            foreach(var a in allocations)
                await c.ExecuteAsync(new CommandDefinition("""
                    INSERT INTO `wms_dispatchpicklist` (`dispatchlist_id`,`packing_task_item_id`,`stock_id`,`erp_stock_id`,`stock_allocation_id`,`reservation_id`,`reservation_item_id`,`goods_owner_id`,`goods_location_id`,`sku_id`,`pick_qty`,`picked_qty`,
                      `is_update_stock`,`last_update_time`,`series_number`,`picker_id`,`picker`,`expiry_date`,`price`,`putaway_date`)
                    VALUES (@detailId,@itemId,@stockId,@erpStockId,@allocationId,@reservationId,@reservationItemId,@ownerId,@locationId,@skuId,@quantity,@quantity,0,@now,@series,@userId,@name,@expiry,@price,@putaway);
                    """,new{detailId=details[a.Item.id],itemId=a.Item.id,stockId=(int?)null,erpStockId=a.ErpStockId,
                        allocationId=(long?)null,reservationId=a.ReservationId,
                        reservationItemId=a.ReservationItemId,ownerId=(int?)null,locationId=(int?)null,
                        skuId=(int?)null,a.Quantity,now,series=string.Empty,userId=user.user_id,name=user.user_name,
                        expiry=ModernWMS.Core.Utility.UtilConvert.MinDate,Price=0m,
                        putaway=ModernWMS.Core.Utility.UtilConvert.MinDate},tx,cancellationToken:ct));
            await c.ExecuteAsync(new CommandDefinition("""
                UPDATE `wms_packing_task_stock_selection`
                   SET `status`='TRANSFERRED',`operation_source`='DISPATCH_PICKING',
                       `last_update_time`=@now,`row_version`=`row_version`+1
                 WHERE `id` IN @selectionIds AND `status`='ACTIVE';
                """,new{selectionIds=allocations.Select(x=>x.SelectionId).Distinct().ToArray(),now},tx,cancellationToken:ct));
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

    private static async Task<List<PickingAllocation>> BuildStockPlanAsync(
        System.Data.IDbConnection c,IDbTransaction tx,
        IReadOnlyList<DispatchPackingTaskItemEntity> items,CancellationToken ct)
    {
        var taskIds=items.Select(x=>x.packing_task_id).Distinct().ToArray();
        var bindings=(await c.QueryAsync<BoundSelectionRow>(new CommandDefinition("""
            SELECT selection.`id`,selection.`sellfox_item_id`,selection.`erp_stock_id`,
                   selection.`reservation_id`,selection.`reservation_item_id`,selection.`qty`,
                   reservation_item.`status` AS reservation_status,
                   reservation_item.`remaining_qty` AS reservation_remaining_qty,
                   task.`id` AS packing_task_id
              FROM `wms_packing_task_stock_selection` selection
              JOIN `wms_dispatch_packing_task` task
                ON task.`source_task_id`=selection.`sellfox_task_id`
              JOIN `wms_dispatch_order` dispatch_order
                ON dispatch_order.`id`=task.`dispatch_order_id`
              JOIN `trk_stock` stock
                ON stock.`id`=selection.`erp_stock_id`
               AND stock.`warehouse_id`=dispatch_order.`warehouse_id`
               AND stock.`deleted`=b'0'
              LEFT JOIN `trk_stock_reservation_item` reservation_item
                ON reservation_item.`id`=selection.`reservation_item_id`
               AND reservation_item.`reservation_id`=selection.`reservation_id`
               AND reservation_item.`stock_id`=selection.`erp_stock_id`
               AND reservation_item.`deleted`=b'0'
             WHERE task.`id` IN @taskIds
               AND task.`is_active`=1
               AND selection.`status`='ACTIVE'
               AND selection.`erp_stock_id` IS NOT NULL
             ORDER BY selection.`erp_stock_id`,selection.`id`
             FOR UPDATE;
            """,new{taskIds},tx,cancellationToken:ct))).AsList();
        if(bindings.Count==0)
            throw DispatchWorkflowCommandException.StockShortage("装箱任务未绑定ERP库存");
        if(bindings.Any(x=>x.erp_stock_id<=0||x.reservation_id is null or <=0||x.reservation_item_id is null or <=0))
            throw DispatchWorkflowCommandException.StockShortage("装箱任务库存绑定缺少有效预占来源");
        if(bindings.GroupBy(x=>(x.reservation_id,x.reservation_item_id,x.erp_stock_id)).Any(group=>
        {
            var owner=group.First();
            return owner.reservation_remaining_qty is not >0
                || group.Sum(x=>(long)x.qty)>owner.reservation_remaining_qty.Value
                || group.Any(x=>x.reservation_status is not ("ACTIVE" or "PARTIALLY_SETTLED"));
        }))
            throw DispatchWorkflowCommandException.StockShortage("装箱任务库存预占已失效，请回退到装箱任务重新选择库存");

        var plan=new List<PickingAllocation>();
        foreach(var item in items)
        {
            var rows=bindings
                .Where(x=>x.packing_task_id==item.packing_task_id&&x.sellfox_item_id==item.source_item_id)
                .ToList();
            if(rows.Sum(x=>x.qty)!=item.required_qty)
                throw DispatchWorkflowCommandException.StockShortage($"装箱任务商品 {item.commodity_sku} 的绑定数量已变化");
            item.wms_sku_id=null;
            plan.AddRange(rows.Select(row=>new PickingAllocation(
                item,row.erp_stock_id,row.reservation_id,row.reservation_item_id,row.qty,row.id)));
        }
        return plan;
    }

    private sealed record PickingAllocation(
        DispatchPackingTaskItemEntity Item,long ErpStockId,long? ReservationId,
        long? ReservationItemId,int Quantity,int SelectionId)
    { }

    private sealed class BoundSelectionRow
    {
        public int id{get;init;}
        public int packing_task_id{get;init;}
        public long sellfox_item_id{get;init;}
        public long erp_stock_id{get;init;}
        public long? reservation_id{get;init;}
        public long? reservation_item_id{get;init;}
        public string? reservation_status{get;init;}
        public long? reservation_remaining_qty{get;init;}
        public int qty{get;init;}
    }
}

/// <summary>
/// 表示 DispatchWorkflowCommandException 类型。
/// </summary>
public sealed partial class DispatchWorkflowCommandException:InvalidOperationException
{
    private DispatchWorkflowCommandException(string code,string detail):base(string.IsNullOrWhiteSpace(detail)?code:$"{code}: {detail}")=>ErrorCode=code;
    /// <summary>
    /// 获取或设置 ErrorCode。
    /// </summary>
    public string ErrorCode{get;}
    /// <summary>
    /// 执行 SourceChanged 操作。
    /// </summary>
    public static DispatchWorkflowCommandException SourceChanged(string detail="packing task source changed during picking completion")=>new("SOURCE_CHANGED",detail);
    /// <summary>
    /// 执行 StockShortage 操作。
    /// </summary>
    public static DispatchWorkflowCommandException StockShortage(string detail)=>new("STOCK_SHORTAGE",detail);
    /// <summary>
    /// 执行 SkuMappingMissing 操作。
    /// </summary>
    public static DispatchWorkflowCommandException SkuMappingMissing(string detail)=>new("SKU_MAPPING_MISSING",detail);
    /// <summary>
    /// 执行 SkuMappingConflict 操作。
    /// </summary>
    public static DispatchWorkflowCommandException SkuMappingConflict(string detail)=>new("SKU_MAPPING_CONFLICT",detail);
    /// <summary>
    /// 执行 ConcurrencyConflict 操作。
    /// </summary>
    public static DispatchWorkflowCommandException ConcurrencyConflict()=>new("CONCURRENCY_CONFLICT","row version does not match");
    /// <summary>
    /// 执行 StatusNotAllowed 操作。
    /// </summary>
    public static DispatchWorkflowCommandException StatusNotAllowed()=>new("STATUS_NOT_ALLOWED","only a pending-pick order can be completed");
}
