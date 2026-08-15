using System.Data;
using Microsoft.EntityFrameworkCore;
using ModernWMS.Core.JWT;
using ModernWMS.WMS.Entities.Models;
using ModernWMS.WMS.Entities.ViewModels.DispatchWorkflow;

namespace ModernWMS.WMS.Services.DispatchWorkflow;

public partial class DispatchWorkflowService
{
    public async Task<PostPickSourceGuardResult> EnsurePostPickSourceCurrentAsync(
        int dispatchOrderId,
        CurrentUser currentUser,
        CancellationToken cancellationToken = default)
    {
        if (dispatchOrderId <= 0)
        {
            throw new ArgumentException("dispatch order id is required", nameof(dispatchOrderId));
        }

        var ownsTransaction = ShouldOwnGuardTransaction(
            _dbContext.Database.IsRelational(), _dbContext.Database.CurrentTransaction != null);
        await using var transaction = ownsTransaction
            ? await _dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
            : null;

        var order = await _dbContext.GetDbSet<DispatchOrderEntity>()
            .Include(t => t.packing_tasks.Where(task => task.is_active))
            .SingleOrDefaultAsync(t => t.id == dispatchOrderId, cancellationToken)
            ?? throw new KeyNotFoundException($"dispatch order not found: {dispatchOrderId}");
        await _warehouseAccessService.EnsureAllowedAsync(order.warehouse_id, currentUser);
        EnsureGuardableStatus(order.status);

        var snapshots = await _sourceReader.ReadAsync(
            order.packing_tasks.Where(t => t.is_active).Select(t => t.source_task_id).ToArray(),
            cancellationToken);
        var currentVersion = CombinedVersion(snapshots);
        var currentSnapshot = SnapshotJson(snapshots);
        var diffSnapshot = SourceDiffJson(order.source_snapshot, currentSnapshot);
        var historicallyAccepted = await _dbContext.GetDbSet<DispatchSourceChangeEventEntity>()
            .AnyAsync(t => t.dispatch_order_id == order.id
                && t.source_version == currentVersion
                && t.decision == DispatchSourceChangeDecision.ContinueShipment, cancellationToken);
        var sourceIsCurrent = string.Equals(order.source_version, currentVersion, StringComparison.Ordinal)
            || string.Equals(order.accepted_source_version, currentVersion, StringComparison.Ordinal)
            || historicallyAccepted;
        if (sourceIsCurrent)
        {
            if (order.source_change_pending
                || !string.IsNullOrEmpty(order.pending_source_version)
                || !string.IsNullOrEmpty(order.source_change_snapshot))
            {
                order.source_change_pending = false;
                order.pending_source_version = string.Empty;
                order.source_change_snapshot = string.Empty;
                order.last_update_time = DateTime.Now;
                order.row_version++;
                await _dbContext.SaveChangesAsync(cancellationToken);
            }
            if (transaction != null)
            {
                await transaction.CommitAsync(cancellationToken);
            }
            return GuardPassed(order, currentVersion);
        }

        var events = _dbContext.GetDbSet<DispatchSourceChangeEventEntity>();
        if (order.status == DispatchOrderStatus.Outbound)
        {
            var anomalyExists = await events.AnyAsync(t => t.dispatch_order_id == order.id
                && t.source_version == currentVersion
                && t.decision == DispatchSourceChangeDecision.OutboundAnomaly, cancellationToken);
            if (!sourceIsCurrent && !anomalyExists)
            {
                events.Add(CreateSourceEvent(order, currentVersion, diffSnapshot,
                    DispatchSourceChangeDecision.OutboundAnomaly, currentUser,
                    "source changed after outbound", DateTime.Now));
                await _dbContext.SaveChangesAsync(cancellationToken);
            }
            if (transaction != null)
            {
                await transaction.CommitAsync(cancellationToken);
            }
            return GuardPassed(order, currentVersion);
        }

        var detectedExists = await events.AnyAsync(t => t.dispatch_order_id == order.id
            && t.source_version == currentVersion
            && t.decision == DispatchSourceChangeDecision.Detected, cancellationToken);
        var now = DateTime.Now;
        if (!detectedExists)
        {
            events.Add(CreateSourceEvent(order, currentVersion, diffSnapshot,
                DispatchSourceChangeDecision.Detected, currentUser,
                "source change detected; awaiting a human decision", now));
        }
        var freezeChanged = !order.source_change_pending
            || !string.Equals(order.pending_source_version, currentVersion, StringComparison.Ordinal)
            || !string.Equals(order.source_change_snapshot, diffSnapshot, StringComparison.Ordinal);
        if (freezeChanged)
        {
            order.source_change_pending = true;
            order.pending_source_version = currentVersion;
            order.source_change_snapshot = diffSnapshot;
            order.last_update_time = now;
            order.row_version++;
        }

        try
        {
            if (!detectedExists || freezeChanged)
            {
                await _dbContext.SaveChangesAsync(cancellationToken);
            }
            if (transaction != null)
            {
                await transaction.CommitAsync(cancellationToken);
            }
            return GuardPending(order, currentVersion);
        }
        catch (Exception exception) when (IsDatabaseConcurrency(exception))
        {
            if (transaction != null)
            {
                await transaction.RollbackAsync(CancellationToken.None);
            }
            _dbContext.ChangeTracker.Clear();
            var winnerOrder = await _dbContext.GetDbSet<DispatchOrderEntity>()
                .AsNoTracking().SingleOrDefaultAsync(t => t.id == dispatchOrderId, CancellationToken.None);
            var winnerDetected = await _dbContext.GetDbSet<DispatchSourceChangeEventEntity>()
                .AsNoTracking().AnyAsync(t => t.dispatch_order_id == dispatchOrderId
                    && t.source_version == currentVersion
                    && t.decision == DispatchSourceChangeDecision.Detected, CancellationToken.None);
            if (winnerOrder?.source_change_pending == true && winnerDetected
                && string.Equals(winnerOrder.pending_source_version, currentVersion, StringComparison.Ordinal)
                && string.Equals(winnerOrder.source_change_snapshot, diffSnapshot, StringComparison.Ordinal))
            {
                return GuardPending(winnerOrder, currentVersion);
            }
            throw DispatchWorkflowCommandException.ConcurrencyConflict();
        }
    }

    public async Task<SourceDecisionResult> DecideSourceChangeAsync(
        int dispatchOrderId,
        SourceDecisionRequest request,
        CurrentUser currentUser,
        CancellationToken cancellationToken = default)
    {
        var decision = ParseDecision(request);
        ValidateDecisionRequest(dispatchOrderId, request);
        var requestId = request.request_id.Trim();
        var sourceVersion = request.source_version.Trim();
        var reason = request.reason.Trim();
        var operation = decision == DispatchSourceChangeDecision.ContinueShipment
            ? DispatchWorkflowOperation.ContinueAfterSourceChange
            : DispatchWorkflowOperation.CancelAfterSourceChange;

        await using var transaction = _dbContext.Database.IsRelational()
            ? await _dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
            : null;
        try
        {
            var order = await _dbContext.GetDbSet<DispatchOrderEntity>()
                .Include(t => t.packing_tasks)
                .SingleOrDefaultAsync(t => t.id == dispatchOrderId, cancellationToken)
                ?? throw new KeyNotFoundException($"dispatch order not found: {dispatchOrderId}");
            await _warehouseAccessService.EnsureAllowedAsync(order.warehouse_id, currentUser);

            var operations = _dbContext.GetDbSet<DispatchWorkflowOperationEntity>();
            var previous = await operations.AsNoTracking().SingleOrDefaultAsync(t =>
                t.dispatch_order_id == order.id
                && (t.operation == DispatchWorkflowOperation.ContinueAfterSourceChange
                    || t.operation == DispatchWorkflowOperation.CancelAfterSourceChange)
                && t.request_id == requestId, cancellationToken);
            if (previous?.result_status == DispatchWorkflowOperationResultStatus.Succeeded)
            {
                if (previous.operation != operation)
                {
                    throw DispatchWorkflowCommandException.IdempotencyConflict();
                }
                var originalEventKey = DecisionEventKey(order.id, operation, requestId);
                var originalEvent = await _dbContext.GetDbSet<DispatchSourceChangeEventEntity>()
                    .AsNoTracking().SingleOrDefaultAsync(t => t.event_idempotency_key == originalEventKey,
                        cancellationToken);
                if (transaction != null)
                {
                    await transaction.CommitAsync(cancellationToken);
                }
                return DecisionFromLedger(previous, request.decision,
                    originalEvent?.source_version ?? sourceVersion);
            }

            if (order.status is DispatchOrderStatus.Outbound
                or DispatchOrderStatus.PendingPick
                or DispatchOrderStatus.SourceCancelled
                or DispatchOrderStatus.ManualCancelled)
            {
                throw DispatchWorkflowCommandException.StatusNotAllowedForSourceDecision();
            }
            if (!order.source_change_pending)
            {
                throw DispatchWorkflowCommandException.SourceDecisionNotPending();
            }
            if (!string.Equals(order.pending_source_version, sourceVersion, StringComparison.Ordinal))
            {
                throw DispatchWorkflowCommandException.SourceVersionConflict();
            }
            if (order.row_version != request.row_version)
            {
                throw DispatchWorkflowCommandException.ConcurrencyConflict();
            }

            var currentSnapshots = await _sourceReader.ReadAsync(
                order.packing_tasks.Where(t => t.is_active).Select(t => t.source_task_id).ToArray(),
                cancellationToken);
            var currentVersion = CombinedVersion(currentSnapshots);
            if (!string.Equals(sourceVersion, currentVersion, StringComparison.Ordinal))
            {
                throw DispatchWorkflowCommandException.SourceVersionConflict();
            }
            var detectionExists = await _dbContext.GetDbSet<DispatchSourceChangeEventEntity>()
                .AnyAsync(t => t.dispatch_order_id == order.id
                    && t.source_version == sourceVersion
                    && t.decision == DispatchSourceChangeDecision.Detected, cancellationToken);
            if (!detectionExists)
            {
                throw DispatchWorkflowCommandException.SourceVersionConflict();
            }

            var now = DateTime.Now;
            if (decision == DispatchSourceChangeDecision.CancelShipment)
            {
                await CancelAfterSourceChangeAsync(order, now, cancellationToken);
            }
            else
            {
                order.accepted_source_version = sourceVersion;
            }

            order.source_change_pending = false;
            order.adjudicated_source_version = sourceVersion;
            order.adjudicated_by = currentUser.user_id;
            order.adjudicated_by_name = currentUser.user_name;
            order.adjudicated_at = now;
            order.adjudication_reason = reason;
            order.last_update_time = now;
            order.row_version++;
            _dbContext.GetDbSet<DispatchSourceChangeEventEntity>().Add(
                CreateSourceEvent(order, sourceVersion, order.source_change_snapshot,
                    decision, currentUser, reason, now, DecisionEventKey(order.id, operation, requestId)));
            order.pending_source_version = string.Empty;
            operations.Add(new DispatchWorkflowOperationEntity
            {
                dispatch_order_id = order.id,
                operation = operation,
                request_id = requestId,
                result_status = DispatchWorkflowOperationResultStatus.Succeeded,
                result_order_status = order.status,
                result_row_version = order.row_version,
                create_operator = currentUser.user_id,
                create_operator_name = currentUser.user_name,
                create_time = now
            });
            await _dbContext.SaveChangesAsync(cancellationToken);
            if (transaction != null)
            {
                await transaction.CommitAsync(cancellationToken);
            }
            return ToDecisionResult(order, requestId, request.decision, sourceVersion);
        }
        catch (Exception exception) when (IsDatabaseConcurrency(exception))
        {
            if (transaction != null)
            {
                await transaction.RollbackAsync(CancellationToken.None);
            }
            _dbContext.ChangeTracker.Clear();
            var winner = await _dbContext.GetDbSet<DispatchWorkflowOperationEntity>()
                .AsNoTracking()
                .SingleOrDefaultAsync(t => t.dispatch_order_id == dispatchOrderId
                    && t.operation == operation && t.request_id == requestId, CancellationToken.None);
            if (winner?.result_status == DispatchWorkflowOperationResultStatus.Succeeded)
            {
                var originalEvent = await _dbContext.GetDbSet<DispatchSourceChangeEventEntity>()
                    .AsNoTracking().SingleOrDefaultAsync(t => t.event_idempotency_key
                        == DecisionEventKey(dispatchOrderId, operation, requestId), CancellationToken.None);
                return DecisionFromLedger(winner, request.decision,
                    originalEvent?.source_version ?? sourceVersion);
            }
            throw DispatchWorkflowCommandException.ConcurrencyConflict();
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

    private async Task CancelAfterSourceChangeAsync(
        DispatchOrderEntity order,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var detailIds = await _dbContext.GetDbSet<DispatchlistEntity>()
            .Where(t => t.dispatch_order_id == order.id)
            .Select(t => t.id)
            .ToListAsync(cancellationToken);
        var allocations = await _dbContext.GetDbSet<DispatchpicklistEntity>()
            .Where(t => detailIds.Contains(t.dispatchlist_id))
            .ToListAsync(cancellationToken);
        if (allocations.Any(t => t.is_update_stock))
        {
            throw DispatchWorkflowCommandException.StockAlreadyDeducted();
        }
        _dbContext.GetDbSet<DispatchpicklistEntity>().RemoveRange(allocations);

        var details = await _dbContext.GetDbSet<DispatchlistEntity>()
            .Where(t => t.dispatch_order_id == order.id)
            .ToListAsync(cancellationToken);
        foreach (var detail in details)
        {
            detail.dispatch_status = 0;
            detail.last_update_time = now;
        }

        var taskIds = order.packing_tasks.Select(t => t.id).ToList();
        var boxes = await _dbContext.GetDbSet<WeighingBoxEntity>()
            .Where(t => taskIds.Contains(t.packing_task_id) && !t.is_invalidated)
            .ToListAsync(cancellationToken);
        foreach (var box in boxes)
        {
            box.is_invalidated = true;
            box.invalidated_at = now;
            box.last_update_time = now;
            box.row_version++;
        }
        foreach (var task in order.packing_tasks)
        {
            task.status = DispatchOrderStatus.ManualCancelled;
            task.last_update_time = now;
            task.row_version++;
        }
        order.status = DispatchOrderStatus.ManualCancelled;
    }

    private static DispatchSourceChangeDecision ParseDecision(SourceDecisionRequest request) =>
        request.decision.Trim().ToUpperInvariant() switch
        {
            "CONTINUE" => DispatchSourceChangeDecision.ContinueShipment,
            "CANCEL" => DispatchSourceChangeDecision.CancelShipment,
            _ => throw new ArgumentException("decision must be CONTINUE or CANCEL", nameof(request))
        };

    private static void ValidateDecisionRequest(int orderId, SourceDecisionRequest request)
    {
        if (orderId <= 0 || string.IsNullOrWhiteSpace(request.decision)
            || string.IsNullOrWhiteSpace(request.source_version) || request.source_version.Trim().Length > 64
            || string.IsNullOrWhiteSpace(request.reason) || request.reason.Trim().Length > 500
            || string.IsNullOrWhiteSpace(request.request_id) || request.request_id.Trim().Length > 64
            || request.row_version < 0)
        {
            throw new ArgumentException(
                "decision, source_version, reason, request_id and row_version are required", nameof(request));
        }
    }

    private static void EnsureGuardableStatus(DispatchOrderStatus status)
    {
        if (status is not (DispatchOrderStatus.Picked or DispatchOrderStatus.Weighing
            or DispatchOrderStatus.PendingOutbound or DispatchOrderStatus.Outbound))
        {
            throw DispatchWorkflowCommandException.StatusNotAllowedForSourceGuard();
        }
    }

    private static DispatchSourceChangeEventEntity CreateSourceEvent(
        DispatchOrderEntity order,
        string sourceVersion,
        string diffSnapshot,
        DispatchSourceChangeDecision decision,
        CurrentUser currentUser,
        string reason,
        DateTime now,
        string? eventIdempotencyKey = null) => new()
        {
            dispatch_order_id = order.id,
            source_version = sourceVersion,
            event_idempotency_key = eventIdempotencyKey
                ?? HashText($"{order.id}|{sourceVersion}|{(byte)decision}"),
            decision = decision,
            operator_id = currentUser.user_id,
            operator_name = currentUser.user_name,
            decision_time = now,
            reason = reason,
            diff_snapshot = diffSnapshot
        };

    private static string DecisionEventKey(
        int orderId, DispatchWorkflowOperation operation, string requestId) =>
        HashText($"{orderId}|{(byte)operation}|{requestId}");

    private static string SourceDiffJson(string acceptedSnapshot, string currentSnapshot) =>
        System.Text.Json.JsonSerializer.Serialize(new
        {
            wms_snapshot = acceptedSnapshot,
            current_source_snapshot = currentSnapshot
        });

    private static PostPickSourceGuardResult GuardPassed(
        DispatchOrderEntity order, string sourceVersion) => new()
        {
            source_change_pending = false,
            source_version = sourceVersion,
            row_version = order.row_version
        };

    private static PostPickSourceGuardResult GuardPending(
        DispatchOrderEntity order, string sourceVersion) => new()
        {
            source_change_pending = true,
            error_code = "SOURCE_CHANGE_PENDING",
            source_version = sourceVersion,
            row_version = order.row_version
        };

    private static bool ShouldOwnGuardTransaction(bool isRelational, bool hasCurrentTransaction) =>
        isRelational && !hasCurrentTransaction;

    private static SourceDecisionResult ToDecisionResult(
        DispatchOrderEntity order, string requestId, string decision, string sourceVersion) => new()
        {
            order_id = order.id,
            request_id = requestId,
            decision = decision.Trim().ToUpperInvariant(),
            source_version = sourceVersion,
            status = ToApiStatus(order.status),
            source_change_pending = order.source_change_pending,
            row_version = order.row_version
        };

    private static SourceDecisionResult DecisionFromLedger(
        DispatchWorkflowOperationEntity operation, string decision, string sourceVersion)
    {
        if (operation.result_order_status == null || operation.result_row_version == null)
        {
            throw DispatchWorkflowCommandException.ConcurrencyConflict();
        }
        return new SourceDecisionResult
        {
            order_id = operation.dispatch_order_id,
            request_id = operation.request_id,
            decision = decision.Trim().ToUpperInvariant(),
            source_version = sourceVersion,
            status = ToApiStatus(operation.result_order_status.Value),
            source_change_pending = false,
            row_version = operation.result_row_version.Value
        };
    }
}

public sealed partial class DispatchWorkflowCommandException
{
    public static DispatchWorkflowCommandException SourceChangePending() =>
        new("SOURCE_CHANGE_PENDING", "source changed after picking and requires a human decision");

    public static DispatchWorkflowCommandException SourceVersionConflict() =>
        new("SOURCE_VERSION_CONFLICT", "source version is not the current pending version");

    public static DispatchWorkflowCommandException SourceDecisionNotPending() =>
        new("SOURCE_DECISION_NOT_PENDING", "the order has no pending source change");

    public static DispatchWorkflowCommandException StockAlreadyDeducted() =>
        new("STOCK_ALREADY_DEDUCTED", "inventory was already deducted and the order cannot be cancelled");

    public static DispatchWorkflowCommandException IdempotencyConflict() =>
        new("IDEMPOTENCY_CONFLICT", "request_id was already used for the opposite source decision");

    public static DispatchWorkflowCommandException StatusNotAllowedForSourceGuard() =>
        new("STATUS_NOT_ALLOWED", "source guard is only valid after picking");

    public static DispatchWorkflowCommandException StatusNotAllowedForSourceDecision() =>
        new("STATUS_NOT_ALLOWED", "source decision is only valid before outbound");
}
