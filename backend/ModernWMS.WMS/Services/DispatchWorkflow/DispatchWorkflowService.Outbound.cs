using System.Data;
using Microsoft.EntityFrameworkCore;
using ModernWMS.Core.DBContext.Entities;
using ModernWMS.Core.JWT;
using ModernWMS.WMS.Entities.Models;
using ModernWMS.WMS.Entities.ViewModels.DispatchWorkflow;

namespace ModernWMS.WMS.Services.DispatchWorkflow;

public partial class DispatchWorkflowService
{
    public Task<OutboundCommandResult> ConfirmOutboundAsync(
        int orderId,
        OutboundCommandRequest request,
        CurrentUser currentUser,
        CancellationToken cancellationToken = default) =>
        ExecuteOutboundMutationAsync(orderId, request, currentUser,
            DispatchWorkflowOperation.ConfirmOutbound, DispatchOrderStatus.PendingOutbound,
            DispatchOrderStatus.Outbound, deduct: true, cancellationToken);

    public Task<OutboundCommandResult> CancelOutboundAsync(
        int orderId,
        OutboundCommandRequest request,
        CurrentUser currentUser,
        CancellationToken cancellationToken = default) =>
        ExecuteOutboundMutationAsync(orderId, request, currentUser,
            DispatchWorkflowOperation.CancelOutbound, DispatchOrderStatus.Outbound,
            DispatchOrderStatus.PendingOutbound, deduct: false, cancellationToken);

    public async Task<SignDispatchOrderResult> SignAsync(
        int orderId,
        SignDispatchOrderRequest request,
        CurrentUser currentUser,
        CancellationToken cancellationToken = default)
    {
        ValidateSignRequest(orderId, request);
        var shouldNotify = false;
        int? claimedNotificationAttempt = null;
        await using (var transaction = _dbContext.Database.IsRelational()
            ? await _dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
            : null)
        {
            try
            {
                var order = await _dbContext.GetDbSet<DispatchOrderEntity>()
                    .SingleOrDefaultAsync(t => t.id == orderId, cancellationToken)
                    ?? throw new KeyNotFoundException($"dispatch order not found: {orderId}");
                await _warehouseAccessService.EnsureAllowedAsync(order.warehouse_id, currentUser);
                if (order.status != DispatchOrderStatus.Outbound)
                {
                    throw DispatchWorkflowCommandException.StatusNotAllowedForSigning();
                }

                await EnsurePostPickSourceCurrentAsync(orderId, currentUser, cancellationToken);
                var details = await _dbContext.GetDbSet<DispatchlistEntity>()
                    .Where(t => t.dispatch_order_id == order.id).ToListAsync(cancellationToken);
                var sameSignRequest = await FindOutboundOperationAsync(
                    order.id, DispatchWorkflowOperation.Sign, request.request_id.Trim(), cancellationToken);
                var shippedQuantity = details.Sum(t => t.actual_qty);
                if (shippedQuantity <= 0 || request.damaged_qty < 0 || request.damaged_qty > shippedQuantity)
                {
                    throw new ArgumentException("damaged_qty must be between zero and the shipped quantity", nameof(request));
                }

                var now = DateTime.Now;
                if (order.signed_at == null)
                {
                    if (order.row_version != request.row_version)
                    {
                        throw DispatchWorkflowCommandException.ConcurrencyConflict();
                    }
                    order.signed_qty = shippedQuantity - request.damaged_qty;
                    order.damaged_qty = request.damaged_qty;
                    order.signed_by = currentUser.user_id;
                    order.signed_by_name = Truncate(currentUser.user_name, 128);
                    order.signed_at = now;
                    order.notification_status = DispatchSignNotificationStatus.Pending;
                    order.notification_last_error = string.Empty;
                    order.notification_updated_at = now;
                    order.last_update_time = now;
                    order.row_version++;
                    AddSucceededOutboundOperation(order, DispatchWorkflowOperation.Sign,
                        request.request_id.Trim(), currentUser, now);
                }
                else
                {
                    if (order.damaged_qty != request.damaged_qty)
                    {
                        throw DispatchWorkflowCommandException.IdempotencyConflict();
                    }
                    if (sameSignRequest == null && order.row_version != request.row_version)
                    {
                        throw DispatchWorkflowCommandException.ConcurrencyConflict();
                    }
                }

                var staleSending = order.notification_status == DispatchSignNotificationStatus.Sending
                    && (order.notification_updated_at == null
                        || order.notification_updated_at <= now.AddMinutes(-10));
                if (order.notification_status is DispatchSignNotificationStatus.Pending
                    or DispatchSignNotificationStatus.Failed || staleSending)
                {
                    order.notification_status = DispatchSignNotificationStatus.Sending;
                    order.notification_attempt_count++;
                    claimedNotificationAttempt = order.notification_attempt_count;
                    order.notification_updated_at = now;
                    order.notification_last_error = string.Empty;
                    order.last_update_time = now;
                    order.row_version++;
                    shouldNotify = true;
                }
                await _dbContext.SaveChangesAsync(cancellationToken);
                if (transaction != null)
                {
                    await transaction.CommitAsync(cancellationToken);
                }
                if (!shouldNotify)
                {
                    return ToSignResult(order, request.request_id);
                }
            }
            catch
            {
                if (transaction != null)
                {
                    await transaction.RollbackAsync(CancellationToken.None);
                }
                _dbContext.ChangeTracker.Clear();
                throw;
            }
        }

        var notificationSucceeded = false;
        if (_dispatchSignNotificationClient != null)
        {
            try
            {
                notificationSucceeded = await _dispatchSignNotificationClient.TryNotifySignedAsync(
                    (await FindOrderAsync(orderId, cancellationToken)).dispatch_no, cancellationToken);
            }
            catch
            {
                notificationSucceeded = false;
            }
        }

        _dbContext.ChangeTracker.Clear();
        await using var completionTransaction = _dbContext.Database.IsRelational()
            ? await _dbContext.Database.BeginTransactionAsync(
                IsolationLevel.Serializable, CancellationToken.None)
            : null;
        try
        {
            var completed = await _dbContext.GetDbSet<DispatchOrderEntity>()
                .SingleAsync(t => t.id == orderId, CancellationToken.None);
            await _warehouseAccessService.EnsureAllowedAsync(completed.warehouse_id, currentUser);
            if (completed.notification_status == DispatchSignNotificationStatus.Sending
                && claimedNotificationAttempt != null
                && completed.notification_attempt_count == claimedNotificationAttempt.Value)
            {
                var now = DateTime.Now;
                completed.notification_status = notificationSucceeded
                    ? DispatchSignNotificationStatus.Sent
                    : DispatchSignNotificationStatus.Failed;
                completed.notification_sent_at = notificationSucceeded ? now : null;
                completed.notification_last_error = notificationSucceeded
                    ? string.Empty
                    : "downstream signing notification was not accepted";
                completed.notification_updated_at = now;
                completed.last_update_time = now;
                completed.row_version++;
                await _dbContext.SaveChangesAsync(CancellationToken.None);
            }
            if (completionTransaction != null)
            {
                await completionTransaction.CommitAsync(CancellationToken.None);
            }
            return ToSignResult(completed, request.request_id);
        }
        catch
        {
            if (completionTransaction != null)
            {
                await completionTransaction.RollbackAsync(CancellationToken.None);
            }
            _dbContext.ChangeTracker.Clear();
            throw;
        }
    }

    private async Task<OutboundCommandResult> ExecuteOutboundMutationAsync(
        int orderId,
        OutboundCommandRequest request,
        CurrentUser currentUser,
        DispatchWorkflowOperation operation,
        DispatchOrderStatus requiredStatus,
        DispatchOrderStatus resultStatus,
        bool deduct,
        CancellationToken cancellationToken)
    {
        ValidateOutboundRequest(orderId, request);
        var requestId = request.request_id.Trim();
        await using var transaction = _dbContext.Database.IsRelational()
            ? await _dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
            : null;
        var transactionCompleted = false;

        try
        {
            var order = await _dbContext.GetDbSet<DispatchOrderEntity>()
                .Include(t => t.packing_tasks.Where(task => task.is_active))
                    .ThenInclude(task => task.boxes.Where(box => !box.is_invalidated))
                .SingleOrDefaultAsync(t => t.id == orderId, cancellationToken)
                ?? throw new KeyNotFoundException($"dispatch order not found: {orderId}");
            await _warehouseAccessService.EnsureAllowedAsync(order.warehouse_id, currentUser);

            var previous = await FindOutboundOperationAsync(orderId, operation, requestId, cancellationToken);
            if (previous != null)
            {
                if (transaction != null)
                {
                    await transaction.CommitAsync(cancellationToken);
                    transactionCompleted = true;
                }
                return OutboundResultFromLedger(previous, requestId);
            }

            if (order.status != requiredStatus)
            {
                throw DispatchWorkflowCommandException.StatusNotAllowedForOutbound(deduct);
            }
            if (order.row_version != request.row_version)
            {
                throw DispatchWorkflowCommandException.ConcurrencyConflict();
            }
            if (!deduct && order.signed_at != null)
            {
                throw DispatchWorkflowCommandException.SignedOrderCannotBeCancelled();
            }

            if (deduct)
            {
                var guard = await EnsurePostPickSourceCurrentAsync(orderId, currentUser, cancellationToken);
                if (guard.source_change_pending)
                {
                    if (transaction != null)
                    {
                        await transaction.CommitAsync(cancellationToken);
                        transactionCompleted = true;
                    }
                    throw DispatchWorkflowCommandException.SourceChangePending();
                }
                EnsureFullyMeasured(order);
            }

            var details = await _dbContext.GetDbSet<DispatchlistEntity>()
                .Where(t => t.dispatch_order_id == order.id)
                .OrderBy(t => t.id)
                .ToListAsync(cancellationToken);
            if (details.Count == 0)
            {
                throw DispatchWorkflowCommandException.StockConflict("dispatch order has no stock detail");
            }
            var detailIds = details.Select(t => t.id).ToList();
            var allocations = await _dbContext.GetDbSet<DispatchpicklistEntity>()
                .Where(t => detailIds.Contains(t.dispatchlist_id))
                .OrderBy(t => t.stock_id).ThenBy(t => t.id)
                .ToListAsync(cancellationToken);
            await ValidateDetailAllocationsAsync(order, details, allocations, deduct, cancellationToken);
            ValidateAllocationState(allocations, deduct);

            var stockIds = allocations.Select(t => t.stock_id).Distinct().ToList();
            var stocks = await _dbContext.GetDbSet<StockEntity>()
                .Where(t => stockIds.Contains(t.id))
                .OrderBy(t => t.id)
                .ToListAsync(cancellationToken);
            await EnsureStocksBelongToOrderWarehouseAsync(
                order.warehouse_id, stocks, cancellationToken);
            ValidateStocks(allocations, stocks, deduct);

            var now = DateTime.Now;
            var existingRecords = await _dbContext.GetDbSet<WmsStockRecordEntity>().AsNoTracking()
                .Where(t => t.biz_id == order.id
                    && (t.biz_type.StartsWith("DISPATCH_OUT") || t.biz_type.StartsWith("DISPATCH_IN")))
                .Select(t => new { t.biz_type, t.biz_item_id, t.stock_id })
                .ToListAsync(cancellationToken);
            foreach (var group in allocations.GroupBy(t => t.stock_id).OrderBy(t => t.Key))
            {
                var stock = stocks.Single(t => t.id == group.Key);
                var runningQuantity = stock.qty;
                foreach (var allocation in group.OrderBy(t => t.id))
                {
                    var delta = deduct ? -allocation.picked_qty : allocation.picked_qty;
                    var afterQuantity = checked(runningQuantity + delta);
                    var prefix = deduct ? "DISPATCH_OUT" : "DISPATCH_IN";
                    var cycle = existingRecords.Count(t => t.biz_item_id == allocation.id
                        && t.stock_id == stock.id && t.biz_type.StartsWith(prefix)) + 1;
                    var businessType = cycle == 1 ? prefix : $"{prefix}_{cycle}";
                    _dbContext.GetDbSet<WmsStockRecordEntity>().Add(new WmsStockRecordEntity
                    {
                        record_no = $"MWMS-{(deduct ? "DO" : "DI")}-{order.id}-{allocation.id}-{cycle}",
                        biz_type = businessType,
                        biz_id = order.id,
                        biz_item_id = allocation.id,
                        stock_id = stock.id,
                        sku_id = allocation.sku_id,
                        goods_location_id = allocation.goods_location_id,
                        goods_owner_id = allocation.goods_owner_id,
                        change_qty = delta,
                        before_qty = runningQuantity,
                        after_qty = afterQuantity,
                        direction = deduct ? "OUT" : "IN",
                        operator_id = currentUser.user_id,
                        operator_name = Truncate(currentUser.user_name, 128),
                        remark = deduct ? "装箱任务拣货单确认出库" : "装箱任务拣货单撤回出库",
                        operate_time = now,
                        tenant_id = order.tenant_id
                    });
                    runningQuantity = afterQuantity;
                    allocation.is_update_stock = deduct;
                    allocation.last_update_time = now;
                }
                stock.qty = runningQuantity;
                stock.last_update_time = now;
            }

            foreach (var detail in details)
            {
                detail.dispatch_status = deduct ? (byte)6 : (byte)5;
                detail.lock_qty = deduct ? 0 : detail.picked_qty;
                detail.actual_qty = deduct ? detail.picked_qty : 0;
                detail.intrasit_qty = deduct ? detail.picked_qty : 0;
                detail.last_update_time = now;
            }
            foreach (var task in order.packing_tasks)
            {
                task.status = resultStatus;
                task.last_update_time = now;
                task.row_version++;
            }
            order.status = resultStatus;
            order.last_update_time = now;
            order.row_version++;
            AddSucceededOutboundOperation(order, operation, requestId, currentUser, now);
            await _dbContext.SaveChangesAsync(cancellationToken);
            if (transaction != null)
            {
                await transaction.CommitAsync(cancellationToken);
                transactionCompleted = true;
            }
            return ToOutboundResult(order, requestId);
        }
        catch (Exception exception) when (IsDatabaseConcurrency(exception))
        {
            if (transaction != null && !transactionCompleted)
            {
                await transaction.RollbackAsync(CancellationToken.None);
            }
            _dbContext.ChangeTracker.Clear();
            var winner = await FindOutboundOperationAsync(
                orderId, operation, requestId, CancellationToken.None);
            if (winner != null)
            {
                return OutboundResultFromLedger(winner, requestId);
            }
            throw DispatchWorkflowCommandException.ConcurrencyConflict();
        }
        catch
        {
            if (transaction != null && !transactionCompleted)
            {
                await transaction.RollbackAsync(CancellationToken.None);
            }
            _dbContext.ChangeTracker.Clear();
            throw;
        }
    }

    private static void EnsureFullyMeasured(DispatchOrderEntity order)
    {
        var tasks = order.packing_tasks.Where(t => t.is_active).ToList();
        if (tasks.Count == 0 || tasks.Any(task =>
            task.status != DispatchOrderStatus.PendingOutbound
            || task.expected_box_count <= 0
            || task.measured_box_count != task.expected_box_count
            || task.boxes.Count != task.expected_box_count
            || task.boxes.Any(box => !HasCompleteMeasurement(box))))
        {
            throw DispatchWorkflowCommandException.WeighingIncomplete(
                "every active packing task must have every physical box fully measured");
        }
    }

    private static void ValidateAllocationState(
        IReadOnlyCollection<DispatchpicklistEntity> allocations, bool deduct)
    {
        if (allocations.Count == 0 || allocations.Any(t => t.stock_id <= 0 || t.picked_qty <= 0
            || (deduct ? t.is_update_stock : !t.is_update_stock)))
        {
            throw DispatchWorkflowCommandException.StockConflict("stock allocation state changed");
        }
    }

    private async Task ValidateDetailAllocationsAsync(
        DispatchOrderEntity order,
        IReadOnlyCollection<DispatchlistEntity> details,
        IReadOnlyCollection<DispatchpicklistEntity> allocations,
        bool deduct,
        CancellationToken cancellationToken)
    {
        if (details.Any(detail => detail.dispatch_order_id != order.id
            || detail.dispatch_status != (deduct ? (byte)5 : (byte)6)
            || detail.packing_task_id is null or <= 0
            || detail.packing_task_item_id is null or <= 0
            || detail.sku_id <= 0
            || detail.picked_qty <= 0
            || detail.lock_qty != (deduct ? detail.picked_qty : 0)))
        {
            throw DispatchWorkflowCommandException.StockConflict(
                "dispatch detail ownership or picked quantity changed");
        }

        var activeTaskIds = order.packing_tasks.Where(t => t.is_active).Select(t => t.id).ToHashSet();
        if (activeTaskIds.Count == 0 || details.Any(detail =>
            !activeTaskIds.Contains(detail.packing_task_id!.Value)))
        {
            throw DispatchWorkflowCommandException.StockConflict(
                "dispatch detail is attached to another order or an inactive packing task");
        }

        var itemIds = details.Select(t => t.packing_task_item_id!.Value).Distinct().ToList();
        var items = await _dbContext.GetDbSet<DispatchPackingTaskItemEntity>().AsNoTracking()
            .Where(t => itemIds.Contains(t.id))
            .ToListAsync(cancellationToken);
        foreach (var detail in details)
        {
            var item = items.SingleOrDefault(t => t.id == detail.packing_task_item_id);
            var rows = allocations.Where(t => t.dispatchlist_id == detail.id).ToList();
            if (item == null || !item.is_active
                || item.packing_task_id != detail.packing_task_id
                || item.wms_sku_id != detail.sku_id
                || rows.Count == 0
                || rows.Any(row => row.packing_task_item_id != detail.packing_task_item_id
                    || row.sku_id != detail.sku_id
                    || row.picked_qty <= 0
                    || row.pick_qty != row.picked_qty)
                || rows.Sum(row => row.picked_qty) != detail.picked_qty)
            {
                throw DispatchWorkflowCommandException.StockConflict(
                    "dispatch detail allocations are incomplete, excessive or attached to another task item");
            }
        }
    }

    private static void ValidateStocks(
        IReadOnlyCollection<DispatchpicklistEntity> allocations,
        IReadOnlyCollection<StockEntity> stocks,
        bool deduct)
    {
        foreach (var group in allocations.GroupBy(t => t.stock_id))
        {
            var stock = stocks.SingleOrDefault(t => t.id == group.Key);
            if (stock == null || group.Any(allocation => !SameIdentity(stock, allocation)))
            {
                throw DispatchWorkflowCommandException.StockConflict("the exact allocated stock row no longer exists");
            }
            if (deduct && (stock.is_freeze || stock.qty < group.Sum(t => t.picked_qty)))
            {
                throw DispatchWorkflowCommandException.StockConflict("allocated stock has insufficient available quantity");
            }
        }
    }

    private async Task EnsureStocksBelongToOrderWarehouseAsync(
        long erpWarehouseId,
        IReadOnlyCollection<StockEntity> stocks,
        CancellationToken cancellationToken)
    {
        var warehouseIds = await _dbContext.GetDbSet<WarehouseEntity>().AsNoTracking()
            .Where(t => t.erp_warehouse_id == erpWarehouseId && t.is_valid)
            .Select(t => t.id)
            .ToListAsync(cancellationToken);
        if (warehouseIds.Count != 1)
        {
            throw DispatchWorkflowCommandException.StockConflict(
                "ERP warehouse has no unique WMS warehouse mapping");
        }
        var allowedLocationIds = await _dbContext.GetDbSet<GoodslocationEntity>().AsNoTracking()
            .Where(t => t.warehouse_id == warehouseIds[0] && t.is_valid)
            .Select(t => t.id)
            .ToListAsync(cancellationToken);
        if (stocks.Any(t => !allowedLocationIds.Contains(t.goods_location_id)))
        {
            throw DispatchWorkflowCommandException.StockConflict(
                "allocated stock does not belong to the order warehouse");
        }
    }

    private Task<DispatchWorkflowOperationEntity?> FindOutboundOperationAsync(
        int orderId,
        DispatchWorkflowOperation operation,
        string requestId,
        CancellationToken cancellationToken) =>
        _dbContext.GetDbSet<DispatchWorkflowOperationEntity>().AsNoTracking()
            .SingleOrDefaultAsync(t => t.dispatch_order_id == orderId
                && t.operation == operation && t.request_id == requestId
                && t.result_status == DispatchWorkflowOperationResultStatus.Succeeded,
                cancellationToken);

    private void AddSucceededOutboundOperation(
        DispatchOrderEntity order,
        DispatchWorkflowOperation operation,
        string requestId,
        CurrentUser currentUser,
        DateTime now) =>
        _dbContext.GetDbSet<DispatchWorkflowOperationEntity>().Add(new DispatchWorkflowOperationEntity
        {
            dispatch_order_id = order.id,
            operation = operation,
            request_id = requestId,
            result_status = DispatchWorkflowOperationResultStatus.Succeeded,
            result_order_status = order.status,
            result_row_version = order.row_version,
            create_operator = currentUser.user_id,
            create_operator_name = Truncate(currentUser.user_name, 128),
            create_time = now
        });

    private static OutboundCommandResult ToOutboundResult(DispatchOrderEntity order, string requestId) => new()
    {
        order_id = order.id,
        request_id = requestId,
        status = ToApiStatus(order.status),
        row_version = order.row_version
    };

    private static OutboundCommandResult OutboundResultFromLedger(
        DispatchWorkflowOperationEntity operation, string requestId)
    {
        if (operation.result_order_status == null || operation.result_row_version == null)
        {
            throw DispatchWorkflowCommandException.ConcurrencyConflict();
        }
        return new OutboundCommandResult
        {
            order_id = operation.dispatch_order_id,
            request_id = requestId,
            status = ToApiStatus(operation.result_order_status.Value),
            row_version = operation.result_row_version.Value
        };
    }

    private static void ValidateOutboundRequest(int orderId, OutboundCommandRequest request)
    {
        if (orderId <= 0 || string.IsNullOrWhiteSpace(request.request_id)
            || request.request_id.Trim().Length > 64 || request.row_version < 0)
        {
            throw new ArgumentException("order id, request_id and row_version are required", nameof(request));
        }
    }

    private static void ValidateSignRequest(int orderId, SignDispatchOrderRequest request)
    {
        if (orderId <= 0 || string.IsNullOrWhiteSpace(request.request_id)
            || request.request_id.Trim().Length > 64 || request.row_version < 0)
        {
            throw new ArgumentException("order id, request_id and row_version are required", nameof(request));
        }
    }

    private static SignDispatchOrderResult ToSignResult(
        DispatchOrderEntity order, string requestId) => new()
        {
            order_id = order.id,
            request_id = requestId.Trim(),
            status = ToApiStatus(order.status),
            row_version = order.row_version,
            signed_qty = order.signed_qty ?? 0,
            damaged_qty = order.damaged_qty ?? 0,
            notification_status = order.notification_status.ToString().ToUpperInvariant()
        };

    private static string Truncate(string? value, int length)
    {
        var normalized = value?.Trim() ?? string.Empty;
        return normalized.Length <= length ? normalized : normalized[..length];
    }
}

public sealed partial class DispatchWorkflowCommandException
{
    public static DispatchWorkflowCommandException StockConflict(string detail) =>
        new("STOCK_CONFLICT", detail);

    public static DispatchWorkflowCommandException StatusNotAllowedForOutbound(bool confirming) =>
        new("STATUS_NOT_ALLOWED", confirming
            ? "only a pending-outbound order can be confirmed"
            : "only an outbound order can be cancelled");

    public static DispatchWorkflowCommandException SignedOrderCannotBeCancelled() =>
        new("ORDER_ALREADY_SIGNED", "a signed outbound order cannot be cancelled");

    public static DispatchWorkflowCommandException StatusNotAllowedForSigning() =>
        new("STATUS_NOT_ALLOWED", "only an outbound order can be signed");
}
