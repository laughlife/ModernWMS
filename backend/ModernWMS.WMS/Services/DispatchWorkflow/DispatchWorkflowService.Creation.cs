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
    /// 执行 CreateAsync 操作。
    /// </summary>
    public async Task<DispatchOrderDetailViewModel> CreateAsync(CreateDispatchOrderRequest request, CurrentUser currentUser,
        CancellationToken cancellationToken = default)
    {
        if (request.source_task_ids == null || request.source_task_ids.Any(x => x <= 0))
            throw new ArgumentException("source_task_ids must contain only positive task identities", nameof(request));
        var taskIds = request.source_task_ids.Distinct().OrderBy(x => x).ToArray();
        if (request.warehouse_id <= 0 || taskIds.Length == 0) throw new ArgumentException("warehouse_id and source_task_ids are required");
        if (taskIds.Length != 1) throw new ArgumentException("每个拣货单只能包含一个装箱任务", nameof(request));
        await _warehouseAccessService.EnsureAllowedAsync(request.warehouse_id, currentUser);
        var capability = await _sourceReader.VerifyCapabilityAsync(cancellationToken);
        if (!capability.IsSupported) throw new InvalidOperationException(capability.Error);
        var snapshots = await _sourceReader.ReadAsync(taskIds, cancellationToken);
        ValidateCreationSnapshots(taskIds, request.warehouse_id, snapshots);
        var key = TaskSetKey(taskIds);
        if (!string.IsNullOrWhiteSpace(request.idempotency_key)
            && !string.Equals(request.idempotency_key.Trim(), key, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("idempotency_key does not match the sorted source_task_ids set", nameof(request));

        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        try
        {
            var existing = await connection.QuerySingleOrDefaultAsync<DispatchOrderEntity>(new CommandDefinition(
                "SELECT * FROM `wms_dispatch_order` WHERE `create_idempotency_key`=@key LIMIT 1;", new { key }, transaction,
                cancellationToken: cancellationToken));
            if (existing != null)
            {
                if (existing.warehouse_id != request.warehouse_id) throw new InvalidOperationException("idempotent task set belongs to another warehouse");
                if (existing.status is DispatchOrderStatus.ManualCancelled or DispatchOrderStatus.SourceCancelled)
                {
                    // 回退/取消过的旧订单不再参与幂等重放：释放唯一键后同一任务组合可以重新建单。
                    // create_idempotency_key 列宽 64，使用哈希后的回收键避免超长与冲突。
                    await connection.ExecuteAsync(new CommandDefinition("""
                        UPDATE `wms_dispatch_order` SET `create_idempotency_key`=@recycledKey,`last_update_time`=@now,
                          `row_version`=`row_version`+1 WHERE `id`=@id;
                        """, new
                        {
                            recycledKey = HashText($"recycled:{existing.id}:{existing.create_idempotency_key}"),
                            now = DateTime.Now,
                            id = existing.id
                        }, transaction, cancellationToken: cancellationToken));
                }
                else
                {
                    await transaction.CommitAsync(cancellationToken);
                    return await LoadDetailAsync(existing.id, cancellationToken);
                }
            }
            var occupied = await FindOccupiedTaskIdsAsync(connection, transaction, taskIds, cancellationToken);
            if (occupied.Count > 0) throw new InvalidOperationException($"packing tasks already belong to an active order: {string.Join(',', occupied.Order())}");
            var runtime = await LoadInventoryRuntimeAsync(connection, transaction,
                currentUser.tenant_id, request.warehouse_id, cancellationToken);
            var bindingRows = await LoadCreationBindingRowsAsync(connection,transaction,
                currentUser.tenant_id,taskIds,runtime.Mode == CanonicalInventoryMode,cancellationToken);
            var bindingQty = bindingRows.GroupBy(x => (x.TaskId, x.ItemId))
                .ToDictionary(x => x.Key, x => x.Sum(row => row.LockedQty));
            foreach (var snapshot in snapshots)
            foreach (var item in snapshot.Items)
                if (!bindingQty.TryGetValue((snapshot.SourceTaskId, item.SourceItemId), out var lockedQty)
                    || lockedQty < item.Quantity)
                    throw new InvalidOperationException($"装箱任务{snapshot.TaskNo}存在未绑定库存的商品，不能生成拣货单");
            var availableSnapshots = BuildAvailableSnapshots(snapshots, bindingRows);
            var now = DateTime.Now;
            var order = new DispatchOrderEntity
            {
                dispatch_no=$"PK{now:yyyyMMddHHmmssfff}{Random.Shared.Next(100,1000)}",create_idempotency_key=key,
                warehouse_id=request.warehouse_id,status=DispatchOrderStatus.PendingPick,source_version=CombinedVersion(snapshots),
                source_snapshot=SnapshotJson(snapshots),tenant_id=currentUser.tenant_id,created_by=currentUser.user_id,
                creator=currentUser.user_name,create_time=now,last_update_time=now,row_version=0
            };
            order.id = await connection.ExecuteScalarAsync<int>(new CommandDefinition("""
                INSERT INTO `wms_dispatch_order`
                  (`dispatch_no`,`create_idempotency_key`,`warehouse_id`,`status`,`source_version`,`source_snapshot`,
                   `source_change_pending`,`pending_source_version`,`source_change_snapshot`,`accepted_source_version`,`adjudicated_source_version`,
                   `adjudicated_by_name`,`adjudication_reason`,`signed_by_name`,`notification_status`,`notification_attempt_count`,
                   `notification_last_error`,`tenant_id`,`created_by`,`creator`,`create_time`,`last_update_time`,`row_version`)
                VALUES (@dispatch_no,@create_idempotency_key,@warehouse_id,@status,@source_version,@source_snapshot,
                   0,'','','','','','','',0,0,'',@tenant_id,@created_by,@creator,@create_time,@last_update_time,@row_version);
                SELECT LAST_INSERT_ID();
                """, order, transaction, cancellationToken: cancellationToken));
            foreach (var snapshot in snapshots.OrderBy(x => x.SourceTaskId))
                await InsertTaskAsync(connection, transaction, order.id,
                    CreateTask(snapshot, null, now, bindingQty, availableSnapshots), cancellationToken);
            await EnsureCreationSourceUnchangedAsync(taskIds, snapshots, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return await LoadDetailAsync(order.id, cancellationToken);
        }
        catch (MySqlException exception)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            var concurrent = await connection.QuerySingleOrDefaultAsync<DispatchOrderEntity>(
                "SELECT * FROM `wms_dispatch_order` WHERE `create_idempotency_key`=@key LIMIT 1;", new { key });
            if (concurrent != null && concurrent.warehouse_id == request.warehouse_id) return await LoadDetailAsync(concurrent.id, cancellationToken);
            var occupied = await FindOccupiedTaskIdsAsync(connection, null, taskIds, cancellationToken);
            if (occupied.Count > 0) throw new InvalidOperationException($"packing tasks already belong to an active order: {string.Join(',', occupied.Order())}");
            throw new InvalidOperationException("dispatch order creation conflicted with another concurrent request", exception);
        }
    }

    private static async Task<List<CreationBindingRow>> LoadCreationBindingRowsAsync(
        System.Data.IDbConnection connection,IDbTransaction transaction,long tenantId,
        IReadOnlyCollection<long> taskIds,bool canonical,CancellationToken cancellationToken)
    {
        var sql = canonical ? """
                SELECT selection.`sellfox_task_id` AS TaskId,selection.`sellfox_item_id` AS ItemId,
                       selection.`stock_allocation_id` AS StockKey,selection.`qty` AS LockedQty,
                       allocation.`allocated_qty`-allocation.`occupied_qty`+selection.`qty` AS AvailableBeforeTask
                  FROM `wms_packing_task_stock_selection` selection
                  JOIN `wms_erp_stock_allocation` allocation
                    ON allocation.`tenant_id`=selection.`tenant_id`
                   AND allocation.`id`=selection.`stock_allocation_id`
                   AND allocation.`erp_stock_id`=selection.`erp_stock_id`
                 WHERE selection.`tenant_id`=@tenantId AND selection.`sellfox_task_id` IN @taskIds
                   AND selection.`status`='ACTIVE'
                   AND selection.`erp_stock_id` IS NOT NULL
                   AND selection.`stock_allocation_id` IS NOT NULL
                   AND allocation.`location_state`='ACTIVE'
                 ORDER BY selection.`stock_allocation_id`,selection.`sellfox_item_id`,selection.`id` FOR UPDATE;
                """ : """
                SELECT selection.`sellfox_task_id` AS TaskId,selection.`sellfox_item_id` AS ItemId,
                       selection.`stock_id` AS StockKey,selection.`qty` AS LockedQty,
                       GREATEST(0,CASE WHEN stock.`is_freeze`=1 THEN 0 ELSE stock.`qty`
                         -COALESCE((SELECT SUM(pick.`pick_qty`) FROM `wms_dispatchpicklist` pick
                           JOIN `wms_dispatchlist` detail ON detail.`id`=pick.`dispatchlist_id`
                           WHERE detail.`dispatch_status`>1 AND detail.`dispatch_status`<6 AND pick.`stock_id`=stock.`id`),0)
                         -COALESCE((SELECT SUM(process.`qty`) FROM `wms_stockprocessdetail` process
                           WHERE process.`is_update_stock`=0 AND process.`sku_id`=stock.`sku_id`
                             AND process.`goods_location_id`=stock.`goods_location_id` AND process.`goods_owner_id`=stock.`goods_owner_id`),0)
                         -COALESCE((SELECT SUM(move.`qty`) FROM `wms_stockmove` move
                           WHERE move.`move_status`=0 AND move.`sku_id`=stock.`sku_id`
                             AND move.`orig_goods_location_id`=stock.`goods_location_id` AND move.`goods_owner_id`=stock.`goods_owner_id`),0)
                         -COALESCE((SELECT SUM(other_selection.`qty`) FROM `wms_packing_task_stock_selection` other_selection
                           WHERE other_selection.`tenant_id`=@tenantId AND other_selection.`stock_id`=stock.`id`
                             AND other_selection.`status`='ACTIVE'),0)
                         +COALESCE((SELECT SUM(task_selection.`qty`) FROM `wms_packing_task_stock_selection` task_selection
                           WHERE task_selection.`tenant_id`=@tenantId AND task_selection.`stock_id`=stock.`id`
                             AND task_selection.`sellfox_task_id` IN @taskIds
                             AND task_selection.`status`='ACTIVE'),0) END) AS AvailableBeforeTask
                FROM `wms_packing_task_stock_selection` selection
                JOIN `wms_stock` stock ON stock.`id`=selection.`stock_id` AND stock.`tenant_id`=selection.`tenant_id`
                WHERE selection.`tenant_id`=@tenantId AND selection.`sellfox_task_id` IN @taskIds
                  AND selection.`status`='ACTIVE'
                ORDER BY selection.`stock_id`,selection.`sellfox_item_id`,selection.`id` FOR UPDATE;
                """;
        return (await connection.QueryAsync<CreationBindingRow>(new CommandDefinition(
            sql,new{tenantId,taskIds},transaction,cancellationToken:cancellationToken))).AsList();
    }

    private static async Task<List<long>> FindOccupiedTaskIdsAsync(System.Data.IDbConnection connection, IDbTransaction? transaction,
        IReadOnlyCollection<long> taskIds, CancellationToken cancellationToken) =>
        (await connection.QueryAsync<long>(new CommandDefinition("""
            SELECT `source_task_id` FROM `wms_dispatch_packing_task`
            WHERE `active_source_task_id` IS NOT NULL AND `active_source_task_id` IN @taskIds;
            """, new { taskIds }, transaction, cancellationToken: cancellationToken))).AsList();

    private async Task EnsureCreationSourceUnchangedAsync(IReadOnlyCollection<long> taskIds,
        IReadOnlyList<PackingTaskSourceSnapshot> snapshots, CancellationToken cancellationToken)
    {
        var commit = await _sourceReader.ReadAsync(taskIds, cancellationToken);
        if (!string.Equals(SnapshotJson(commit), SnapshotJson(snapshots), StringComparison.Ordinal))
            throw new InvalidOperationException("packing task source changed during dispatch order creation");
    }

    private static void ValidateCreationSnapshots(IReadOnlyCollection<long> requested, long warehouseId,
        IReadOnlyList<PackingTaskSourceSnapshot> snapshots)
    {
        if (!requested.Order().SequenceEqual(snapshots.Select(x => x.SourceTaskId).Order())) throw new InvalidOperationException("one or more packing tasks are missing");
        if (snapshots.Any(x => x.IsCancelled)) throw new InvalidOperationException("cancelled packing task cannot enter a WMS order");
        if (snapshots.Any(x => x.WarehouseId != warehouseId)) throw new InvalidOperationException("packing tasks from different warehouses cannot be merged");
    }

    private static DispatchPackingTaskEntity CreateTask(PackingTaskSourceSnapshot snapshot,
        IReadOnlyDictionary<long,int>? mappings, DateTime now,
        IReadOnlyDictionary<(long TaskId,long ItemId),int>? bindingQty = null,
        IReadOnlyDictionary<(long TaskId,long ItemId),int>? availableSnapshots = null)
    {
        var task = new DispatchPackingTaskEntity
        {
            task_no=snapshot.TaskNo,source_task_id=snapshot.SourceTaskId,source_task_no=snapshot.TaskNo,
            source_cartons_json=snapshot.CartonsJson,status=DispatchOrderStatus.PendingPick,expected_box_count=snapshot.Boxes.Count,
            source_version=snapshot.SourceVersion,stable_box_identity_verified=snapshot.Boxes.Count>0&&snapshot.Boxes.All(x=>!string.IsNullOrWhiteSpace(x.SourceBoxIdentity)),
            box_identity_validation_error=snapshot.Boxes.Count==0?"来源尚未提供物理箱，进入称重前必须同步并验证稳定箱ID":"",
            create_time=now,last_update_time=now,items=snapshot.Items.Select(x=>CreateItem(
                x,snapshot.SourceVersion,mappings,now,
                bindingQty?.GetValueOrDefault((snapshot.SourceTaskId,x.SourceItemId)),
                availableSnapshots?.GetValueOrDefault((snapshot.SourceTaskId,x.SourceItemId)))).ToList()
        };
        task.SetActiveState(true); return task;
    }

    private static DispatchPackingTaskItemEntity CreateItem(PackingTaskSourceItem item,string version,
        IReadOnlyDictionary<long,int>? mappings,DateTime now,int? requiredQty = null,int? availableQty = null)
    {
        var lockedQty = requiredQty ?? item.Quantity;
        if (item.Quantity <= 0 || lockedQty <= 0 || lockedQty % item.Quantity != 0)
            throw new InvalidOperationException($"商品 {item.CommoditySku} 的任务量与变体数据不一致");
        return new()
        {
        source_item_id=item.SourceItemId,source_commodity_id=item.CommodityId,wms_sku_id=mappings==null?null:MappedSkuId(item,mappings),
        commodity_sku=item.CommoditySku,commodity_name=item.CommodityName,fn_sku=item.FnSku,msku=item.Msku,
        required_qty=lockedQty,source_quantity_shipped=item.Quantity,source_stock_available=availableQty,variant_qty=lockedQty/item.Quantity,
        source_version=version,source_snapshot=item.SourceSnapshot,
        is_active=true,create_time=now,last_update_time=now
        };
    }

    private static IReadOnlyDictionary<(long TaskId,long ItemId),int> BuildAvailableSnapshots(
        IReadOnlyList<PackingTaskSourceSnapshot> snapshots,IReadOnlyList<CreationBindingRow> bindings)
    {
        var remainingByStock = bindings.GroupBy(x => x.StockKey)
            .ToDictionary(x => x.Key, x => x.First().AvailableBeforeTask);
        var result = new Dictionary<(long TaskId,long ItemId),int>();
        foreach (var snapshot in snapshots.OrderBy(x => x.SourceTaskId))
        foreach (var item in snapshot.Items.OrderBy(x => x.SourceItemId))
        {
            var itemBindings = bindings
                .Where(x => x.TaskId == snapshot.SourceTaskId && x.ItemId == item.SourceItemId)
                .ToList();
            foreach (var binding in itemBindings)
                remainingByStock[binding.StockKey] = Math.Max(0,
                    remainingByStock.GetValueOrDefault(binding.StockKey) - binding.LockedQty);
            result[(snapshot.SourceTaskId,item.SourceItemId)] = itemBindings
                .Select(x => x.StockKey).Distinct().Sum(stockId => remainingByStock.GetValueOrDefault(stockId));
        }
        return result;
    }

    private sealed class CreationBindingRow
    {
        public long TaskId { get; init; }
        public long ItemId { get; init; }
        public long StockKey { get; init; }
        public int LockedQty { get; init; }
        public int AvailableBeforeTask { get; init; }
    }

    private static async Task InsertTaskAsync(System.Data.IDbConnection c, IDbTransaction tx, int orderId,
        DispatchPackingTaskEntity task, CancellationToken ct)
    {
        task.dispatch_order_id=orderId;
        task.id=await c.ExecuteScalarAsync<int>(new CommandDefinition("""
            INSERT INTO `wms_dispatch_packing_task`
              (`dispatch_order_id`,`task_no`,`source_task_id`,`active_source_task_id`,`source_task_no`,`source_cartons_json`,`status`,
               `measured_box_count`,`expected_box_count`,`source_version`,`stable_box_identity_verified`,`box_identity_validation_error`,
               `is_active`,`writeback_status`,`writeback_request_hash`,`writeback_response`,`writeback_retry_count`,`create_time`,`last_update_time`,`row_version`)
            VALUES (@dispatch_order_id,@task_no,@source_task_id,@active_source_task_id,@source_task_no,@source_cartons_json,@status,
               @measured_box_count,@expected_box_count,@source_version,@stable_box_identity_verified,@box_identity_validation_error,
               @is_active,@writeback_status,@writeback_request_hash,@writeback_response,@writeback_retry_count,@create_time,@last_update_time,@row_version);
            SELECT LAST_INSERT_ID();
            """,task,tx,cancellationToken:ct));
        foreach(var item in task.items)
        {
            item.packing_task_id=task.id;
            item.id=await c.ExecuteScalarAsync<int>(new CommandDefinition("""
                INSERT INTO `wms_dispatch_packing_task_item`
                  (`packing_task_id`,`source_item_id`,`source_commodity_id`,`wms_sku_id`,`commodity_sku`,`commodity_name`,`fn_sku`,`msku`,
                   `required_qty`,`source_quantity_shipped`,`source_stock_available`,`source_version`,`source_snapshot`,`is_active`,`create_time`,`last_update_time`,`row_version`)
                VALUES (@packing_task_id,@source_item_id,@source_commodity_id,@wms_sku_id,@commodity_sku,@commodity_name,@fn_sku,@msku,
                   @required_qty,@source_quantity_shipped,@source_stock_available,@source_version,@source_snapshot,@is_active,@create_time,@last_update_time,@row_version);
                SELECT LAST_INSERT_ID();
                """,item,tx,cancellationToken:ct));
        }
    }
}
