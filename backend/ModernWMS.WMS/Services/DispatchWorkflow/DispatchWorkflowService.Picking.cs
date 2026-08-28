using System.Data;
using Dapper;
using ModernWMS.Core.JWT;
using ModernWMS.WMS.Entities.Models;
using ModernWMS.WMS.Entities.ViewModels.DispatchWorkflow;
using ModernWMS.WMS.Entities.ViewModels.PackingTask;
using ModernWMS.WMS.Services.PackingTask;
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
        await using(var replayConnection=await _connectionFactory.OpenConnectionAsync(ct))
        {
            var replayOrder=await replayConnection.QuerySingleOrDefaultAsync<DispatchOrderEntity>(new CommandDefinition(
                "SELECT * FROM `wms_dispatch_order` WHERE `id`=@orderId;",new{orderId},cancellationToken:ct))
                ??throw new KeyNotFoundException($"dispatch order not found: {orderId}");
            await _warehouseAccessService.EnsureAllowedAsync(replayOrder.warehouse_id,user);
            var replay=await FindOperationAsync(replayConnection,null,orderId,
                DispatchWorkflowOperation.CompletePicking,requestId,ct);
            if(replay?.result_status==DispatchWorkflowOperationResultStatus.Succeeded)return FromLedger(replay);
        }
        var canonicalPlans=await LoadCanonicalPickingPlansAsync(orderId,user,ct);
        await using var c=await _connectionFactory.OpenConnectionAsync(ct);
        await using var tx=await c.BeginTransactionAsync(IsolationLevel.Serializable,ct);
        try
        {
            var previous=await FindOperationAsync(c,tx,orderId,DispatchWorkflowOperation.CompletePicking,requestId,ct);
            if(previous?.result_status==DispatchWorkflowOperationResultStatus.Succeeded){await tx.CommitAsync(ct);return FromLedger(previous);}
            var order=await LoadReconcileOrderAsync(c,tx,orderId,ct);
            await _warehouseAccessService.EnsureAllowedAsync(order.warehouse_id,user);
            var runtime=await LoadInventoryRuntimeAsync(c,tx,order.warehouse_id,ct);
            var canonical=runtime.Mode==CanonicalInventoryMode;
            if(order.status!=DispatchOrderStatus.PendingPick)throw DispatchWorkflowCommandException.StatusNotAllowed();
            if(order.row_version!=request.row_version)throw DispatchWorkflowCommandException.ConcurrencyConflict();
            var tasks=order.packing_tasks.Where(x=>x.is_active).ToList();
            var snapshots=await _sourceReader.ReadAsync(tasks.Select(x=>x.source_task_id).ToArray(),ct);
            if(snapshots.Count!=tasks.Count||snapshots.Where(x=>!x.IsCancelled).Any(x=>x.WarehouseId!=order.warehouse_id))throw DispatchWorkflowCommandException.SourceChanged();
            var now=DateTime.Now;
            foreach(var task in tasks)
            {
                var snap=snapshots.Single(x=>x.SourceTaskId==task.source_task_id);
                if(snap.IsCancelled){await CancelTaskAsync(c,tx,task,order,user,canonical,now,ct);task.is_active=false;continue;}
                if(!string.Equals(task.source_version,snap.SourceVersion,StringComparison.Ordinal))
                {await RemoveTaskAllocationsAsync(c,tx,task,order,user,canonical,"PICK_REBUILD",ct);await RebuildTaskItemsAsync(c,tx,task,snap,now,ct);}
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
            var allocations=await BuildAllocationPlanAsync(c,tx,items,canonical,canonicalPlans,ct);
            foreach(var item in items)
                await c.ExecuteAsync(new CommandDefinition("""
                    UPDATE `wms_dispatch_packing_task_item`
                    SET `wms_sku_id`=@wms_sku_id,`required_qty`=@required_qty,
                        `erp_stock_plan_row_version`=@erp_stock_plan_row_version,
                        `last_update_time`=@now,`row_version`=`row_version`+1
                    WHERE `id`=@id AND `is_active`=1;
                    """,new{item.wms_sku_id,item.required_qty,item.erp_stock_plan_row_version,now,item.id},tx,cancellationToken:ct));
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
                    """,new{orderId,taskId=item.packing_task_id,itemId=item.id,dispatchNo=order.dispatch_no,skuId=item.wms_sku_id,qty=item.required_qty,
                        name=user.user_name,now,minDate=ModernWMS.Core.Utility.UtilConvert.MinDate},tx,cancellationToken:ct));
                details[item.id]=detailId;
            }
            foreach(var a in allocations)
                await c.ExecuteAsync(new CommandDefinition("""
                    INSERT INTO `wms_dispatchpicklist` (`dispatchlist_id`,`packing_task_item_id`,`stock_id`,`erp_stock_id`,`stock_allocation_id`,`reservation_id`,`reservation_item_id`,`goods_owner_id`,`goods_location_id`,`sku_id`,`pick_qty`,`picked_qty`,
                      `is_update_stock`,`last_update_time`,`series_number`,`picker_id`,`picker`,`expiry_date`,`price`,`putaway_date`)
                    VALUES (@detailId,@itemId,@stockId,@erpStockId,@allocationId,@reservationId,@reservationItemId,@ownerId,@locationId,@skuId,@quantity,@quantity,0,@now,@series,@userId,@name,@expiry,@price,@putaway);
                    """,new{detailId=details[a.Item.id],itemId=a.Item.id,a.StockId,erpStockId=a.ErpStockId,
                        allocationId=a.StockAllocationId,reservationId=a.ReservationId,
                        reservationItemId=a.ReservationItemId,ownerId=a.GoodsOwnerId,locationId=a.GoodsLocationId,
                        skuId=a.SkuId,a.Quantity,now,series=a.SeriesNumber,userId=user.user_id,name=user.user_name,
                        expiry=a.ExpiryDate,a.Price,putaway=a.PutawayDate},tx,cancellationToken:ct));
            if(!canonical)
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

    private async Task<IReadOnlyDictionary<(int TaskId,long ItemId),ErpPackingStockPlan>?> LoadCanonicalPickingPlansAsync(
        int orderId,CurrentUser user,CancellationToken ct)
    {
        await using var c=await _connectionFactory.OpenConnectionAsync(ct);
        var order=await c.QuerySingleOrDefaultAsync<DispatchOrderEntity>(new CommandDefinition(
            "SELECT * FROM `wms_dispatch_order` WHERE `id`=@orderId;",new{orderId},cancellationToken:ct))
            ??throw new KeyNotFoundException($"dispatch order not found: {orderId}");
        await _warehouseAccessService.EnsureAllowedAsync(order.warehouse_id,user);
        var runtime=await LoadInventoryRuntimeAsync(c,null,order.warehouse_id,ct);
        if(runtime.Mode!=CanonicalInventoryMode)return null;
        var client=_erpPackingStockClient
            ??throw new InvalidOperationException("ERP 装箱库存客户端未注册，统一模式拒绝生成拣货明细");
        var rows=(await c.QueryAsync<CanonicalPickingPlanRow>(new CommandDefinition("""
            SELECT task.`id` TaskId,task.`source_task_id` SourceTaskId,item.`source_item_id` SourceItemId
              FROM `wms_dispatch_packing_task` task
              JOIN `wms_dispatch_packing_task_item` item ON item.`packing_task_id`=task.`id` AND item.`is_active`=1
             WHERE task.`dispatch_order_id`=@orderId AND task.`is_active`=1
             ORDER BY task.`id`,item.`source_item_id`;
            """,new{orderId},cancellationToken:ct))).AsList();
        if(rows.Count==0)throw DispatchWorkflowCommandException.SourceChanged("packing task has no active item to pick");
        var actorId=user.user_id.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var actorName=string.IsNullOrWhiteSpace(user.user_name)?$"用户{user.user_id}":user.user_name.Trim();
        var result=new Dictionary<(int TaskId,long ItemId),ErpPackingStockPlan>();
        foreach(var row in rows)
        {
            var response=await client.GetPlanAsync(new ErpPackingStockPlanQuery(
                row.SourceTaskId,row.SourceItemId,actorId,actorName),ct);
            if(!response.IsSuccess||response.Data==null)
                throw DispatchWorkflowCommandException.StockShortage(response.ErrorMessage);
            var plan=response.Data;
            var remaining=plan.activeBindings.Where(x=>x.remainingQty>0).Sum(x=>x.remainingQty);
            if(!string.Equals(plan.status,"SELECTED",StringComparison.Ordinal)
               ||plan.requiredQty<=0||plan.reservedQty!=plan.requiredQty||plan.shortageQty!=0
               ||remaining!=plan.requiredQty)
                throw DispatchWorkflowCommandException.StockShortage(
                    $"装箱任务商品 {row.SourceItemId} 尚未完成ERP库存预占");
            result.Add((row.TaskId,row.SourceItemId),plan);
        }
        return result;
    }

    private static async Task<List<PickingAllocation>> BuildAllocationPlanAsync(System.Data.IDbConnection c,IDbTransaction tx,IReadOnlyList<DispatchPackingTaskItemEntity> items,bool canonical,
        IReadOnlyDictionary<(int TaskId,long ItemId),ErpPackingStockPlan>? canonicalPlans,CancellationToken ct)
    {
        var taskIds=items.Select(x=>x.packing_task_id).Distinct().ToArray();
        if(canonical)return await BuildCanonicalAllocationPlanAsync(c,tx,items,
            canonicalPlans??throw DispatchWorkflowCommandException.StockShortage("ERP装箱库存计划未加载"),ct);
        const string bindingSql="""
            SELECT s.`id`,s.`sellfox_item_id`,s.`stock_id`,s.`erp_stock_id`,s.`stock_allocation_id`,
                   s.`reservation_id`,s.`reservation_item_id`,
                   s.`wms_sku_id`,s.`qty`,t.`id` AS packing_task_id
            FROM `wms_packing_task_stock_selection` s
            INNER JOIN `wms_dispatch_packing_task` t ON t.`source_task_id`=s.`sellfox_task_id`
            WHERE t.`id` IN @taskIds
              AND s.`status`='ACTIVE'
            ORDER BY s.`stock_id`,s.`id` FOR UPDATE;
            """;
        var bindings=(await c.QueryAsync<BoundSelectionRow>(new CommandDefinition(
            bindingSql,new{taskIds},tx,cancellationToken:ct))).AsList();
        if(bindings.Count==0)throw DispatchWorkflowCommandException.StockShortage("装箱任务未绑定库存");
        var stockIds=bindings.Select(x=>x.stock_id).Distinct().ToArray();
        var stocks=(await c.QueryAsync<AvailableStockRow>(new CommandDefinition("""
            SELECT s.*,(CASE WHEN s.`is_freeze`=1 THEN 0 ELSE s.`qty`
              -COALESCE((SELECT SUM(p.`pick_qty`) FROM `wms_dispatchpicklist` p JOIN `wms_dispatchlist` d ON d.`id`=p.`dispatchlist_id` WHERE d.`dispatch_status`>1 AND d.`dispatch_status`<6 AND p.`stock_id`=s.`id`),0)
              -COALESCE((SELECT SUM(p.`qty`) FROM `wms_stockprocessdetail` p WHERE p.`is_update_stock`=0 AND p.`sku_id`=s.`sku_id` AND p.`goods_location_id`=s.`goods_location_id` AND p.`goods_owner_id`=s.`goods_owner_id`),0)
              -COALESCE((SELECT SUM(m.`qty`) FROM `wms_stockmove` m WHERE m.`move_status`=0 AND m.`sku_id`=s.`sku_id` AND m.`orig_goods_location_id`=s.`goods_location_id` AND m.`goods_owner_id`=s.`goods_owner_id`),0)
              -COALESCE((SELECT SUM(ps.`qty`) FROM `wms_packing_task_stock_selection` ps WHERE ps.`stock_id`=s.`id` AND ps.`status`='ACTIVE'),0) END) available_qty
            FROM `wms_stock` s WHERE s.`id` IN @stockIds
            ORDER BY s.`id` FOR UPDATE;
            """,new{stockIds},tx,cancellationToken:ct))).AsList();
        var stockById=stocks.ToDictionary(x=>x.id);var plan=new List<PickingAllocation>();
        foreach(var item in items)
        {
            var rows=bindings.Where(x=>x.packing_task_id==item.packing_task_id&&x.sellfox_item_id==item.source_item_id).ToList();
            if(rows.Sum(x=>x.qty)!=item.required_qty)throw DispatchWorkflowCommandException.StockShortage($"装箱任务商品 {item.commodity_sku} 的绑定数量已变化");
            var selectedSkuIds=rows.Where(x=>x.wms_sku_id>0).Select(x=>x.wms_sku_id).Distinct().ToArray();
            if(selectedSkuIds.Length!=1)
                throw DispatchWorkflowCommandException.SkuMappingConflict($"装箱任务商品 {item.commodity_sku} 绑定了多个库存SKU");
            item.wms_sku_id=selectedSkuIds[0];
            foreach(var row in rows)
            {
                if(!stockById.TryGetValue(row.stock_id,out var stock)||stock.sku_id!=row.wms_sku_id||stock.sku_id!=item.wms_sku_id)
                    throw DispatchWorkflowCommandException.StockShortage($"装箱任务商品 {item.commodity_sku} 的绑定库存已变化");
                if(stock.is_freeze||stock.available_qty<0)throw DispatchWorkflowCommandException.StockShortage($"装箱任务商品 {item.commodity_sku} 可用量不足");
                plan.Add(new(item,stock.id,null,null,null,null,stock.goods_owner_id,stock.goods_location_id,
                    stock.sku_id,stock.series_number,stock.expiry_date,stock.price,stock.putaway_date,row.qty,row.id));
            }
        }
        return plan;
    }
    private static async Task<List<PickingAllocation>> BuildCanonicalAllocationPlanAsync(
        System.Data.IDbConnection c,IDbTransaction tx,
        IReadOnlyList<DispatchPackingTaskItemEntity> items,
        IReadOnlyDictionary<(int TaskId,long ItemId),ErpPackingStockPlan> plans,CancellationToken ct)
    {
        var bindings=items.SelectMany(item=>plans.TryGetValue((item.packing_task_id,item.source_item_id),out var plan)
                ? plan.activeBindings.Where(binding=>binding.remainingQty>0)
                : [])
            .ToList();
        if(bindings.Count==0||bindings.Any(x=>x.erpStockId<=0||x.allocationId<=0||x.goodsOwnerId<=0||x.remainingQty<=0))
            throw DispatchWorkflowCommandException.StockShortage("ERP装箱库存计划没有有效预占绑定");
        var allocationIds=bindings.Select(x=>x.allocationId).Distinct().OrderBy(x=>x).ToArray();
        var stocks=(await c.QueryAsync<CanonicalPickingStock>(new CommandDefinition("""
            SELECT allocation.`id` StockAllocationId,allocation.`erp_stock_id` ErpStockId,
                   map.`wms_sku_id` SkuId,allocation.`goods_owner_id` GoodsOwnerId,
                   allocation.`goods_location_id` GoodsLocationId,allocation.`series_number` SeriesNumber,
                   allocation.`expiry_date` ExpiryDate,allocation.`price` Price,
                   allocation.`putaway_date` PutawayDate,allocation.`allocated_qty` AllocatedQty,
                   allocation.`occupied_qty` OccupiedQty,allocation.`location_state` LocationState
              FROM `wms_erp_stock_allocation` allocation
              JOIN `trk_stock` stock ON stock.`id`=allocation.`erp_stock_id` AND stock.`deleted`=b'0'
              JOIN `wms_erp_commodity_map` map ON map.`erp_commodity_id`=stock.`commodity_id` AND map.`wms_sku_id`>0
             WHERE allocation.`id` IN @allocationIds
             ORDER BY allocation.`erp_stock_id`,allocation.`id` FOR UPDATE;
            """,new{allocationIds},tx,cancellationToken:ct))).AsList();
        if(stocks.Count!=allocationIds.Length||stocks.Any(x=>x.LocationState!="ACTIVE"||x.AllocatedQty<x.OccupiedQty))
            throw DispatchWorkflowCommandException.StockShortage("ERP库存位置分配不存在、不可用或已变更");
        var byId=stocks.ToDictionary(x=>x.StockAllocationId);var plan=new List<PickingAllocation>();
        foreach(var group in bindings.GroupBy(x=>x.allocationId))
            if(byId[group.Key].OccupiedQty<group.Sum(x=>x.remainingQty))
                throw DispatchWorkflowCommandException.StockShortage("ERP库存位置分配的预占数量不足");
        foreach(var item in items)
        {
            if(!plans.TryGetValue((item.packing_task_id,item.source_item_id),out var itemPlan))
                throw DispatchWorkflowCommandException.StockShortage($"装箱任务商品 {item.commodity_sku} 的ERP库存计划不存在");
            var rows=itemPlan.activeBindings.Where(x=>x.remainingQty>0).ToList();
            if(itemPlan.requiredQty<=0||itemPlan.requiredQty>int.MaxValue||rows.Sum(x=>x.remainingQty)!=itemPlan.requiredQty)
                throw DispatchWorkflowCommandException.StockShortage($"装箱任务商品 {item.commodity_sku} 的ERP预占数量已变化");
            item.required_qty=checked((int)itemPlan.requiredQty);
            item.erp_stock_plan_row_version=itemPlan.rowVersion;
            var skuIds=rows.Select(x=>byId.GetValueOrDefault(x.allocationId)?.SkuId??0).Where(x=>x>0).Distinct().ToArray();
            if(skuIds.Length!=1)throw DispatchWorkflowCommandException.SkuMappingConflict($"装箱任务商品 {item.commodity_sku} 绑定了多个库存SKU");
            item.wms_sku_id=skuIds[0];
            foreach(var row in rows)
            {
                var stock=byId.GetValueOrDefault(row.allocationId);
                if(stock==null||stock.ErpStockId!=row.erpStockId||stock.GoodsOwnerId!=row.goodsOwnerId||stock.SkuId!=item.wms_sku_id)
                    throw DispatchWorkflowCommandException.StockShortage($"装箱任务商品 {item.commodity_sku} 的ERP库存绑定已变化");
                plan.Add(new(item,0,stock.ErpStockId,stock.StockAllocationId,null,
                    null,stock.GoodsOwnerId,
                    stock.GoodsLocationId,stock.SkuId,stock.SeriesNumber,
                    stock.ExpiryDate??ModernWMS.Core.Utility.UtilConvert.MinDate,stock.Price,
                    stock.PutawayDate??ModernWMS.Core.Utility.UtilConvert.MinDate,checked((int)row.remainingQty),0));
            }
        }
        return plan;
    }
    private sealed record PickingAllocation(DispatchPackingTaskItemEntity Item,int StockId,long? ErpStockId,
        long? StockAllocationId,long? ReservationId,long? ReservationItemId,
        int GoodsOwnerId,int? GoodsLocationId,int SkuId,string SeriesNumber,
        DateTime? ExpiryDate,decimal Price,DateTime? PutawayDate,int Quantity,int SelectionId);
    private sealed class BoundSelectionRow{public int id{get;init;}public int packing_task_id{get;init;}public long sellfox_item_id{get;init;}public int stock_id{get;init;}public long? erp_stock_id{get;init;}public long? stock_allocation_id{get;init;}public long? reservation_id{get;init;}public long? reservation_item_id{get;init;}public int wms_sku_id{get;init;}public int qty{get;init;}}
    private sealed class CanonicalPickingStock{public long StockAllocationId{get;init;}public long ErpStockId{get;init;}public int SkuId{get;init;}public int GoodsOwnerId{get;init;}public int? GoodsLocationId{get;init;}public string SeriesNumber{get;init;}=string.Empty;public DateTime? ExpiryDate{get;init;}public decimal Price{get;init;}public DateTime? PutawayDate{get;init;}public long AllocatedQty{get;init;}public long OccupiedQty{get;init;}public string LocationState{get;init;}=string.Empty;}
    private sealed class CanonicalPickingPlanRow{public int TaskId{get;init;}public long SourceTaskId{get;init;}public long SourceItemId{get;init;}}
    private sealed class AvailableStockRow:StockEntity{public int available_qty{get;set;}}
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
