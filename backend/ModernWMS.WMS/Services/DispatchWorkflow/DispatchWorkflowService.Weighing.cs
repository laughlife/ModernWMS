using System.Data;
using Microsoft.EntityFrameworkCore;
using ModernWMS.Core.JWT;
using ModernWMS.WMS.Entities.Models;
using ModernWMS.WMS.Entities.ViewModels.DispatchWorkflow;
using ModernWMS.WMS.Services.PackingTask;

namespace ModernWMS.WMS.Services.DispatchWorkflow;

public partial class DispatchWorkflowService
{
    public async Task<List<WeighingBoxViewModel>> GetTaskBoxesAsync(
        int orderId,
        int packingTaskId,
        CurrentUser currentUser,
        CancellationToken cancellationToken = default)
    {
        if (orderId <= 0 || packingTaskId <= 0)
        {
            throw new ArgumentException("order id and packing task id are required");
        }
        var order = await _dbContext.GetDbSet<DispatchOrderEntity>().AsNoTracking()
            .SingleOrDefaultAsync(t => t.id == orderId, cancellationToken)
            ?? throw new KeyNotFoundException($"dispatch order not found: {orderId}");
        await _warehouseAccessService.EnsureAllowedAsync(order.warehouse_id, currentUser);
        var taskExists = await _dbContext.GetDbSet<DispatchPackingTaskEntity>().AsNoTracking()
            .AnyAsync(t => t.id == packingTaskId && t.dispatch_order_id == orderId && t.is_active,
                cancellationToken);
        if (!taskExists)
        {
            throw new KeyNotFoundException($"packing task not found in dispatch order: {packingTaskId}");
        }
        return await _dbContext.GetDbSet<WeighingBoxEntity>().AsNoTracking()
            .Where(t => t.packing_task_id == packingTaskId && !t.is_invalidated)
            .OrderBy(t => t.box_sequence).ThenBy(t => t.id)
            .Select(t => new WeighingBoxViewModel
            {
                id = t.id,
                packing_task_id = t.packing_task_id,
                source_box_identity = t.source_box_identity,
                box_sequence = t.box_sequence,
                weight = t.weight,
                length = t.length,
                width = t.width,
                height = t.height,
                measurement_status = t.measurement_status,
                copied_from_box_id = t.copied_from_box_id,
                row_version = t.row_version
            }).ToListAsync(cancellationToken);
    }

    public Task<WeighingCommandResult> SaveWeighingBoxAsync(
        int orderId,
        int boxId,
        SaveWeighingBoxRequest request,
        CurrentUser currentUser,
        CancellationToken cancellationToken = default)
    {
        if (boxId <= 0 || request.box_row_version < 0
            || request.weight <= 0 || request.length <= 0 || request.width <= 0 || request.height <= 0)
        {
            throw new ArgumentException("box, row version and four positive measurements are required", nameof(request));
        }
        var ledgerRequestId = ScopedRequestId("SAVE_BOX", boxId.ToString(), request.request_id);
        return ExecuteWeighingMutationAsync(
            orderId, request.request_id, ledgerRequestId, request.row_version,
            DispatchWorkflowOperation.SaveWeighing,
            [DispatchOrderStatus.Weighing], currentUser, async (order, now, ct) =>
            {
                var box = FindAvailableBox(order, boxId);
                if (box.row_version != request.box_row_version)
                {
                    throw DispatchWorkflowCommandException.ConcurrencyConflict();
                }
                ApplyMeasurement(box, request.weight, request.length, request.width, request.height,
                    null, currentUser, now);
                UpdateTaskMeasuredCount(order, box.packing_task_id, now);
                await Task.CompletedTask;
            }, cancellationToken);
    }

    public Task<WeighingCommandResult> CopyWeighingBoxAsync(
        int orderId,
        int targetBoxId,
        CopyWeighingBoxRequest request,
        CurrentUser currentUser,
        CancellationToken cancellationToken = default)
    {
        if (request.source_box_id <= 0 || targetBoxId <= 0
            || request.source_box_id == targetBoxId || request.target_box_row_version < 0)
        {
            throw new ArgumentException("different existing source and target boxes are required", nameof(request));
        }
        var ledgerRequestId = ScopedRequestId(
            "COPY_BOX", $"{request.source_box_id}:{targetBoxId}", request.request_id);
        return ExecuteWeighingMutationAsync(
            orderId, request.request_id, ledgerRequestId, request.row_version,
            DispatchWorkflowOperation.CopyWeighing,
            [DispatchOrderStatus.Weighing], currentUser, async (order, now, ct) =>
            {
                var source = FindAvailableBox(order, request.source_box_id);
                var target = FindAvailableBox(order, targetBoxId);
                if (source.packing_task_id != target.packing_task_id)
                {
                    throw DispatchWorkflowCommandException.BoxNotAvailable(
                        "measurements may only be copied inside one packing task");
                }
                if (target.row_version != request.target_box_row_version)
                {
                    throw DispatchWorkflowCommandException.ConcurrencyConflict();
                }
                if (!HasCompleteMeasurement(source))
                {
                    throw DispatchWorkflowCommandException.WeighingIncomplete(
                        "source box has no complete WMS measurement");
                }
                ApplyMeasurement(target, source.weight!.Value, source.length!.Value,
                    source.width!.Value, source.height!.Value, source.id, currentUser, now);
                UpdateTaskMeasuredCount(order, target.packing_task_id, now);
                await Task.CompletedTask;
            }, cancellationToken);
    }

    public Task<WeighingCommandResult> CompleteTaskWeighingAsync(
        int orderId,
        int packingTaskId,
        WeighingOrderCommandRequest request,
        CurrentUser currentUser,
        CancellationToken cancellationToken = default)
    {
        if (packingTaskId <= 0)
        {
            throw new ArgumentException("packing task id is required", nameof(packingTaskId));
        }
        var ledgerRequestId = ScopedRequestId(
            "COMPLETE_TASK_WEIGHING", packingTaskId.ToString(), request.request_id);
        return ExecuteWeighingMutationAsync(
            orderId, request.request_id, ledgerRequestId, request.row_version,
            DispatchWorkflowOperation.CompleteTaskWeighing,
            [DispatchOrderStatus.Weighing], currentUser, async (order, now, ct) =>
            {
                var task = order.packing_tasks.SingleOrDefault(t => t.id == packingTaskId && t.is_active)
                    ?? throw new KeyNotFoundException($"packing task not found in dispatch order: {packingTaskId}");
                var boxes = task.boxes.Where(t => !t.is_invalidated).ToList();
                if (boxes.Count == 0 || boxes.Count != task.expected_box_count || boxes.Any(t => !HasCompleteMeasurement(t)))
                {
                    throw DispatchWorkflowCommandException.WeighingIncomplete(
                        "every current physical box in the task must be measured");
                }
                task.measured_box_count = boxes.Count;
                task.status = DispatchOrderStatus.PendingOutbound;
                task.last_update_time = now;
                task.row_version++;
                await Task.CompletedTask;
            }, cancellationToken);
    }

    public Task<WeighingCommandResult> CompleteOrderWeighingAsync(
        int orderId,
        WeighingOrderCommandRequest request,
        CurrentUser currentUser,
        CancellationToken cancellationToken = default) =>
        ExecuteWeighingMutationAsync(
            orderId, request.request_id, request.request_id, request.row_version,
            DispatchWorkflowOperation.CompleteWeighing,
            [DispatchOrderStatus.Weighing], currentUser, async (order, now, ct) =>
            {
                var tasks = order.packing_tasks.Where(t => t.is_active).ToList();
                if (tasks.Count == 0 || tasks.Any(task =>
                    task.status != DispatchOrderStatus.PendingOutbound
                    || task.boxes.Count(t => !t.is_invalidated) == 0
                    || task.boxes.Count(t => !t.is_invalidated) != task.expected_box_count
                    || task.boxes.Where(t => !t.is_invalidated).Any(t => !HasCompleteMeasurement(t))))
                {
                    throw DispatchWorkflowCommandException.WeighingIncomplete(
                        "every active packing task must finish all box measurements");
                }
                order.status = DispatchOrderStatus.PendingOutbound;
                await Task.CompletedTask;
            }, cancellationToken);

    public async Task<WeighingCommandResult> StartWeighingAsync(
        int orderId,
        WeighingOrderCommandRequest request,
        CurrentUser currentUser,
        CancellationToken cancellationToken = default)
    {
        ValidateOrderCommand(orderId, request.request_id, request.row_version);
        var requestId = request.request_id;
        await using var transaction = _dbContext.Database.IsRelational()
            ? await _dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
            : null;
        var transactionCompleted = false;

        try
        {
            var order = await _dbContext.GetDbSet<DispatchOrderEntity>()
                .Include(t => t.packing_tasks.Where(task => task.is_active))
                .SingleOrDefaultAsync(t => t.id == orderId, cancellationToken)
                ?? throw new KeyNotFoundException($"dispatch order not found: {orderId}");
            await _warehouseAccessService.EnsureAllowedAsync(order.warehouse_id, currentUser);

            var previous = await FindSucceededOperationAsync(
                orderId, DispatchWorkflowOperation.StartWeighing, requestId, cancellationToken);
            if (previous != null)
            {
                if (transaction != null)
                {
                    await transaction.CommitAsync(cancellationToken);
                    transactionCompleted = true;
                }
                return WeighingResultFromLedger(previous, requestId);
            }

            if (order.status != DispatchOrderStatus.Picked)
            {
                throw DispatchWorkflowCommandException.StatusNotAllowedForWeighing();
            }
            if (order.row_version != request.row_version)
            {
                throw DispatchWorkflowCommandException.ConcurrencyConflict();
            }

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

            var capability = await _sourceReader.VerifyCapabilityAsync(cancellationToken);
            if (!capability.IsSupported)
            {
                throw DispatchWorkflowCommandException.SourceBoxIdentityUnsupported(capability.Error);
            }

            var tasks = order.packing_tasks.Where(t => t.is_active).OrderBy(t => t.id).ToList();

            var now = DateTime.Now;
            foreach (var task in tasks)
            {
                var parsed = SellFoxCartonParser.Parse(task.source_cartons_json);
                if (!parsed.IsSupported)
                {
                    throw DispatchWorkflowCommandException.SourceBoxIdentityUnsupported(
                        $"packing task {task.source_task_no}: {parsed.Error}");
                }

                foreach (var sourceBox in parsed.Boxes.OrderBy(t => t.Sequence))
                {
                    _dbContext.GetDbSet<WeighingBoxEntity>().Add(new WeighingBoxEntity
                    {
                        packing_task_id = task.id,
                        box_identity = HashText($"{task.source_task_id}:{sourceBox.SourceBoxIdentity.Trim()}"),
                        source_box_identity = sourceBox.SourceBoxIdentity.Trim(),
                        box_sequence = sourceBox.Sequence,
                        measurement_status = "UNMEASURED",
                        source_snapshot = sourceBox.SourceSnapshot,
                        is_invalidated = false,
                        create_time = now,
                        last_update_time = now
                    });
                }

                task.status = DispatchOrderStatus.Weighing;
                task.expected_box_count = parsed.Boxes.Count;
                task.measured_box_count = 0;
                task.stable_box_identity_verified = true;
                task.box_identity_validation_error = string.Empty;
                task.last_update_time = now;
                task.row_version++;
            }

            order.status = DispatchOrderStatus.Weighing;
            order.last_update_time = now;
            order.row_version++;
            AddSucceededWeighingOperation(
                order, DispatchWorkflowOperation.StartWeighing, requestId, currentUser, now);
            await _dbContext.SaveChangesAsync(cancellationToken);
            if (transaction != null)
            {
                await transaction.CommitAsync(cancellationToken);
                transactionCompleted = true;
            }
            return ToWeighingResult(order, requestId);
        }
        catch (Exception exception) when (IsDatabaseConcurrency(exception))
        {
            if (transaction != null && !transactionCompleted)
            {
                await transaction.RollbackAsync(CancellationToken.None);
            }
            _dbContext.ChangeTracker.Clear();
            var winner = await FindSucceededOperationAsync(
                orderId, DispatchWorkflowOperation.StartWeighing, requestId, CancellationToken.None);
            if (winner != null)
            {
                return WeighingResultFromLedger(winner, requestId);
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

    private static void ValidateOrderCommand(int orderId, string requestId, long rowVersion)
    {
        if (orderId <= 0 || string.IsNullOrWhiteSpace(requestId)
            || requestId.Length > 64
            || !string.Equals(requestId, requestId.Trim(), StringComparison.Ordinal)
            || rowVersion < 0)
        {
            throw new ArgumentException("order id, request_id and row_version are required");
        }
    }

    private async Task<WeighingCommandResult> ExecuteWeighingMutationAsync(
        int orderId,
        string clientRequestId,
        string ledgerRequestId,
        long rowVersion,
        DispatchWorkflowOperation operation,
        IReadOnlyCollection<DispatchOrderStatus> allowedStatuses,
        CurrentUser currentUser,
        Func<DispatchOrderEntity, DateTime, CancellationToken, Task> mutation,
        CancellationToken cancellationToken)
    {
        ValidateOrderCommand(orderId, clientRequestId, rowVersion);
        await using var transaction = _dbContext.Database.IsRelational()
            ? await _dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
            : null;
        var transactionCompleted = false;
        try
        {
            var order = await _dbContext.GetDbSet<DispatchOrderEntity>()
                .Include(t => t.packing_tasks.Where(task => task.is_active))
                    .ThenInclude(task => task.boxes)
                .SingleOrDefaultAsync(t => t.id == orderId, cancellationToken)
                ?? throw new KeyNotFoundException($"dispatch order not found: {orderId}");
            await _warehouseAccessService.EnsureAllowedAsync(order.warehouse_id, currentUser);
            var previous = await FindSucceededOperationAsync(
                orderId, operation, ledgerRequestId, cancellationToken);
            if (previous != null)
            {
                if (transaction != null)
                {
                    await transaction.CommitAsync(cancellationToken);
                    transactionCompleted = true;
                }
                return WeighingResultFromLedger(previous, clientRequestId);
            }
            if (!allowedStatuses.Contains(order.status))
            {
                throw DispatchWorkflowCommandException.StatusNotAllowedForWeighing();
            }
            if (order.row_version != rowVersion)
            {
                throw DispatchWorkflowCommandException.ConcurrencyConflict();
            }
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

            var now = DateTime.Now;
            await mutation(order, now, cancellationToken);
            order.last_update_time = now;
            order.row_version++;
            AddSucceededWeighingOperation(order, operation, ledgerRequestId, currentUser, now);
            await _dbContext.SaveChangesAsync(cancellationToken);
            if (transaction != null)
            {
                await transaction.CommitAsync(cancellationToken);
                transactionCompleted = true;
            }
            return ToWeighingResult(order, clientRequestId);
        }
        catch (Exception exception) when (IsDatabaseConcurrency(exception))
        {
            if (transaction != null && !transactionCompleted)
            {
                await transaction.RollbackAsync(CancellationToken.None);
            }
            _dbContext.ChangeTracker.Clear();
            var winner = await FindSucceededOperationAsync(
                orderId, operation, ledgerRequestId, CancellationToken.None);
            if (winner != null)
            {
                return WeighingResultFromLedger(winner, clientRequestId);
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

    private static WeighingBoxEntity FindAvailableBox(DispatchOrderEntity order, int boxId) =>
        order.packing_tasks.Where(t => t.is_active).SelectMany(t => t.boxes)
            .SingleOrDefault(t => t.id == boxId && !t.is_invalidated)
        ?? throw DispatchWorkflowCommandException.BoxNotAvailable(
            "box does not belong to the active packing tasks of this order");

    private static bool HasCompleteMeasurement(WeighingBoxEntity box) =>
        box.measurement_status == "MEASURED"
        && box.weight > 0 && box.length > 0 && box.width > 0 && box.height > 0;

    private static void ApplyMeasurement(
        WeighingBoxEntity box,
        decimal weight,
        decimal length,
        decimal width,
        decimal height,
        int? copiedFromBoxId,
        CurrentUser currentUser,
        DateTime now)
    {
        box.weight = weight;
        box.length = length;
        box.width = width;
        box.height = height;
        box.measurement_status = "MEASURED";
        box.measured_by = currentUser.user_id;
        box.measured_by_name = currentUser.user_name;
        box.measured_at = now;
        box.copied_from_box_id = copiedFromBoxId;
        box.last_update_time = now;
        box.row_version++;
    }

    private static void UpdateTaskMeasuredCount(
        DispatchOrderEntity order,
        int packingTaskId,
        DateTime now)
    {
        var task = order.packing_tasks.Single(t => t.id == packingTaskId);
        task.measured_box_count = task.boxes.Count(t => !t.is_invalidated && HasCompleteMeasurement(t));
        task.last_update_time = now;
        task.row_version++;
    }

    private Task<DispatchWorkflowOperationEntity?> FindSucceededOperationAsync(
        int orderId,
        DispatchWorkflowOperation operation,
        string requestId,
        CancellationToken cancellationToken) =>
        _dbContext.GetDbSet<DispatchWorkflowOperationEntity>().AsNoTracking()
            .SingleOrDefaultAsync(t => t.dispatch_order_id == orderId
                && t.operation == operation && t.request_id == requestId
                && t.result_status == DispatchWorkflowOperationResultStatus.Succeeded, cancellationToken);

    private void AddSucceededWeighingOperation(
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
            create_operator_name = currentUser.user_name,
            create_time = now
        });

    private static WeighingCommandResult ToWeighingResult(DispatchOrderEntity order, string requestId) => new()
    {
        order_id = order.id,
        request_id = requestId,
        status = ToApiStatus(order.status),
        row_version = order.row_version
    };

    private static WeighingCommandResult WeighingResultFromLedger(
        DispatchWorkflowOperationEntity operation,
        string clientRequestId)
    {
        if (operation.result_order_status == null || operation.result_row_version == null)
        {
            throw DispatchWorkflowCommandException.ConcurrencyConflict();
        }
        return new WeighingCommandResult
        {
            order_id = operation.dispatch_order_id,
            request_id = clientRequestId,
            status = ToApiStatus(operation.result_order_status.Value),
            row_version = operation.result_row_version.Value
        };
    }

    private static string ScopedRequestId(string kind, string resourceId, string clientRequestId)
    {
        if (string.IsNullOrWhiteSpace(clientRequestId) || clientRequestId.Length > 64
            || !string.Equals(clientRequestId, clientRequestId.Trim(), StringComparison.Ordinal))
        {
            throw new ArgumentException("request_id must be non-empty, canonical and at most 64 characters");
        }
        return HashText($"{kind}|{resourceId}|{clientRequestId}");
    }
}

public sealed partial class DispatchWorkflowCommandException
{
    public static DispatchWorkflowCommandException StatusNotAllowedForWeighing() =>
        new("STATUS_NOT_ALLOWED", "weighing command is not allowed for the current order status");

    public static DispatchWorkflowCommandException SourceBoxIdentityUnsupported(string detail) =>
        new("SOURCE_BOX_ID_UNSUPPORTED", detail);

    public static DispatchWorkflowCommandException BoxNotAvailable(string detail) =>
        new("BOX_NOT_AVAILABLE", detail);

    public static DispatchWorkflowCommandException WeighingIncomplete(string detail) =>
        new("WEIGHING_INCOMPLETE", detail);
}
