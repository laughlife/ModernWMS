using Microsoft.EntityFrameworkCore;
using ModernWMS.Core.DBContext;
using ModernWMS.Core.JWT;
using ModernWMS.WMS.Entities.Models;
using ModernWMS.WMS.Entities.ViewModels.DispatchWorkflow;
using ModernWMS.WMS.IServices;
using ModernWMS.WMS.IServices.DispatchWorkflow;

namespace ModernWMS.WMS.Services.DispatchWorkflow;

public sealed class DispatchOrderQueryService : IDispatchOrderQueryService
{
    private readonly SqlDBContext _dbContext;
    private readonly IWarehouseAccessService _warehouseAccessService;
    private readonly IDispatchWorkflowService _workflowService;

    public DispatchOrderQueryService(
        SqlDBContext dbContext,
        IWarehouseAccessService warehouseAccessService,
        IDispatchWorkflowService workflowService)
    {
        _dbContext = dbContext;
        _warehouseAccessService = warehouseAccessService;
        _workflowService = workflowService;
    }

    public async Task<DispatchOrderPageResult> PageAsync(
        DispatchOrderPageRequest request,
        CurrentUser currentUser,
        CancellationToken cancellationToken = default)
    {
        await _warehouseAccessService.EnsureAllowedAsync(request.warehouse_id, currentUser);
        if (string.IsNullOrWhiteSpace(request.status)
            || DispatchWorkflowService.ParseApiStatus(request.status) == DispatchOrderStatus.PendingPick)
        {
            await ReconcilePendingOrdersAsync(request.warehouse_id, currentUser, cancellationToken);
        }

        var query = _dbContext.GetDbSet<DispatchOrderEntity>()
            .AsNoTracking()
            .Include(t => t.packing_tasks.Where(task => task.is_active))
            .Where(t => t.warehouse_id == request.warehouse_id);

        if (!string.IsNullOrWhiteSpace(request.status))
        {
            var status = DispatchWorkflowService.ParseApiStatus(request.status);
            query = query.Where(t => t.status == status);
        }

        var keyword = request.keyword.Trim();
        if (keyword.Length > 0)
        {
            query = query.Where(t => t.dispatch_no.Contains(keyword)
                || t.packing_tasks.Any(task => task.is_active && task.source_task_no.Contains(keyword)));
        }

        var totals = await query.CountAsync(cancellationToken);
        var pageIndex = Math.Max(request.pageIndex, 1);
        var pageSize = Math.Clamp(request.pageSize, 1, 200);
        var orders = await query
            .OrderByDescending(t => t.create_time)
            .ThenByDescending(t => t.id)
            .Skip((pageIndex - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var rows = orders.Select(ToSummary).ToList();
        return new DispatchOrderPageResult(rows, totals);
    }

    public async Task<DispatchOrderStatusCounts> CountsAsync(
        long warehouseId,
        CurrentUser currentUser,
        CancellationToken cancellationToken = default)
    {
        await _warehouseAccessService.EnsureAllowedAsync(warehouseId, currentUser);
        await ReconcilePendingOrdersAsync(warehouseId, currentUser, cancellationToken);
        var raw = await _dbContext.GetDbSet<DispatchOrderEntity>()
            .AsNoTracking()
            .Where(t => t.warehouse_id == warehouseId)
            .GroupBy(t => t.status)
            .Select(t => new { Status = t.Key, Count = t.Count() })
            .ToListAsync(cancellationToken);
        var counts = Enum.GetValues<DispatchOrderStatus>()
            .ToDictionary(DispatchWorkflowService.ToApiStatus, _ => 0);
        foreach (var item in raw)
        {
            counts[DispatchWorkflowService.ToApiStatus(item.Status)] = item.Count;
        }

        return new DispatchOrderStatusCounts(counts);
    }

    public async Task<DispatchOrderDetailViewModel> GetAsync(
        int orderId,
        CurrentUser currentUser,
        CancellationToken cancellationToken = default)
    {
        var order = await _dbContext.GetDbSet<DispatchOrderEntity>()
            .AsNoTracking()
            .Include(t => t.packing_tasks.Where(task => task.is_active))
                .ThenInclude(task => task.items.Where(item => item.is_active))
            .SingleOrDefaultAsync(t => t.id == orderId, cancellationToken)
            ?? throw new KeyNotFoundException($"dispatch order not found: {orderId}");
        await _warehouseAccessService.EnsureAllowedAsync(order.warehouse_id, currentUser);
        if (order.status == DispatchOrderStatus.PendingPick)
        {
            return await _workflowService.ReconcileAsync(orderId, currentUser, cancellationToken);
        }

        return DispatchWorkflowService.ToDetail(order);
    }

    private async Task ReconcilePendingOrdersAsync(
        long warehouseId,
        CurrentUser currentUser,
        CancellationToken cancellationToken)
    {
        var pendingIds = await _dbContext.GetDbSet<DispatchOrderEntity>()
            .AsNoTracking()
            .Where(t => t.warehouse_id == warehouseId && t.status == DispatchOrderStatus.PendingPick)
            .OrderBy(t => t.id)
            .Select(t => t.id)
            .ToListAsync(cancellationToken);
        foreach (var id in pendingIds)
        {
            await _workflowService.ReconcileAsync(id, currentUser, cancellationToken);
        }
    }

    private static DispatchOrderSummaryViewModel ToSummary(DispatchOrderEntity order) => new()
    {
        id = order.id,
        dispatch_no = order.dispatch_no,
        warehouse_id = order.warehouse_id,
        status = DispatchWorkflowService.ToApiStatus(order.status),
        packing_task_nos = order.packing_tasks.Where(t => t.is_active)
            .OrderBy(t => t.source_task_no)
            .Select(t => t.source_task_no)
            .ToList(),
        creator = order.creator,
        create_time = order.create_time,
        last_update_time = order.last_update_time,
        source_change_pending = order.source_change_pending
    };
}
