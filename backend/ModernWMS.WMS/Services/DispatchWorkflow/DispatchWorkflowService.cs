using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ModernWMS.Core.DBContext;
using ModernWMS.Core.JWT;
using ModernWMS.WMS.Entities.Models;
using ModernWMS.WMS.Entities.ViewModels.DispatchWorkflow;
using ModernWMS.WMS.Entities.ViewModels.PackingTask;
using ModernWMS.WMS.IServices;
using ModernWMS.WMS.IServices.DispatchWorkflow;
using ModernWMS.WMS.IServices.PackingTask;
using ModernWMS.WMS.Services.Dispatchlist;

namespace ModernWMS.WMS.Services.DispatchWorkflow;

public partial class DispatchWorkflowService : IDispatchWorkflowService
{
    private readonly SqlDBContext _dbContext;
    private readonly IPackingTaskSourceReader _sourceReader;
    private readonly IWarehouseAccessService _warehouseAccessService;
    private readonly IDispatchSignNotificationClient? _dispatchSignNotificationClient;

    public DispatchWorkflowService(
        SqlDBContext dbContext,
        IPackingTaskSourceReader sourceReader,
        IWarehouseAccessService warehouseAccessService,
        IDispatchSignNotificationClient? dispatchSignNotificationClient = null)
    {
        _dbContext = dbContext;
        _sourceReader = sourceReader;
        _warehouseAccessService = warehouseAccessService;
        _dispatchSignNotificationClient = dispatchSignNotificationClient;
    }

    public async Task<DispatchOrderDetailViewModel> PrintAsync(
        int orderId,
        CurrentUser currentUser,
        CancellationToken cancellationToken = default)
    {
        var header = await FindOrderAsync(orderId, cancellationToken);
        await _warehouseAccessService.EnsureAllowedAsync(header.warehouse_id, currentUser);
        if (header.status != DispatchOrderStatus.PendingPick)
        {
            throw new InvalidOperationException("only pending-pick orders can be printed");
        }

        var reconciled = await ReconcileAsync(orderId, currentUser, cancellationToken);
        if (!string.Equals(reconciled.status, "PENDING_PICK", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("packing task reconciliation left no printable pending-pick order");
        }

        return reconciled;
    }

    internal async Task<DispatchOrderEntity> FindOrderAsync(int orderId, CancellationToken cancellationToken) =>
        await _dbContext.GetDbSet<DispatchOrderEntity>()
            .SingleOrDefaultAsync(t => t.id == orderId, cancellationToken)
        ?? throw new KeyNotFoundException($"dispatch order not found: {orderId}");

    internal async Task<DispatchOrderDetailViewModel> LoadDetailAsync(
        int orderId,
        CancellationToken cancellationToken)
    {
        var order = await _dbContext.GetDbSet<DispatchOrderEntity>()
            .AsNoTracking()
            .Include(t => t.packing_tasks.Where(task => task.is_active))
                .ThenInclude(task => task.items.Where(item => item.is_active))
            .SingleOrDefaultAsync(t => t.id == orderId, cancellationToken)
            ?? throw new KeyNotFoundException($"dispatch order not found: {orderId}");

        return ToDetail(order);
    }

    internal static DispatchOrderDetailViewModel ToDetail(DispatchOrderEntity order)
    {
        var tasks = order.packing_tasks
            .Where(t => t.is_active)
            .OrderBy(t => t.source_task_no)
            .ThenBy(t => t.source_task_id)
            .Select(t => new DispatchPackingTaskViewModel
            {
                id = t.id,
                source_task_id = t.source_task_id,
                source_task_no = t.source_task_no,
                status = ToApiStatus(t.status),
                source_version = t.source_version,
                expected_box_count = t.expected_box_count,
                measured_box_count = t.measured_box_count,
                items = t.items.Where(i => i.is_active)
                    .OrderBy(i => i.source_item_id)
                    .Select(i => new DispatchPackingTaskItemViewModel
                    {
                        id = i.id,
                        source_item_id = i.source_item_id,
                        source_commodity_id = i.source_commodity_id,
                        wms_sku_id = i.wms_sku_id,
                        commodity_sku = i.commodity_sku,
                        commodity_name = i.commodity_name,
                        fn_sku = i.fn_sku,
                        msku = i.msku,
                        required_qty = i.required_qty,
                        source_stock_available = i.source_stock_available
                    }).ToList()
            }).ToList();

        return new DispatchOrderDetailViewModel
        {
            id = order.id,
            dispatch_no = order.dispatch_no,
            warehouse_id = order.warehouse_id,
            status = ToApiStatus(order.status),
            source_version = order.source_version,
            packing_task_nos = tasks.Select(t => t.source_task_no).ToList(),
            packing_tasks = tasks,
            creator = order.creator,
            create_time = order.create_time,
            last_update_time = order.last_update_time,
            source_change_pending = order.source_change_pending,
            row_version = order.row_version
        };
    }

    internal static string ToApiStatus(DispatchOrderStatus status) => status switch
    {
        DispatchOrderStatus.PendingPick => "PENDING_PICK",
        DispatchOrderStatus.Picked => "PICKED",
        DispatchOrderStatus.Weighing => "WEIGHING",
        DispatchOrderStatus.PendingOutbound => "PENDING_OUTBOUND",
        DispatchOrderStatus.Outbound => "OUTBOUND",
        DispatchOrderStatus.SourceCancelled => "SOURCE_CANCELLED",
        DispatchOrderStatus.ManualCancelled => "MANUAL_CANCELLED",
        _ => throw new InvalidOperationException($"unsupported dispatch status: {(byte)status}")
    };

    internal static DispatchOrderStatus ParseApiStatus(string status) => status.Trim().ToUpperInvariant() switch
    {
        "PENDING_PICK" => DispatchOrderStatus.PendingPick,
        "PICKED" => DispatchOrderStatus.Picked,
        "WEIGHING" => DispatchOrderStatus.Weighing,
        "PENDING_OUTBOUND" => DispatchOrderStatus.PendingOutbound,
        "OUTBOUND" => DispatchOrderStatus.Outbound,
        "SOURCE_CANCELLED" => DispatchOrderStatus.SourceCancelled,
        "MANUAL_CANCELLED" => DispatchOrderStatus.ManualCancelled,
        _ => throw new ArgumentException($"unsupported dispatch status: {status}", nameof(status))
    };

    internal static string HashText(string text) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();

    internal static string TaskSetKey(IReadOnlyCollection<long> sourceTaskIds) =>
        HashText(string.Join(",", sourceTaskIds.OrderBy(t => t)));

    internal static string CombinedVersion(IEnumerable<PackingTaskSourceSnapshot> snapshots) =>
        HashText(string.Join("|", snapshots.OrderBy(t => t.SourceTaskId)
            .Select(t => $"{t.SourceTaskId}:{t.SourceVersion}:{t.IsCancelled}")));

    internal static string SnapshotJson(IEnumerable<PackingTaskSourceSnapshot> snapshots) =>
        JsonSerializer.Serialize(snapshots.OrderBy(t => t.SourceTaskId));
}
