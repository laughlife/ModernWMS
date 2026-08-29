using System.Data;
using Dapper;
using ModernWMS.Core.DBContext.Entities;
using ModernWMS.Core.JWT;
using ModernWMS.WMS.Entities.Models;
using ModernWMS.WMS.Entities.ViewModels.DispatchWorkflow;
using ModernWMS.WMS.IServices.StockAllocation;
using MySqlConnector;

namespace ModernWMS.WMS.Services.DispatchWorkflow;

/// <summary>
/// 表示 DispatchWorkflowService 类型。
/// </summary>
public partial class DispatchWorkflowService
{
    /// <summary>
    /// 执行 ConfirmOutboundAsync 操作。
    /// </summary>
    public Task<OutboundCommandResult> ConfirmOutboundAsync(int orderId, OutboundCommandRequest request,
        CurrentUser currentUser, CancellationToken cancellationToken = default) =>
        ExecuteOutboundMutationAsync(orderId, request, currentUser, DispatchWorkflowOperation.ConfirmOutbound,
            DispatchOrderStatus.PendingOutbound, DispatchOrderStatus.Outbound, true, cancellationToken);

    /// <summary>
    /// 执行 CancelOutboundAsync 操作。
    /// </summary>
    public Task<OutboundCommandResult> CancelOutboundAsync(int orderId, OutboundCommandRequest request,
        CurrentUser currentUser, CancellationToken cancellationToken = default) =>
        ExecuteOutboundMutationAsync(orderId, request, currentUser, DispatchWorkflowOperation.CancelOutbound,
            DispatchOrderStatus.Outbound, DispatchOrderStatus.PendingOutbound, false, cancellationToken);

    /// <summary>
    /// 执行 SignAsync 操作。
    /// </summary>
    public async Task<SignDispatchOrderResult> SignAsync(int orderId, SignDispatchOrderRequest request,
        CurrentUser currentUser, CancellationToken cancellationToken = default)
    {
        ValidateSignRequest(orderId, request);
        var requestId = request.request_id.Trim();
        var transactionCompleted = false;
        DispatchOrderEntity order;
        await using (var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken))
        await using (var transaction = await connection.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken))
        {
            try
            {
                order = await LoadOrderForUpdateAsync(connection, transaction, orderId, cancellationToken);
                await _warehouseAccessService.EnsureAllowedAsync(order.warehouse_id, currentUser);
                if (order.status != DispatchOrderStatus.Outbound)
                    throw DispatchWorkflowCommandException.StatusNotAllowedForSigning();
                if (order.signed_at == null && order.row_version != request.row_version)
                    throw DispatchWorkflowCommandException.ConcurrencyConflict();
                var guard = await EnsurePostPickSourceCurrentAsync(
                    connection, transaction, orderId, currentUser, cancellationToken);
                if (guard.source_change_pending)
                {
                    await transaction.CommitAsync(cancellationToken);
                    transactionCompleted = true;
                    throw DispatchWorkflowCommandException.SourceChangePending();
                }
                order = await LoadOrderForUpdateAsync(connection, transaction, orderId, cancellationToken);
                await _warehouseAccessService.EnsureAllowedAsync(order.warehouse_id, currentUser);
                if (order.status != DispatchOrderStatus.Outbound)
                    throw DispatchWorkflowCommandException.StatusNotAllowedForSigning();
                var previous = await FindOutboundOperationAsync(connection, transaction, orderId,
                    DispatchWorkflowOperation.Sign, requestId, cancellationToken);
                var shippedQuantity = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
                    "SELECT COALESCE(SUM(`actual_qty`),0) FROM `wms_dispatchlist` WHERE `dispatch_order_id`=@orderId;",
                    new { orderId }, transaction, cancellationToken: cancellationToken));
                if (shippedQuantity <= 0 || request.damaged_qty < 0 || request.damaged_qty > shippedQuantity)
                    throw new ArgumentException("damaged_qty must be between zero and the shipped quantity", nameof(request));

                var now = DateTime.Now;
                if (order.signed_at == null)
                {
                    order.signed_qty = shippedQuantity - request.damaged_qty;
                    order.damaged_qty = request.damaged_qty;
                    order.signed_by = currentUser.user_id;
                    order.signed_by_name = Truncate(currentUser.user_name, 128);
                    order.signed_at = now;
                    order.notification_status = DispatchSignNotificationStatus.None;
                    order.notification_attempt_count = 0;
                    order.notification_sent_at = null;
                    order.notification_last_error = string.Empty;
                    order.notification_updated_at = null;
                    order.last_update_time = now;
                    order.row_version++;
                    await connection.ExecuteAsync(new CommandDefinition("""
                        UPDATE `wms_dispatch_order` SET `signed_qty`=@signed_qty,`damaged_qty`=@damaged_qty,
                          `signed_by`=@signed_by,`signed_by_name`=@signed_by_name,`signed_at`=@signed_at,
                          `notification_status`=@notification_status,`notification_attempt_count`=0,
                          `notification_sent_at`=NULL,`notification_last_error`='',`notification_updated_at`=NULL,
                          `last_update_time`=@last_update_time,
                          `row_version`=@row_version
                        WHERE `id`=@id;
                        """, order, transaction, cancellationToken: cancellationToken));
                    await InsertOperationAsync(connection, transaction, order.id, DispatchWorkflowOperation.Sign,
                        requestId, order.status, order.row_version, currentUser, now, cancellationToken);
                }
                else
                {
                    if (order.damaged_qty != request.damaged_qty)
                        throw DispatchWorkflowCommandException.IdempotencyConflict();
                    if (previous == null && order.row_version != request.row_version)
                        throw DispatchWorkflowCommandException.ConcurrencyConflict();
                }
                await transaction.CommitAsync(cancellationToken);
                transactionCompleted = true;
                return ToSignResult(order, requestId);
            }
            catch
            {
                if (!transactionCompleted) await transaction.RollbackAsync(CancellationToken.None);
                throw;
            }
        }
    }

    private async Task<OutboundCommandResult> ExecuteOutboundMutationAsync(int orderId, OutboundCommandRequest request,
        CurrentUser currentUser, DispatchWorkflowOperation operation, DispatchOrderStatus requiredStatus,
        DispatchOrderStatus resultStatus, bool deduct, CancellationToken cancellationToken)
    {
        ValidateOutboundRequest(orderId, request);
        var requestId = request.request_id.Trim();
        await using (var idempotencyConnection = await _connectionFactory.OpenConnectionAsync(cancellationToken))
        {
            var completed = await FindOutboundOperationAsync(idempotencyConnection, null, orderId,
                operation, requestId, cancellationToken);
            if (completed != null) return OutboundResultFromLedger(completed, requestId);
        }
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var transactionCompleted = false;
        try
        {
            var previous = await FindOutboundOperationAsync(connection, transaction, orderId, operation, requestId, cancellationToken);
            if (previous != null)
            {
                await transaction.CommitAsync(cancellationToken);
                transactionCompleted = true;
                return OutboundResultFromLedger(previous, requestId);
            }
            var lockedOrder = await LoadOrderForUpdateAsync(connection, transaction, orderId, cancellationToken);
            await _warehouseAccessService.EnsureAllowedAsync(lockedOrder.warehouse_id, currentUser);
            if (lockedOrder.status != requiredStatus)
                throw DispatchWorkflowCommandException.StatusNotAllowedForOutbound(deduct);
            if (lockedOrder.row_version != request.row_version)
                throw DispatchWorkflowCommandException.ConcurrencyConflict();
            if (!deduct && lockedOrder.signed_at != null)
                throw DispatchWorkflowCommandException.SignedOrderCannotBeCancelled();
            if (deduct)
            {
                var guard = await EnsurePostPickSourceCurrentAsync(
                    connection, transaction, orderId, currentUser, cancellationToken);
                if (guard.source_change_pending)
                {
                    await transaction.CommitAsync(cancellationToken);
                    transactionCompleted = true;
                    throw DispatchWorkflowCommandException.SourceChangePending();
                }
            }
            var order = await LoadOutboundOrderAsync(connection, transaction, orderId, cancellationToken);
            if (deduct) EnsureFullyMeasured(order);

            var details = (await connection.QueryAsync<DispatchlistEntity>(new CommandDefinition("""
                SELECT * FROM `wms_dispatchlist` WHERE `dispatch_order_id`=@orderId ORDER BY `id` FOR UPDATE;
                """, new { orderId }, transaction, cancellationToken: cancellationToken))).AsList();
            if (details.Count == 0) throw DispatchWorkflowCommandException.StockConflict("dispatch order has no stock detail");
            if (deduct) EnsureCarrierConfigured(details);
            var detailIds = details.Select(x => x.id).ToArray();
            var allocations = (await connection.QueryAsync<DispatchpicklistEntity>(new CommandDefinition("""
                SELECT * FROM `wms_dispatchpicklist` WHERE `dispatchlist_id` IN @detailIds ORDER BY `erp_stock_id`,`id` FOR UPDATE;
                """, new { detailIds }, transaction, cancellationToken: cancellationToken))).AsList();
            await ValidateDetailAllocationsAsync(connection, transaction, order, details, allocations, deduct, cancellationToken);
            ValidateAllocationState(allocations, deduct);
            var now = DateTime.Now;
            if (deduct)
                await ApplyErpStockOutboundAsync(connection, transaction, order, allocations,
                    currentUser, requestId, cancellationToken);
            else
                await RestoreErpStockReservationAsync(connection,transaction,order,allocations,
                    currentUser,requestId,cancellationToken);

            await connection.ExecuteAsync(new CommandDefinition("""
                UPDATE `wms_dispatchlist` SET `dispatch_status`=@detailStatus,`lock_qty`=IF(@deduct,0,`picked_qty`),
                  `actual_qty`=IF(@deduct,`picked_qty`,0),`intrasit_qty`=IF(@deduct,`picked_qty`,0),`last_update_time`=@now
                WHERE `dispatch_order_id`=@orderId;
                UPDATE `wms_dispatch_packing_task` SET `status`=@resultStatus,`last_update_time`=@now,
                  `row_version`=`row_version`+1 WHERE `dispatch_order_id`=@orderId AND `is_active`=1;
                """, new { detailStatus = deduct ? (byte)6 : (byte)5, deduct, now, orderId, resultStatus },
                transaction, cancellationToken: cancellationToken));
            var orderUpdated = await connection.ExecuteAsync(new CommandDefinition("""
                UPDATE `wms_dispatch_order` SET `status`=@resultStatus,`last_update_time`=@now,`row_version`=`row_version`+1
                WHERE `id`=@orderId AND `row_version`=@expectedVersion;
                """, new { resultStatus, now, orderId, expectedVersion = order.row_version }, transaction,
                cancellationToken: cancellationToken));
            if (orderUpdated != 1) throw DispatchWorkflowCommandException.ConcurrencyConflict();
            order.status = resultStatus;
            order.last_update_time = now;
            order.row_version++;
            await InsertOperationAsync(connection, transaction, order.id, operation, requestId, order.status,
                order.row_version, currentUser, now, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            transactionCompleted = true;
            return ToOutboundResult(order, requestId);
        }
        catch (Exception exception) when (IsDatabaseConcurrency(exception))
        {
            if (!transactionCompleted) await transaction.RollbackAsync(CancellationToken.None);
            var winner = await FindOutboundOperationAsync(connection, null, orderId, operation, requestId, CancellationToken.None);
            if (winner != null) return OutboundResultFromLedger(winner, requestId);
            throw DispatchWorkflowCommandException.ConcurrencyConflict();
        }
        catch
        {
            if (!transactionCompleted) await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private static async Task<DispatchOrderEntity> LoadOrderForUpdateAsync(IDbConnection connection, IDbTransaction transaction,
        int orderId, CancellationToken cancellationToken) =>
        await connection.QuerySingleOrDefaultAsync<DispatchOrderEntity>(new CommandDefinition(
            "SELECT * FROM `wms_dispatch_order` WHERE `id`=@orderId FOR UPDATE;", new { orderId }, transaction,
            cancellationToken: cancellationToken)) ?? throw new KeyNotFoundException($"dispatch order not found: {orderId}");

    private static async Task<DispatchOrderEntity> LoadOutboundOrderAsync(IDbConnection connection, IDbTransaction transaction,
        int orderId, CancellationToken cancellationToken)
    {
        using var grid = await connection.QueryMultipleAsync(new CommandDefinition("""
            SELECT * FROM `wms_dispatch_order` WHERE `id`=@orderId FOR UPDATE;
            SELECT * FROM `wms_dispatch_packing_task` WHERE `dispatch_order_id`=@orderId AND `is_active`=1 ORDER BY `id` FOR UPDATE;
            SELECT b.* FROM `wms_weighing_box` b JOIN `wms_dispatch_packing_task` t ON t.`id`=b.`packing_task_id`
              WHERE t.`dispatch_order_id`=@orderId AND t.`is_active`=1 AND b.`is_invalidated`=0 ORDER BY b.`id` FOR UPDATE;
            """, new { orderId }, transaction, cancellationToken: cancellationToken));
        var order = await grid.ReadSingleOrDefaultAsync<DispatchOrderEntity>()
            ?? throw new KeyNotFoundException($"dispatch order not found: {orderId}");
        order.packing_tasks = (await grid.ReadAsync<DispatchPackingTaskEntity>()).AsList();
        var boxes = (await grid.ReadAsync<WeighingBoxEntity>()).AsList();
        foreach (var task in order.packing_tasks) task.boxes = boxes.Where(x => x.packing_task_id == task.id).ToList();
        return order;
    }

    private static void EnsureFullyMeasured(DispatchOrderEntity order)
    {
        var tasks = order.packing_tasks.Where(x => x.is_active).ToList();
        if (tasks.Count == 0 || tasks.Any(x => x.status != DispatchOrderStatus.PendingOutbound
            || x.expected_box_count <= 0 || x.measured_box_count != x.expected_box_count
            || x.boxes.Count != x.expected_box_count || x.boxes.Any(box => !HasCompleteOutboundMeasurement(box))))
            throw DispatchWorkflowCommandException.WeighingIncomplete(
                "every active packing task must have every physical box fully measured");
    }

    private static bool HasCompleteOutboundMeasurement(WeighingBoxEntity box) =>
        box.measurement_status == "MEASURED" && box.weight > 0 && box.length > 0 && box.width > 0 && box.height > 0;

    private static void EnsureCarrierConfigured(IReadOnlyCollection<DispatchlistEntity> details)
    {
        var carrierIds = details.Where(x => x.carrier_warehouse_id is > 0)
            .Select(x => x.carrier_warehouse_id!.Value).Distinct().ToList();
        var carrierNames = details.Where(x => !string.IsNullOrWhiteSpace(x.carrier_unit))
            .Select(x => x.carrier_unit.Trim()).Distinct(StringComparer.Ordinal).ToList();
        if (carrierIds.Count != 1 || carrierNames.Count != 1
            || details.Any(x => x.carrier_warehouse_id != carrierIds[0]
                || !string.Equals(x.carrier_unit?.Trim(), carrierNames[0], StringComparison.Ordinal)))
            throw DispatchWorkflowCommandException.CarrierRequired();
    }

    private static void ValidateAllocationState(IReadOnlyCollection<DispatchpicklistEntity> allocations, bool deduct)
    {
        if (allocations.Count == 0 || allocations.Any(x => x.picked_qty <= 0
            || x.erp_stock_id is null or <= 0
            || x.reservation_id is null or <= 0||x.reservation_item_id is null or <= 0
            || (deduct ? x.is_update_stock : !x.is_update_stock)))
            throw DispatchWorkflowCommandException.StockConflict("stock allocation state changed");
    }

    private async Task ApplyErpStockOutboundAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        DispatchOrderEntity order,
        IReadOnlyCollection<DispatchpicklistEntity> allocations,
        CurrentUser user,
        string requestId,
        CancellationToken cancellationToken)
    {
        var mutation = RequirePackingStockMutationService();
        var prelocks=allocations.Select(allocation=>new PackingStockPrelockRequest(
            DispatchStockMutationContext(user,order.warehouse_id,"DISPATCH_SHIP_OUT",order.id,allocation.id,
                allocation.erp_stock_id!.Value,allocation.picked_qty,requestId,
                allocation.reservation_id,allocation.reservation_item_id),
            allocation.erp_stock_id.Value,"SHIP_OUT")).ToArray();
        await mutation.PrelockAsync(connection,transaction,[order.warehouse_id],prelocks,cancellationToken);
        foreach (var allocation in allocations.OrderBy(x=>x.erp_stock_id).ThenBy(x=>x.id))
        {
            await mutation.ShipLockedAsync(connection,transaction,
                DispatchStockMutationContext(user,order.warehouse_id,"DISPATCH_SHIP_OUT",order.id,allocation.id,
                    allocation.erp_stock_id!.Value,allocation.picked_qty,requestId,
                    allocation.reservation_id,allocation.reservation_item_id),
                allocation.erp_stock_id.Value,allocation.picked_qty,cancellationToken);
            if(allocation.stock_allocation_id is >0)
                await RequireLegacyPackingReleaseAdapter().SettleConsumeAsync(
                    connection,transaction,allocation.erp_stock_id.Value,allocation.stock_allocation_id.Value,
                    allocation.reservation_item_id!.Value,allocation.picked_qty,user.user_name??string.Empty,
                    cancellationToken);
            await connection.ExecuteAsync(new CommandDefinition("""
                UPDATE `wms_dispatchpicklist` SET `is_update_stock`=1,`last_update_time`=@now
                 WHERE `id`=@id AND `is_update_stock`=0;
                """,new{now=DateTime.Now,allocation.id},transaction,cancellationToken:cancellationToken));
        }
    }

    private async Task RestoreErpStockReservationAsync(
        IDbConnection connection,IDbTransaction transaction,DispatchOrderEntity order,
        IReadOnlyCollection<DispatchpicklistEntity> allocations,CurrentUser user,string requestId,
        CancellationToken cancellationToken)
    {
        var mutation=RequirePackingStockMutationService();
        foreach(var allocation in allocations.OrderBy(x=>x.erp_stock_id).ThenBy(x=>x.id))
        {
            var stockId=allocation.erp_stock_id!.Value;
            await mutation.AdjustAvailableAsync(connection,transaction,
                DispatchStockMutationContext(user,order.warehouse_id,"DISPATCH_SHIP_RESTORE",order.id,
                    allocation.id,stockId,allocation.picked_qty,requestId,null,null),
                stockId,allocation.picked_qty,cancellationToken);
            var reserve=await mutation.ReserveAsync(connection,transaction,
                DispatchStockMutationContext(user,order.warehouse_id,"DISPATCH_RESERVE",order.id,
                    allocation.id,stockId,allocation.picked_qty,$"OUTBOUND_CANCEL:{requestId}",null,null),
                stockId,allocation.picked_qty,cancellationToken);
            await connection.ExecuteAsync(new CommandDefinition("""
                UPDATE `wms_dispatchpicklist`
                   SET `is_update_stock`=0,`stock_allocation_id`=NULL,
                       `reservation_id`=@reservationId,`reservation_item_id`=@reservationItemId,
                       `last_update_time`=@now
                 WHERE `id`=@id AND `is_update_stock`=1;
                """,new{reservationId=reserve.ReservationId,reservationItemId=reserve.ReservationItemId,
                    now=DateTime.Now,allocation.id},transaction,cancellationToken:cancellationToken));
        }
    }

    private static async Task ValidateDetailAllocationsAsync(IDbConnection connection, IDbTransaction transaction,
        DispatchOrderEntity order, IReadOnlyCollection<DispatchlistEntity> details,
        IReadOnlyCollection<DispatchpicklistEntity> allocations, bool deduct, CancellationToken cancellationToken)
    {
        if (details.Any(x => x.dispatch_order_id != order.id || x.dispatch_status != (deduct ? (byte)5 : (byte)6)
            || x.packing_task_id is null or <= 0
            || x.picked_qty <= 0 || x.lock_qty != (deduct ? x.picked_qty : 0)))
            throw DispatchWorkflowCommandException.StockConflict("dispatch detail ownership or picked quantity changed");
        var taskIds = order.packing_tasks.Where(x => x.is_active).Select(x => x.id).ToHashSet();
        if (taskIds.Count == 0 || details.Any(x => !taskIds.Contains(x.packing_task_id!.Value)))
            throw DispatchWorkflowCommandException.StockConflict(
                "dispatch detail is attached to another order or an inactive packing task");
        var itemIds = details.Where(x=>x.packing_task_item_id is >0)
            .Select(x => x.packing_task_item_id!.Value).Distinct().ToArray();
        var items = itemIds.Length==0?[]:(await connection.QueryAsync<DispatchPackingTaskItemEntity>(new CommandDefinition("""
            SELECT * FROM `wms_dispatch_packing_task_item` WHERE `id` IN @itemIds FOR UPDATE;
            """, new { itemIds }, transaction, cancellationToken: cancellationToken))).AsList();
        foreach (var detail in details)
        {
            var item = items.SingleOrDefault(x => x.id == detail.packing_task_item_id);
            var rows = allocations.Where(x => x.dispatchlist_id == detail.id).ToList();
            var actualQuantity=rows.Count==0?0:await connection.ExecuteScalarAsync<int>(new CommandDefinition("""
                SELECT COALESCE(SUM(box_item.`actual_qty`),0)
                  FROM `wms_weighing_box_item` box_item
                  JOIN `wms_weighing_box` box ON box.`id`=box_item.`weighing_box_id`
                 WHERE box.`packing_task_id`=@taskId AND box.`is_invalidated`=0
                   AND box_item.`packing_task_item_id` <=> @taskItemId
                   AND box_item.`dispatchpicklist_id` IN @pickIds;
                """,new{taskId=detail.packing_task_id,taskItemId=detail.packing_task_item_id,
                    pickIds=rows.Select(x=>x.id).ToArray()},transaction,cancellationToken:cancellationToken));
            if ((detail.packing_task_item_id is >0
                    &&(item == null || !item.is_active || item.packing_task_id != detail.packing_task_id))
                || rows.Count == 0
                || rows.Any(x => x.packing_task_item_id != detail.packing_task_item_id
                    ||x.erp_stock_id is null or <=0
                    || x.picked_qty <= 0 || x.pick_qty != x.picked_qty)
                || rows.Sum(x => x.picked_qty) != detail.picked_qty
                || actualQuantity!=detail.picked_qty)
                throw DispatchWorkflowCommandException.StockConflict(
                    "dispatch detail allocations do not match actual box contents");
        }
    }

    private static void ValidateStocks(IReadOnlyCollection<DispatchpicklistEntity> allocations,
        IReadOnlyCollection<StockEntity> stocks, bool deduct)
    {
        foreach (var group in allocations.GroupBy(x => x.stock_id))
        {
            var stock = stocks.SingleOrDefault(x => x.id == group.Key);
            if (stock == null || group.Any(x => !SameAllocatedIdentity(stock, x)))
                throw DispatchWorkflowCommandException.StockConflict("the exact allocated stock row no longer exists");
            if (deduct && (stock.is_freeze || stock.qty < group.Sum(x => x.picked_qty)))
                throw DispatchWorkflowCommandException.StockConflict("allocated stock has insufficient available quantity");
        }
    }

    private static bool SameAllocatedIdentity(StockEntity stock, DispatchpicklistEntity allocation) =>
        stock.sku_id == allocation.sku_id && stock.goods_location_id == allocation.goods_location_id
        && stock.goods_owner_id == allocation.goods_owner_id && stock.series_number == allocation.series_number
        && stock.expiry_date == allocation.expiry_date && stock.price == allocation.price
        && stock.putaway_date == allocation.putaway_date;

    private static async Task EnsureStocksBelongToOrderWarehouseAsync(IDbConnection connection, IDbTransaction transaction,
        long erpWarehouseId, IReadOnlyCollection<StockEntity> stocks, CancellationToken cancellationToken)
    {
        var warehouseIds = (await connection.QueryAsync<int>(new CommandDefinition("""
            SELECT `id` FROM `wms_warehouse` WHERE `erp_warehouse_id`=@erpWarehouseId AND `is_valid`=1;
            """, new { erpWarehouseId }, transaction, cancellationToken: cancellationToken))).AsList();
        if (warehouseIds.Count != 1)
            throw DispatchWorkflowCommandException.StockConflict("ERP warehouse has no unique WMS warehouse mapping");
        var locationIds = (await connection.QueryAsync<int>(new CommandDefinition("""
            SELECT `id` FROM `wms_goodslocation` WHERE `warehouse_id`=@warehouseId AND `is_valid`=1;
            """, new { warehouseId = warehouseIds[0] }, transaction, cancellationToken: cancellationToken))).AsList();
        if (stocks.Any(x => !locationIds.Contains(x.goods_location_id)))
            throw DispatchWorkflowCommandException.StockConflict("allocated stock does not belong to the order warehouse");
    }

    private static async Task<DispatchWorkflowOperationEntity?> FindOutboundOperationAsync(IDbConnection connection,
        IDbTransaction? transaction, int orderId, DispatchWorkflowOperation operation, string requestId,
        CancellationToken cancellationToken)
    {
        var value = await FindOperationAsync(connection, transaction, orderId, operation, requestId, cancellationToken);
        return value?.result_status == DispatchWorkflowOperationResultStatus.Succeeded ? value : null;
    }

    private static Task InsertStockRecordAsync(IDbConnection connection, IDbTransaction transaction,
        DispatchOrderEntity order, DispatchpicklistEntity allocation, CurrentUser user, DateTime now,
        int delta, int before, int after, string prefix, int cycle, bool deduct, CancellationToken cancellationToken) =>
        connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO `wms_stock_record` (`record_no`,`biz_type`,`biz_id`,`biz_item_id`,`stock_id`,`sku_id`,
              `goods_location_id`,`goods_owner_id`,`change_qty`,`before_qty`,`after_qty`,`direction`,`operator_id`,
              `operator_name`,`remark`,`operate_time`)
            VALUES (@recordNo,@bizType,@orderId,@allocationId,@stockId,@skuId,@locationId,@ownerId,@delta,@before,@after,
              @direction,@operatorId,@operatorName,@remark,@now);
            """, new
            {
                recordNo = $"MWMS-{(deduct ? "DO" : "DI")}-{order.id}-{allocation.id}-{cycle}",
                bizType = cycle == 1 ? prefix : $"{prefix}_{cycle}", orderId = order.id, allocationId = allocation.id,
                stockId = allocation.stock_id, skuId = allocation.sku_id, locationId = allocation.goods_location_id,
                ownerId = allocation.goods_owner_id, delta, before, after, direction = deduct ? "OUT" : "IN",
                operatorId = user.user_id, operatorName = Truncate(user.user_name, 128),
                remark = deduct ? "装箱任务拣货单确认出库" : "装箱任务拣货单撤回出库", now
            }, transaction, cancellationToken: cancellationToken));

    private static OutboundCommandResult ToOutboundResult(DispatchOrderEntity order, string requestId) => new()
        { order_id = order.id, request_id = requestId, status = ToApiStatus(order.status), row_version = order.row_version };

    private static OutboundCommandResult OutboundResultFromLedger(DispatchWorkflowOperationEntity operation, string requestId)
    {
        if (operation.result_order_status == null || operation.result_row_version == null)
            throw DispatchWorkflowCommandException.ConcurrencyConflict();
        return new OutboundCommandResult { order_id = operation.dispatch_order_id, request_id = requestId,
            status = ToApiStatus(operation.result_order_status.Value), row_version = operation.result_row_version.Value };
    }

    private static void ValidateOutboundRequest(int orderId, OutboundCommandRequest request)
    {
        if (orderId <= 0 || string.IsNullOrWhiteSpace(request.request_id)
            || request.request_id.Trim().Length > 64 || request.row_version < 0)
            throw new ArgumentException("order id, request_id and row_version are required", nameof(request));
    }

    private static void ValidateSignRequest(int orderId, SignDispatchOrderRequest request)
    {
        if (orderId <= 0 || string.IsNullOrWhiteSpace(request.request_id)
            || request.request_id.Trim().Length > 64 || request.row_version < 0)
            throw new ArgumentException("order id, request_id and row_version are required", nameof(request));
    }

    private static SignDispatchOrderResult ToSignResult(DispatchOrderEntity order, string requestId) => new()
    {
        order_id = order.id, request_id = requestId.Trim(), status = ToApiStatus(order.status), row_version = order.row_version,
        signed_qty = order.signed_qty ?? 0, damaged_qty = order.damaged_qty ?? 0,
        notification_status = order.notification_status.ToString().ToUpperInvariant()
    };

    private static string Truncate(string? value, int length)
    {
        var normalized = value?.Trim() ?? string.Empty;
        return normalized.Length <= length ? normalized : normalized[..length];
    }

    private sealed class StockRecordKey
    {
        public string biz_type { get; set; } = string.Empty;
        public long biz_item_id { get; set; }
        public int stock_id { get; set; }
    }
}

/// <summary>
/// 表示 DispatchWorkflowCommandException 类型。
/// </summary>
public sealed partial class DispatchWorkflowCommandException
{
    /// <summary>
    /// 执行 StockConflict 操作。
    /// </summary>
    public static DispatchWorkflowCommandException StockConflict(string detail) => new("STOCK_CONFLICT", detail);
    /// <summary>
    /// 执行 StatusNotAllowedForOutbound 操作。
    /// </summary>
    public static DispatchWorkflowCommandException StatusNotAllowedForOutbound(bool confirming) =>
        new("STATUS_NOT_ALLOWED", confirming ? "only a pending-outbound order can be confirmed" : "only an outbound order can be cancelled");
    /// <summary>
    /// 执行 SignedOrderCannotBeCancelled 操作。
    /// </summary>
    public static DispatchWorkflowCommandException SignedOrderCannotBeCancelled() =>
        new("ORDER_ALREADY_SIGNED", "a signed outbound order cannot be cancelled");
    /// <summary>
    /// 执行 CanonicalOutboundCannotBeCancelled 操作。
    /// </summary>
    public static DispatchWorkflowCommandException CanonicalOutboundCannotBeCancelled() =>
        new("OUTBOUND_REVERSAL_NOT_SUPPORTED",
            "统一ERP库存模式不支持已出库库存的无损撤销，请停止操作并按库存流水执行人工向前修复");
    /// <summary>
    /// 执行 StatusNotAllowedForSigning 操作。
    /// </summary>
    public static DispatchWorkflowCommandException StatusNotAllowedForSigning() =>
        new("STATUS_NOT_ALLOWED", "only an outbound order can be signed");
}
