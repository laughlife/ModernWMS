using Microsoft.EntityFrameworkCore;
using ModernWMS.Core.JWT;
using ModernWMS.WMS.Entities.Models;
using ModernWMS.WMS.Entities.ViewModels.DispatchWorkflow;
using ModernWMS.WMS.Entities.ViewModels.PackingTask;

namespace ModernWMS.WMS.Services.DispatchWorkflow;

public partial class DispatchWorkflowService
{
    public async Task<DispatchOrderDetailViewModel> ReconcileAsync(
        int orderId,
        CurrentUser currentUser,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = _dbContext.Database.IsRelational()
            ? await _dbContext.Database.BeginTransactionAsync(cancellationToken)
            : null;
        var order = await _dbContext.GetDbSet<DispatchOrderEntity>()
            .Include(t => t.packing_tasks.Where(task => task.is_active))
                .ThenInclude(task => task.items)
            .SingleOrDefaultAsync(t => t.id == orderId, cancellationToken)
            ?? throw new KeyNotFoundException($"dispatch order not found: {orderId}");
        await _warehouseAccessService.EnsureAllowedAsync(order.warehouse_id, currentUser);
        if (order.status != DispatchOrderStatus.PendingPick)
        {
            return await LoadDetailAsync(orderId, cancellationToken);
        }

        var activeTasks = order.packing_tasks.Where(t => t.is_active).ToList();
        if (activeTasks.Count == 0)
        {
            order.status = DispatchOrderStatus.SourceCancelled;
            order.last_update_time = DateTime.Now;
            await _dbContext.SaveChangesAsync(cancellationToken);
            if (transaction != null)
            {
                await transaction.CommitAsync(cancellationToken);
            }

            return await LoadDetailAsync(orderId, cancellationToken);
        }

        var snapshots = await _sourceReader.ReadAsync(
            activeTasks.Select(t => t.source_task_id).ToArray(),
            cancellationToken);
        if (snapshots.Count != activeTasks.Count)
        {
            throw new InvalidOperationException("one or more packing tasks are missing during reconciliation");
        }

        if (snapshots.Where(t => !t.IsCancelled).Any(t => t.WarehouseId != order.warehouse_id))
        {
            throw new InvalidOperationException("packing task warehouse changed; order reconciliation rejected");
        }

        var skuMappings = await ResolveCurrentSkuMappingsAsync(
            snapshots, cancellationToken);
        var now = DateTime.Now;
        foreach (var task in activeTasks)
        {
            var snapshot = snapshots.Single(t => t.SourceTaskId == task.source_task_id);
            if (snapshot.IsCancelled)
            {
                await CancelTaskAsync(task, now, cancellationToken);
                continue;
            }

            if (!string.Equals(task.source_version, snapshot.SourceVersion, StringComparison.Ordinal))
            {
                await RemoveTaskAllocationsAsync(task, cancellationToken);
                RebuildTaskItems(task, snapshot, skuMappings, now);
            }
            else
            {
                RefreshTaskSkuMappings(task, snapshot, skuMappings, now);
            }
        }

        var remainingSnapshots = snapshots.Where(t => !t.IsCancelled).ToList();
        order.status = remainingSnapshots.Count == 0
            ? DispatchOrderStatus.SourceCancelled
            : DispatchOrderStatus.PendingPick;
        order.source_version = CombinedVersion(snapshots);
        order.source_snapshot = SnapshotJson(snapshots);
        order.last_update_time = now;
        if (transaction == null)
        {
            await EnsureSourceUnchangedAsync(activeTasks, snapshots, cancellationToken);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        else
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            await EnsureSourceUnchangedAsync(activeTasks, snapshots, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        return await LoadDetailAsync(orderId, cancellationToken);
    }

    private async Task EnsureSourceUnchangedAsync(
        IReadOnlyCollection<DispatchPackingTaskEntity> activeTasks,
        IReadOnlyList<PackingTaskSourceSnapshot> snapshots,
        CancellationToken cancellationToken)
    {
        var commitSnapshots = await _sourceReader.ReadAsync(
            activeTasks.Select(t => t.source_task_id).ToArray(),
            cancellationToken);
        if (!string.Equals(SnapshotJson(commitSnapshots), SnapshotJson(snapshots), StringComparison.Ordinal))
        {
            throw new DbUpdateConcurrencyException("packing task source changed during reconciliation");
        }
    }

    private async Task CancelTaskAsync(
        DispatchPackingTaskEntity task,
        DateTime now,
        CancellationToken cancellationToken)
    {
        await RemoveTaskAllocationsAsync(task, cancellationToken);

        foreach (var item in task.items)
        {
            item.is_active = false;
            item.last_update_time = now;
        }

        task.SetActiveState(false);
        task.source_cancelled_at = now;
        task.status = DispatchOrderStatus.SourceCancelled;
        task.last_update_time = now;
    }

    private async Task RemoveTaskAllocationsAsync(
        DispatchPackingTaskEntity task,
        CancellationToken cancellationToken)
    {
        var itemIds = task.items.Select(t => t.id).Where(t => t > 0).ToList();
        if (itemIds.Count > 0)
        {
            var allocations = await _dbContext.GetDbSet<DispatchpicklistEntity>()
                .Where(t => t.packing_task_item_id != null && itemIds.Contains(t.packing_task_item_id.Value))
                .ToListAsync(cancellationToken);
            if (allocations.Any(t => t.is_update_stock))
            {
                throw new InvalidOperationException(
                    "packing task has allocations that already updated stock; automatic reconciliation is forbidden");
            }

            _dbContext.GetDbSet<DispatchpicklistEntity>().RemoveRange(
                allocations.Where(t => !t.is_update_stock));
        }
    }

    private static void RebuildTaskItems(
        DispatchPackingTaskEntity task,
        PackingTaskSourceSnapshot snapshot,
        IReadOnlyDictionary<long, int> skuMappings,
        DateTime now)
    {
        var sourceItems = snapshot.Items.ToDictionary(t => t.SourceItemId);
        foreach (var existing in task.items)
        {
            if (sourceItems.TryGetValue(existing.source_item_id, out var current))
            {
                ApplyItem(existing, current, snapshot.SourceVersion, skuMappings, now);
                sourceItems.Remove(existing.source_item_id);
            }
            else
            {
                existing.is_active = false;
                existing.last_update_time = now;
            }
        }

        foreach (var item in sourceItems.Values)
        {
            task.items.Add(CreateItem(item, snapshot.SourceVersion, skuMappings, now));
        }

        task.task_no = snapshot.TaskNo;
        task.source_task_no = snapshot.TaskNo;
        task.source_cartons_json = snapshot.CartonsJson;
        task.source_version = snapshot.SourceVersion;
        task.expected_box_count = snapshot.Boxes.Count;
        task.stable_box_identity_verified = snapshot.Boxes.All(t => !string.IsNullOrWhiteSpace(t.SourceBoxIdentity));
        task.last_update_time = now;
    }

    private static void ApplyItem(
        DispatchPackingTaskItemEntity entity,
        PackingTaskSourceItem source,
        string sourceVersion,
        IReadOnlyDictionary<long, int> skuMappings,
        DateTime now)
    {
        entity.source_commodity_id = source.CommodityId;
        entity.wms_sku_id = MappedSkuId(source, skuMappings);
        entity.commodity_sku = source.CommoditySku;
        entity.commodity_name = source.CommodityName;
        entity.fn_sku = source.FnSku;
        entity.msku = source.Msku;
        entity.required_qty = source.Quantity;
        entity.source_quantity_shipped = source.Quantity;
        entity.source_version = sourceVersion;
        entity.source_snapshot = source.SourceSnapshot;
        entity.is_active = true;
        entity.last_update_time = now;
    }

    private static void RefreshTaskSkuMappings(
        DispatchPackingTaskEntity task,
        PackingTaskSourceSnapshot snapshot,
        IReadOnlyDictionary<long, int> skuMappings,
        DateTime now)
    {
        var sourceItems = snapshot.Items.ToDictionary(t => t.SourceItemId);
        foreach (var entity in task.items.Where(t => t.is_active))
        {
            if (!sourceItems.TryGetValue(entity.source_item_id, out var source))
            {
                throw new InvalidOperationException("packing task source version does not match its item snapshot");
            }

            var mappedSkuId = MappedSkuId(source, skuMappings);
            if (entity.wms_sku_id != mappedSkuId)
            {
                entity.wms_sku_id = mappedSkuId;
                entity.last_update_time = now;
            }
        }
    }
}
