using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dapper;
using ModernWMS.Core.Database;
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
    private readonly IMySqlConnectionFactory _connectionFactory;
    private readonly IPackingTaskSourceReader _sourceReader;
    private readonly IWarehouseAccessService _warehouseAccessService;
    private readonly IDispatchSignNotificationClient? _dispatchSignNotificationClient;

    public DispatchWorkflowService(
        IMySqlConnectionFactory connectionFactory,
        IPackingTaskSourceReader sourceReader,
        IWarehouseAccessService warehouseAccessService,
        IDispatchSignNotificationClient? dispatchSignNotificationClient = null)
    {
        _connectionFactory = connectionFactory;
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

    internal async Task<DispatchOrderEntity> FindOrderAsync(int orderId, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<DispatchOrderEntity>(new CommandDefinition(
            "SELECT * FROM `wms_dispatch_order` WHERE `id`=@orderId LIMIT 1;",
            new { orderId }, cancellationToken: cancellationToken))
            ?? throw new KeyNotFoundException($"dispatch order not found: {orderId}");
    }

    internal async Task<DispatchOrderDetailViewModel> LoadDetailAsync(
        int orderId,
        CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        var order = await LoadOrderAggregateAsync(connection, orderId, cancellationToken);

        return ToDetail(order);
    }

    internal static DispatchOrderDetailViewModel ToDetail(DispatchOrderEntity order)
    {
        var anomaly = LatestOutboundAnomaly(order);
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
                        main_image = SourceMainImage(i.source_snapshot),
                        fn_sku = i.fn_sku,
                        msku = i.msku,
                        task_qty = i.source_quantity_shipped,
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
            pending_source_version = order.pending_source_version,
            source_change_snapshot = order.source_change_snapshot,
            accepted_source_version = order.accepted_source_version,
            signed_qty = order.signed_qty,
            damaged_qty = order.damaged_qty,
            signed_at = order.signed_at,
            signed_by_name = order.signed_by_name,
            notification_status = order.notification_status.ToString().ToUpperInvariant(),
            notification_last_error = order.notification_last_error,
            outbound_source_anomaly = anomaly != null,
            outbound_source_anomaly_snapshot = anomaly?.diff_snapshot ?? string.Empty,
            row_version = order.row_version
        };
    }

    private static string SourceMainImage(string sourceSnapshot)
    {
        if (string.IsNullOrWhiteSpace(sourceSnapshot)) return string.Empty;
        try
        {
            using var document = JsonDocument.Parse(sourceSnapshot);
            return document.RootElement.TryGetProperty("mainImage", out var value)
                && value.ValueKind == JsonValueKind.String
                ? value.GetString()?.Trim() ?? string.Empty
                : string.Empty;
        }
        catch (JsonException)
        {
            return string.Empty;
        }
    }

    internal static DispatchSourceChangeEventEntity? LatestOutboundAnomaly(DispatchOrderEntity order) =>
        order.source_change_events
            .Where(sourceEvent => sourceEvent.decision == DispatchSourceChangeDecision.OutboundAnomaly)
            .OrderByDescending(sourceEvent => sourceEvent.decision_time)
            .ThenByDescending(sourceEvent => sourceEvent.id)
            .FirstOrDefault();

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

    private async Task<IReadOnlyDictionary<long, int>> ResolveCurrentSkuMappingsAsync(
        IReadOnlyCollection<PackingTaskSourceSnapshot> snapshots,
        CancellationToken cancellationToken)
    {
        var items = snapshots.Where(t => !t.IsCancelled).SelectMany(t => t.Items).ToList();
        if (items.Any(t => t.CommodityId is null or <= 0))
        {
            throw DispatchWorkflowCommandException.SkuMappingMissing(
                "packing task item has no ERP commodity identity");
        }

        var commodityIds = items.Select(t => t.CommodityId!.Value).Distinct().ToArray();
        if (commodityIds.Length == 0)
        {
            return new Dictionary<long, int>();
        }

        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        var mappings = (await connection.QueryAsync<CommodityMapRow>(new CommandDefinition("""
            SELECT `erp_commodity_id`,`wms_sku_id` FROM `wms_erp_commodity_map`
            WHERE `erp_commodity_id` IN @commodityIds;
            """, new { commodityIds }, cancellationToken: cancellationToken))).AsList();
        var resolved = new Dictionary<long, int>();
        foreach (var commodityId in commodityIds)
        {
            var skuIds = mappings.Where(t => t.erp_commodity_id == commodityId && t.wms_sku_id > 0)
                .Select(t => t.wms_sku_id)
                .Distinct()
                .ToArray();
            if (skuIds.Length == 0)
            {
                throw DispatchWorkflowCommandException.SkuMappingMissing(
                    $"ERP commodity {commodityId} has no WMS SKU mapping");
            }

            if (skuIds.Length > 1)
            {
                throw DispatchWorkflowCommandException.SkuMappingConflict(
                    $"ERP commodity {commodityId} maps to multiple WMS SKUs");
            }

            resolved.Add(commodityId, skuIds[0]);
        }

        return resolved;
    }

    private static int MappedSkuId(
        PackingTaskSourceItem item,
        IReadOnlyDictionary<long, int> mappings) =>
        item.CommodityId is long commodityId && mappings.TryGetValue(commodityId, out var skuId)
            ? skuId
            : throw DispatchWorkflowCommandException.SkuMappingMissing(
                "packing task item has no WMS SKU mapping");

    internal static async Task<DispatchOrderEntity> LoadOrderAggregateAsync(
        System.Data.IDbConnection connection,
        int orderId,
        CancellationToken cancellationToken)
    {
        using var result = await connection.QueryMultipleAsync(new CommandDefinition("""
            SELECT * FROM `wms_dispatch_order` WHERE `id`=@orderId LIMIT 1;
            SELECT * FROM `wms_dispatch_packing_task` WHERE `dispatch_order_id`=@orderId AND `is_active`=1;
            SELECT i.* FROM `wms_dispatch_packing_task_item` i
            JOIN `wms_dispatch_packing_task` t ON t.`id`=i.`packing_task_id`
            WHERE t.`dispatch_order_id`=@orderId AND t.`is_active`=1 AND i.`is_active`=1;
            SELECT * FROM `wms_dispatch_source_change_event`
            WHERE `dispatch_order_id`=@orderId AND `decision`=@anomaly;
            """, new { orderId, anomaly = DispatchSourceChangeDecision.OutboundAnomaly }, cancellationToken: cancellationToken));
        var order = await result.ReadSingleOrDefaultAsync<DispatchOrderEntity>()
            ?? throw new KeyNotFoundException($"dispatch order not found: {orderId}");
        order.packing_tasks = (await result.ReadAsync<DispatchPackingTaskEntity>()).AsList();
        var items = (await result.ReadAsync<DispatchPackingTaskItemEntity>()).AsList();
        foreach (var task in order.packing_tasks)
            task.items = items.Where(x => x.packing_task_id == task.id).ToList();
        order.source_change_events = (await result.ReadAsync<DispatchSourceChangeEventEntity>()).AsList();
        return order;
    }

    private sealed class CommodityMapRow
    {
        public long erp_commodity_id { get; set; }
        public int wms_sku_id { get; set; }
    }
}
