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
    public async Task<DispatchOrderDetailViewModel> CreateAsync(CreateDispatchOrderRequest request, CurrentUser currentUser,
        CancellationToken cancellationToken = default)
    {
        if (request.source_task_ids == null || request.source_task_ids.Any(x => x <= 0))
            throw new ArgumentException("source_task_ids must contain only positive task identities", nameof(request));
        var taskIds = request.source_task_ids.Distinct().OrderBy(x => x).ToArray();
        if (request.warehouse_id <= 0 || taskIds.Length == 0) throw new ArgumentException("warehouse_id and source_task_ids are required");
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
                await transaction.CommitAsync(cancellationToken);
                return await LoadDetailAsync(existing.id, cancellationToken);
            }
            var occupied = await FindOccupiedTaskIdsAsync(connection, transaction, taskIds, cancellationToken);
            if (occupied.Count > 0) throw new InvalidOperationException($"packing tasks already belong to an active order: {string.Join(',', occupied.Order())}");
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
                await InsertTaskAsync(connection, transaction, order.id, CreateTask(snapshot, null, now), cancellationToken);
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
        IReadOnlyDictionary<long,int>? mappings, DateTime now)
    {
        var task = new DispatchPackingTaskEntity
        {
            task_no=snapshot.TaskNo,source_task_id=snapshot.SourceTaskId,source_task_no=snapshot.TaskNo,
            source_cartons_json=snapshot.CartonsJson,status=DispatchOrderStatus.PendingPick,expected_box_count=snapshot.Boxes.Count,
            source_version=snapshot.SourceVersion,stable_box_identity_verified=snapshot.Boxes.Count>0&&snapshot.Boxes.All(x=>!string.IsNullOrWhiteSpace(x.SourceBoxIdentity)),
            box_identity_validation_error=snapshot.Boxes.Count==0?"来源尚未提供物理箱，进入称重前必须同步并验证稳定箱ID":"",
            create_time=now,last_update_time=now,items=snapshot.Items.Select(x=>CreateItem(x,snapshot.SourceVersion,mappings,now)).ToList()
        };
        task.SetActiveState(true); return task;
    }

    private static DispatchPackingTaskItemEntity CreateItem(PackingTaskSourceItem item,string version,
        IReadOnlyDictionary<long,int>? mappings,DateTime now) => new()
    {
        source_item_id=item.SourceItemId,source_commodity_id=item.CommodityId,wms_sku_id=mappings==null?null:MappedSkuId(item,mappings),
        commodity_sku=item.CommoditySku,commodity_name=item.CommodityName,fn_sku=item.FnSku,msku=item.Msku,
        required_qty=item.Quantity,source_quantity_shipped=item.Quantity,source_version=version,source_snapshot=item.SourceSnapshot,
        is_active=true,create_time=now,last_update_time=now
    };

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
