using System.Data;
using Microsoft.EntityFrameworkCore;
using MySql.Data.MySqlClient;
using ModernWMS.Core.JWT;
using ModernWMS.WMS.Entities.Models;
using ModernWMS.WMS.Entities.ViewModels.DispatchWorkflow;

namespace ModernWMS.WMS.Services.DispatchWorkflow;

public partial class DispatchWorkflowService
{
    public async Task<CompletePickingResult> CompletePickingAsync(
        int orderId,
        CompletePickingRequest request,
        CurrentUser currentUser,
        CancellationToken cancellationToken = default)
    {
        if (orderId <= 0 || string.IsNullOrWhiteSpace(request.request_id)
            || request.request_id.Trim().Length > 64 || request.row_version < 0)
        {
            throw new ArgumentException("order id, request_id and row_version are required", nameof(request));
        }

        var requestId = request.request_id.Trim();
        await using var transaction = _dbContext.Database.IsRelational()
            ? await _dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
            : null;

        try
        {
            var order = await _dbContext.GetDbSet<DispatchOrderEntity>()
                .Include(t => t.packing_tasks.Where(task => task.is_active))
                    .ThenInclude(task => task.items)
                .SingleOrDefaultAsync(t => t.id == orderId, cancellationToken)
                ?? throw new KeyNotFoundException($"dispatch order not found: {orderId}");
            await _warehouseAccessService.EnsureAllowedAsync(order.warehouse_id, currentUser);

            var operationSet = _dbContext.GetDbSet<DispatchWorkflowOperationEntity>();
            var previous = await operationSet.AsNoTracking().SingleOrDefaultAsync(
                t => t.dispatch_order_id == orderId
                    && t.operation == DispatchWorkflowOperation.CompletePicking
                    && t.request_id == requestId,
                cancellationToken);
            if (previous?.result_status == DispatchWorkflowOperationResultStatus.Succeeded)
            {
                if (transaction != null)
                {
                    await transaction.CommitAsync(cancellationToken);
                }

                return FromLedger(previous);
            }

            if (order.status != DispatchOrderStatus.PendingPick)
            {
                throw DispatchWorkflowCommandException.StatusNotAllowed();
            }

            if (order.row_version != request.row_version)
            {
                throw DispatchWorkflowCommandException.ConcurrencyConflict();
            }

            var activeTasks = order.packing_tasks.Where(t => t.is_active).ToList();
            var snapshots = await _sourceReader.ReadAsync(
                activeTasks.Select(t => t.source_task_id).ToArray(), cancellationToken);
            if (snapshots.Count != activeTasks.Count)
            {
                throw DispatchWorkflowCommandException.SourceChanged();
            }

            if (snapshots.Where(t => !t.IsCancelled).Any(t => t.WarehouseId != order.warehouse_id))
            {
                throw DispatchWorkflowCommandException.SourceChanged();
            }

            IReadOnlyDictionary<long, int> skuMappings;
            try
            {
                skuMappings = await ResolveCurrentSkuMappingsAsync(
                    snapshots, order.tenant_id, cancellationToken);
            }
            catch (InvalidOperationException exception)
            {
                throw DispatchWorkflowCommandException.StockShortage(exception.Message);
            }

            var now = DateTime.Now;
            foreach (var task in activeTasks)
            {
                var snapshot = snapshots.Single(t => t.SourceTaskId == task.source_task_id);
                if (snapshot.IsCancelled)
                {
                    await CancelTaskAsync(task, now, cancellationToken);
                }
                else if (!string.Equals(task.source_version, snapshot.SourceVersion, StringComparison.Ordinal))
                {
                    await RemoveTaskAllocationsAsync(task, cancellationToken);
                    RebuildTaskItems(task, snapshot, skuMappings, now);
                }
                else
                {
                    RefreshTaskSkuMappings(task, snapshot, skuMappings, now);
                }
            }

            var remainingTasks = activeTasks.Where(t => t.is_active).ToList();
            if (remainingTasks.Count == 0)
            {
                var commitCancelledSnapshots = await _sourceReader.ReadAsync(
                    activeTasks.Select(t => t.source_task_id).ToArray(), cancellationToken);
                if (!string.Equals(SnapshotJson(commitCancelledSnapshots), SnapshotJson(snapshots), StringComparison.Ordinal))
                {
                    throw DispatchWorkflowCommandException.SourceChanged();
                }

                order.status = DispatchOrderStatus.SourceCancelled;
                order.source_version = CombinedVersion(snapshots);
                order.source_snapshot = SnapshotJson(snapshots);
                order.last_update_time = now;
                order.row_version++;
                operationSet.Add(CreateSucceededOperation(order, requestId, currentUser, now));
                await _dbContext.SaveChangesAsync(cancellationToken);
                if (transaction != null)
                {
                    await transaction.CommitAsync(cancellationToken);
                }

                return ToPickingResult(order, requestId);
            }

            var items = remainingTasks.SelectMany(t => t.items.Where(i => i.is_active))
                .OrderBy(t => t.packing_task_id)
                .ThenBy(t => t.source_item_id)
                .ToList();
            if (items.Count == 0 || items.Any(t => t.wms_sku_id is null or <= 0 || t.required_qty is null or <= 0))
            {
                throw DispatchWorkflowCommandException.StockShortage("packing task item has no unambiguous WMS SKU mapping");
            }

            var allocations = await BuildAllocationPlanAsync(
                order.warehouse_id, items, cancellationToken);

            var commitSnapshots = await _sourceReader.ReadAsync(
                activeTasks.Select(t => t.source_task_id).ToArray(), cancellationToken);
            if (!string.Equals(SnapshotJson(commitSnapshots), SnapshotJson(snapshots), StringComparison.Ordinal))
            {
                throw DispatchWorkflowCommandException.SourceChanged();
            }

            var details = new Dictionary<DispatchPackingTaskItemEntity, DispatchlistEntity>();
            foreach (var item in items)
            {
                var task = remainingTasks.Single(t => t.id == item.packing_task_id);
                var detail = new DispatchlistEntity
                {
                    dispatch_order_id = order.id,
                    dispatch_order = order,
                    packing_task_id = task.id,
                    packing_task = task,
                    packing_task_item_id = item.id,
                    packing_task_item = item,
                    dispatch_no = order.dispatch_no,
                    dispatch_status = 3,
                    sku_id = item.wms_sku_id!.Value,
                    qty = item.required_qty!.Value,
                    lock_qty = item.required_qty.Value,
                    picked_qty = item.required_qty.Value,
                    creator = currentUser.user_name,
                    create_time = now,
                    last_update_time = now,
                    tenant_id = order.tenant_id,
                    pick_checker_id = currentUser.user_id,
                    pick_checker = currentUser.user_name
                };
                details.Add(item, detail);
                _dbContext.GetDbSet<DispatchlistEntity>().Add(detail);
            }

            foreach (var allocation in allocations)
            {
                var stock = allocation.Stock;
                _dbContext.GetDbSet<DispatchpicklistEntity>().Add(new DispatchpicklistEntity
                {
                    Dispatchlist = details[allocation.Item],
                    packing_task_item = allocation.Item,
                    stock_id = stock.id,
                    goods_owner_id = stock.goods_owner_id,
                    goods_location_id = stock.goods_location_id,
                    sku_id = stock.sku_id,
                    pick_qty = allocation.Quantity,
                    picked_qty = allocation.Quantity,
                    is_update_stock = false,
                    last_update_time = now,
                    series_number = stock.series_number,
                    expiry_date = stock.expiry_date,
                    price = stock.price,
                    putaway_date = stock.putaway_date,
                    picker_id = currentUser.user_id,
                    picker = currentUser.user_name
                });
            }

            foreach (var task in remainingTasks)
            {
                task.status = DispatchOrderStatus.Picked;
                task.last_update_time = now;
                task.row_version++;
            }

            order.status = DispatchOrderStatus.Picked;
            order.source_version = CombinedVersion(snapshots);
            order.source_snapshot = SnapshotJson(snapshots);
            order.last_update_time = now;
            order.row_version++;
            operationSet.Add(CreateSucceededOperation(order, requestId, currentUser, now));

            await _dbContext.SaveChangesAsync(cancellationToken);
            if (transaction != null)
            {
                await transaction.CommitAsync(cancellationToken);
            }

            return ToPickingResult(order, requestId);
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
                .SingleOrDefaultAsync(
                    t => t.dispatch_order_id == orderId
                        && t.operation == DispatchWorkflowOperation.CompletePicking
                        && t.request_id == requestId,
                    CancellationToken.None);
            if (winner?.result_status == DispatchWorkflowOperationResultStatus.Succeeded)
            {
                return FromLedger(winner);
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

    private static bool IsDatabaseConcurrency(Exception exception)
    {
        if (exception is DbUpdateConcurrencyException)
        {
            return true;
        }

        for (Exception? current = exception; current != null; current = current.InnerException)
        {
            if (current is MySqlException mysqlException
                && (mysqlException.Number is 1062 or 1205 or 1213
                    || mysqlException.Code is 1062 or 1205 or 1213
                    || string.Equals(mysqlException.SqlState, "40001", StringComparison.Ordinal)))
            {
                return true;
            }
        }

        return false;
    }

    private static DispatchWorkflowOperationEntity CreateSucceededOperation(
        DispatchOrderEntity order,
        string requestId,
        CurrentUser currentUser,
        DateTime now) => new()
        {
            dispatch_order_id = order.id,
            operation = DispatchWorkflowOperation.CompletePicking,
            request_id = requestId,
            result_status = DispatchWorkflowOperationResultStatus.Succeeded,
            result_order_status = order.status,
            result_row_version = order.row_version,
            create_operator = currentUser.user_id,
            create_operator_name = currentUser.user_name,
            create_time = now
        };

    private static CompletePickingResult ToPickingResult(DispatchOrderEntity order, string requestId) => new()
    {
        order_id = order.id,
        request_id = requestId,
        status = ToApiStatus(order.status),
        row_version = order.row_version
    };

    private static CompletePickingResult FromLedger(DispatchWorkflowOperationEntity operation)
    {
        if (operation.result_order_status == null || operation.result_row_version == null)
        {
            throw DispatchWorkflowCommandException.ConcurrencyConflict();
        }

        return new CompletePickingResult
        {
            order_id = operation.dispatch_order_id,
            request_id = operation.request_id,
            status = ToApiStatus(operation.result_order_status.Value),
            row_version = operation.result_row_version.Value
        };
    }

    private async Task<List<PickingAllocation>> BuildAllocationPlanAsync(
        long erpWarehouseId,
        IReadOnlyList<DispatchPackingTaskItemEntity> items,
        CancellationToken cancellationToken)
    {
        var warehouseIds = await _dbContext.GetDbSet<WarehouseEntity>().AsNoTracking()
            .Where(t => t.erp_warehouse_id == erpWarehouseId && t.is_valid)
            .Select(t => t.id)
            .ToListAsync(cancellationToken);
        if (warehouseIds.Count != 1)
        {
            throw DispatchWorkflowCommandException.StockShortage("ERP warehouse has no unique WMS warehouse mapping");
        }

        var locationIds = await _dbContext.GetDbSet<GoodslocationEntity>().AsNoTracking()
            .Where(t => t.warehouse_id == warehouseIds[0] && t.is_valid && t.warehouse_area_property != 5)
            .Select(t => t.id)
            .ToListAsync(cancellationToken);
        var skuIds = items.Select(t => t.wms_sku_id!.Value).Distinct().ToList();
        var stocks = await _dbContext.GetDbSet<StockEntity>().AsNoTracking()
            .Where(t => skuIds.Contains(t.sku_id) && locationIds.Contains(t.goods_location_id))
            .OrderBy(t => t.putaway_date)
            .ThenBy(t => t.expiry_date)
            .ThenBy(t => t.id)
            .ToListAsync(cancellationToken);

        var dispatchLocks = await (
            from detail in _dbContext.GetDbSet<DispatchlistEntity>().AsNoTracking()
            join pick in _dbContext.GetDbSet<DispatchpicklistEntity>().AsNoTracking()
                on detail.id equals pick.dispatchlist_id
            where detail.dispatch_status > 1 && detail.dispatch_status < 6
                && skuIds.Contains(pick.sku_id) && locationIds.Contains(pick.goods_location_id)
            select pick).ToListAsync(cancellationToken);
        var processLocks = await _dbContext.GetDbSet<StockprocessdetailEntity>().AsNoTracking()
            .Where(t => !t.is_update_stock && skuIds.Contains(t.sku_id) && locationIds.Contains(t.goods_location_id))
            .ToListAsync(cancellationToken);
        var moveLocks = await _dbContext.GetDbSet<StockmoveEntity>().AsNoTracking()
            .Where(t => t.move_status == 0 && skuIds.Contains(t.sku_id) && locationIds.Contains(t.orig_goods_location_id))
            .ToListAsync(cancellationToken);

        var available = stocks.Select(stock => new AvailableStock(
            stock,
            stock.is_freeze ? 0 : Math.Max(0, stock.qty -
                dispatchLocks.Where(t => SameIdentity(stock, t)).Sum(t => t.pick_qty) -
                processLocks.Where(t => SameIdentity(stock, t)).Sum(t => t.qty) -
                moveLocks.Where(t => SameIdentity(stock, t)).Sum(t => t.qty))))
            .Where(t => t.Quantity > 0)
            .ToList();

        var plan = new List<PickingAllocation>();
        foreach (var item in items)
        {
            var candidates = available.Where(t => t.Stock.sku_id == item.wms_sku_id).ToList();
            var owners = candidates.Select(t => t.Stock.goods_owner_id).Distinct().ToList();
            if (owners.Count != 1)
            {
                throw DispatchWorkflowCommandException.StockShortage(
                    owners.Count == 0
                        ? $"insufficient stock for SKU {item.commodity_sku}"
                        : $"multiple goods owners match SKU {item.commodity_sku}");
            }

            var remaining = item.required_qty!.Value;
            foreach (var candidate in candidates.Where(t => t.Stock.goods_owner_id == owners[0]))
            {
                var quantity = Math.Min(remaining, candidate.Quantity);
                if (quantity <= 0)
                {
                    continue;
                }

                plan.Add(new PickingAllocation(item, candidate.Stock, quantity));
                candidate.Quantity -= quantity;
                remaining -= quantity;
                if (remaining == 0)
                {
                    break;
                }
            }

            if (remaining != 0)
            {
                throw DispatchWorkflowCommandException.StockShortage(
                    $"insufficient stock for SKU {item.commodity_sku}");
            }
        }

        return plan;
    }

    private static bool SameIdentity(StockEntity stock, DispatchpicklistEntity row) =>
        stock.sku_id == row.sku_id && stock.goods_location_id == row.goods_location_id
        && stock.goods_owner_id == row.goods_owner_id && stock.series_number == row.series_number
        && stock.expiry_date == row.expiry_date && stock.price == row.price
        && stock.putaway_date == row.putaway_date;

    private static bool SameIdentity(StockEntity stock, StockprocessdetailEntity row) =>
        stock.sku_id == row.sku_id && stock.goods_location_id == row.goods_location_id
        && stock.goods_owner_id == row.goods_owner_id && stock.series_number == row.series_number
        && stock.expiry_date == row.expiry_date && stock.price == row.price
        && stock.putaway_date == row.putaway_date;

    private static bool SameIdentity(StockEntity stock, StockmoveEntity row) =>
        stock.sku_id == row.sku_id && stock.goods_location_id == row.orig_goods_location_id
        && stock.goods_owner_id == row.goods_owner_id && stock.series_number == row.series_number
        && stock.expiry_date == row.expiry_date && stock.price == row.price
        && stock.putaway_date == row.putaway_date;

    private sealed record PickingAllocation(
        DispatchPackingTaskItemEntity Item,
        StockEntity Stock,
        int Quantity);

    private sealed class AvailableStock(StockEntity stock, int quantity)
    {
        public StockEntity Stock { get; } = stock;
        public int Quantity { get; set; } = quantity;
    }
}

public sealed partial class DispatchWorkflowCommandException : InvalidOperationException
{
    private DispatchWorkflowCommandException(string errorCode, string detail)
        : base(string.IsNullOrWhiteSpace(detail) ? errorCode : $"{errorCode}: {detail}") =>
        ErrorCode = errorCode;

    public string ErrorCode { get; }

    public static DispatchWorkflowCommandException SourceChanged() =>
        new("SOURCE_CHANGED", "packing task source changed during picking completion");

    public static DispatchWorkflowCommandException StockShortage(string detail) =>
        new("STOCK_SHORTAGE", detail);

    public static DispatchWorkflowCommandException ConcurrencyConflict() =>
        new("CONCURRENCY_CONFLICT", "row version does not match");

    public static DispatchWorkflowCommandException StatusNotAllowed() =>
        new("STATUS_NOT_ALLOWED", "only a pending-pick order can be completed");
}
