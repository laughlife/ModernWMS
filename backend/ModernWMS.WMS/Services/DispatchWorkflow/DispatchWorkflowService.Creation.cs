using Microsoft.EntityFrameworkCore;
using ModernWMS.Core.JWT;
using ModernWMS.WMS.Entities.Models;
using ModernWMS.WMS.Entities.ViewModels.DispatchWorkflow;
using ModernWMS.WMS.Entities.ViewModels.PackingTask;

namespace ModernWMS.WMS.Services.DispatchWorkflow;

public partial class DispatchWorkflowService
{
    public async Task<DispatchOrderDetailViewModel> CreateAsync(
        CreateDispatchOrderRequest request,
        CurrentUser currentUser,
        CancellationToken cancellationToken = default)
    {
        if (request.source_task_ids == null || request.source_task_ids.Any(t => t <= 0))
        {
            throw new ArgumentException("source_task_ids must contain only positive task identities", nameof(request));
        }

        var taskIds = request.source_task_ids.Distinct().OrderBy(t => t).ToArray();
        if (request.warehouse_id <= 0 || taskIds.Length == 0)
        {
            throw new ArgumentException("warehouse_id and source_task_ids are required");
        }

        await _warehouseAccessService.EnsureAllowedAsync(request.warehouse_id, currentUser);
        var capability = await _sourceReader.VerifyCapabilityAsync(cancellationToken);
        if (!capability.IsSupported)
        {
            throw new InvalidOperationException(capability.Error);
        }

        var snapshots = await _sourceReader.ReadAsync(taskIds, cancellationToken);
        ValidateCreationSnapshots(taskIds, request.warehouse_id, snapshots);

        var idempotencyKey = TaskSetKey(taskIds);
        if (!string.IsNullOrWhiteSpace(request.idempotency_key)
            && !string.Equals(request.idempotency_key.Trim(), idempotencyKey, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("idempotency_key does not match the sorted source_task_ids set", nameof(request));
        }

        await using var transaction = _dbContext.Database.IsRelational()
            ? await _dbContext.Database.BeginTransactionAsync(cancellationToken)
            : null;
        try
        {
            var existing = await _dbContext.GetDbSet<DispatchOrderEntity>()
                .AsNoTracking()
                .SingleOrDefaultAsync(t => t.create_idempotency_key == idempotencyKey, cancellationToken);
            if (existing != null)
            {
                if (existing.warehouse_id != request.warehouse_id)
                {
                    throw new InvalidOperationException("idempotent task set belongs to another warehouse");
                }

                if (transaction != null)
                {
                    await transaction.CommitAsync(cancellationToken);
                }

                return await LoadDetailAsync(existing.id, cancellationToken);
            }

            var occupied = await FindOccupiedTaskIdsAsync(taskIds, cancellationToken);
            if (occupied.Count > 0)
            {
                throw new InvalidOperationException($"packing tasks already belong to an active order: {string.Join(',', occupied.Order())}");
            }

            var now = DateTime.Now;
            var order = new DispatchOrderEntity
            {
                dispatch_no = $"PK{now:yyyyMMddHHmmssfff}{Random.Shared.Next(100, 1000)}",
                create_idempotency_key = idempotencyKey,
                warehouse_id = request.warehouse_id,
                status = DispatchOrderStatus.PendingPick,
                source_version = CombinedVersion(snapshots),
                source_snapshot = SnapshotJson(snapshots),
                tenant_id = currentUser.tenant_id,
                created_by = currentUser.user_id,
                creator = currentUser.user_name,
                create_time = now,
                last_update_time = now,
                packing_tasks = snapshots.OrderBy(t => t.SourceTaskId)
                    .Select(snapshot => CreateTask(snapshot, now))
                    .ToList()
            };

            await _dbContext.GetDbSet<DispatchOrderEntity>().AddAsync(order, cancellationToken);
            if (transaction == null)
            {
                await EnsureCreationSourceUnchangedAsync(taskIds, snapshots, cancellationToken);
                await _dbContext.SaveChangesAsync(cancellationToken);
            }
            else
            {
                await _dbContext.SaveChangesAsync(cancellationToken);
                await EnsureCreationSourceUnchangedAsync(taskIds, snapshots, cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }

            return await LoadDetailAsync(order.id, cancellationToken);
        }
        catch (DbUpdateException exception) when (exception is not DbUpdateConcurrencyException)
        {
            if (transaction != null)
            {
                await transaction.RollbackAsync(cancellationToken);
            }

            _dbContext.ChangeTracker.Clear();
            var concurrentOrder = await _dbContext.GetDbSet<DispatchOrderEntity>()
                .AsNoTracking()
                .SingleOrDefaultAsync(t => t.create_idempotency_key == idempotencyKey, cancellationToken);
            if (concurrentOrder != null && concurrentOrder.warehouse_id == request.warehouse_id)
            {
                return await LoadDetailAsync(concurrentOrder.id, cancellationToken);
            }

            var occupied = await FindOccupiedTaskIdsAsync(taskIds, cancellationToken);
            if (occupied.Count > 0)
            {
                throw new InvalidOperationException(
                    $"packing tasks already belong to an active order: {string.Join(',', occupied.Order())}");
            }

            throw new InvalidOperationException(
                "dispatch order creation conflicted with another concurrent request",
                exception);
        }
    }

    private Task<List<long>> FindOccupiedTaskIdsAsync(
        IReadOnlyCollection<long> taskIds,
        CancellationToken cancellationToken) =>
        _dbContext.GetDbSet<DispatchPackingTaskEntity>()
            .AsNoTracking()
            .Where(t => t.active_source_task_id != null && taskIds.Contains(t.active_source_task_id.Value))
            .Select(t => t.source_task_id)
            .ToListAsync(cancellationToken);

    private async Task EnsureCreationSourceUnchangedAsync(
        IReadOnlyCollection<long> taskIds,
        IReadOnlyList<PackingTaskSourceSnapshot> snapshots,
        CancellationToken cancellationToken)
    {
        var commitSnapshots = await _sourceReader.ReadAsync(taskIds, cancellationToken);
        if (!string.Equals(SnapshotJson(commitSnapshots), SnapshotJson(snapshots), StringComparison.Ordinal))
        {
            throw new DbUpdateConcurrencyException("packing task source changed during dispatch order creation");
        }
    }

    private static void ValidateCreationSnapshots(
        IReadOnlyCollection<long> requestedTaskIds,
        long warehouseId,
        IReadOnlyList<PackingTaskSourceSnapshot> snapshots)
    {
        var returnedIds = snapshots.Select(t => t.SourceTaskId).Order().ToArray();
        if (!requestedTaskIds.Order().SequenceEqual(returnedIds))
        {
            throw new InvalidOperationException("one or more packing tasks are missing");
        }

        if (snapshots.Any(t => t.IsCancelled))
        {
            throw new InvalidOperationException("cancelled packing task cannot enter a WMS order");
        }

        if (snapshots.Any(t => t.WarehouseId != warehouseId))
        {
            throw new InvalidOperationException("packing tasks from different warehouses cannot be merged");
        }
    }

    private static DispatchPackingTaskEntity CreateTask(PackingTaskSourceSnapshot snapshot, DateTime now)
    {
        var task = new DispatchPackingTaskEntity
        {
            task_no = snapshot.TaskNo,
            source_task_id = snapshot.SourceTaskId,
            source_task_no = snapshot.TaskNo,
            source_cartons_json = snapshot.CartonsJson,
            status = DispatchOrderStatus.PendingPick,
            expected_box_count = snapshot.Boxes.Count,
            measured_box_count = 0,
            source_version = snapshot.SourceVersion,
            stable_box_identity_verified = snapshot.Boxes.All(t => !string.IsNullOrWhiteSpace(t.SourceBoxIdentity)),
            create_time = now,
            last_update_time = now,
            items = snapshot.Items.Select(item => CreateItem(item, snapshot.SourceVersion, now)).ToList()
        };
        task.SetActiveState(true);
        return task;
    }

    private static DispatchPackingTaskItemEntity CreateItem(
        PackingTaskSourceItem item,
        string sourceVersion,
        DateTime now) => new()
        {
            source_item_id = item.SourceItemId,
            source_commodity_id = item.CommodityId,
            commodity_sku = item.CommoditySku,
            commodity_name = item.CommodityName,
            fn_sku = item.FnSku,
            msku = item.Msku,
            required_qty = item.Quantity,
            source_quantity_shipped = item.Quantity,
            source_version = sourceVersion,
            source_snapshot = item.SourceSnapshot,
            is_active = true,
            create_time = now,
            last_update_time = now
        };
}
