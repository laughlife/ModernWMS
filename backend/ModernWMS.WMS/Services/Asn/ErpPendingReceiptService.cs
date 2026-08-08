using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ModernWMS.Core.DBContext;
using ModernWMS.Core.DBContext.Entities;
using ModernWMS.Core.Models;
using ModernWMS.WMS.Entities.ViewModels;
using ModernWMS.WMS.IServices;

namespace ModernWMS.WMS.Services;

/// <summary>
/// ERP-backed pending receipt query for the Shenzhen warehouse.
/// </summary>
public class ErpPendingReceiptService : IErpPendingReceiptService
{
    private const long ShenzhenWarehouseId = 320118;
    private const string WaitReceiptStatus = "WAIT_RECEIPT";
    private readonly RuoyiDbContext _ruoyiDbContext;

    public ErpPendingReceiptService(RuoyiDbContext ruoyiDbContext)
    {
        _ruoyiDbContext = ruoyiDbContext;
    }

    /// <summary>
    /// Returns one row per ERP logistics shipment and resolves product and tracking snapshots.
    /// </summary>
    public async Task<(List<ErpPendingReceiptViewModel> data, int totals)> PageAsync(PageSearch pageSearch)
    {
        var supplierName = FindSearchText(pageSearch, "supplier_name");
        var productKeyword = FindSearchText(pageSearch, "product_keyword");

        var query = _ruoyiDbContext.LogisticsInfos
            .AsNoTracking()
            .Where(t => !t.deleted
                && t.lifecycle_status == WaitReceiptStatus
                && t.to_warehouse_id == ShenzhenWarehouseId);

        if (!string.IsNullOrWhiteSpace(supplierName))
        {
            query = query.Where(t => t.supplier_name != null && t.supplier_name.Contains(supplierName));
        }

        if (!string.IsNullOrWhiteSpace(productKeyword))
        {
            query = query.Where(t => t.product_snapshot_json.Contains(productKeyword)
                || (t.purchase_no != null && t.purchase_no.Contains(productKeyword))
                || (t.tracking_no != null && t.tracking_no.Contains(productKeyword)));
        }

        var totals = await query.CountAsync();
        var pageIndex = Math.Max(pageSearch.pageIndex, 1);
        var pageSize = Math.Clamp(pageSearch.pageSize, 1, 200);
        var shipments = await query
            .OrderByDescending(t => t.shipment_time)
            .ThenByDescending(t => t.id)
            .Skip((pageIndex - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        var trackingNumbers = shipments
            .Select(t => t.tracking_no)
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Select(t => t!)
            .Distinct()
            .ToList();

        var trackMap = new Dictionary<string, ErpTrackEntity>();
        if (trackingNumbers.Count > 0)
        {
            var tracks = await _ruoyiDbContext.Tracks
                .AsNoTracking()
                .Where(t => !t.deleted && trackingNumbers.Contains(t.track_number))
                .OrderByDescending(t => t.update_time)
                .ThenByDescending(t => t.id)
                .ToListAsync();
            trackMap = tracks
                .GroupBy(t => t.track_number)
                .ToDictionary(t => t.Key, t => t.First());
        }

        var result = shipments.Select(shipment =>
        {
            var products = ParseProducts(shipment.product_snapshot_json);
            trackMap.TryGetValue(shipment.tracking_no ?? string.Empty, out var track);
            return BuildViewModel(shipment, track, products);
        }).ToList();

        return (result, totals);
    }

    private static string FindSearchText(PageSearch pageSearch, string name)
    {
        return pageSearch.searchObjects
            .FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase))
            ?.Text
            ?.Trim() ?? string.Empty;
    }

    private static ErpPendingReceiptViewModel BuildViewModel(
        ErpLogisticsInfoEntity shipment,
        ErpTrackEntity? track,
        List<ErpPendingReceiptProductViewModel> products)
    {
        return new ErpPendingReceiptViewModel
        {
            id = shipment.id,
            source_type = shipment.source_type,
            purchase_no = shipment.purchase_no ?? string.Empty,
            supplier_name = shipment.supplier_name ?? string.Empty,
            order_user_text = shipment.order_user_text ?? string.Empty,
            shipment_batch_no = shipment.shipment_batch_no,
            shipment_type = shipment.shipment_type ?? string.Empty,
            shipment_qty = shipment.shipment_qty ?? 0,
            shipment_time = shipment.shipment_time,
            warehouse_id = shipment.to_warehouse_id ?? ShenzhenWarehouseId,
            warehouse_name = shipment.to_warehouse_name ?? string.Empty,
            freight_forwarder_name = shipment.freight_forwarder_name ?? string.Empty,
            provider_code = shipment.track_provider_code ?? string.Empty,
            logistics_code = shipment.carrier_code ?? string.Empty,
            logistics_name = shipment.carrier_name ?? string.Empty,
            tracking_no = shipment.tracking_no ?? string.Empty,
            lifecycle_status = shipment.lifecycle_status,
            tracking_status = track?.tracking_status ?? "UNKNOWN",
            tracking_status_name = FirstNotEmpty(track?.provider_status_name ?? string.Empty, track?.tracking_status ?? string.Empty, "未知"),
            latest_event_desc = track?.last_event_description ?? string.Empty,
            latest_event_time = track?.last_event_time,
            latest_event_location = track?.last_event_location ?? string.Empty,
            estimated_delivery_time = track?.estimated_delivery_time,
            actual_delivery_time = track?.actual_delivery_time,
            source_version = shipment.source_version,
            product_summary = BuildProductSummary(products),
            product_count = products.Count,
            product_list = products
        };
    }

    private static string BuildProductSummary(List<ErpPendingReceiptProductViewModel> products)
    {
        if (products.Count == 0)
        {
            return "未提供商品快照";
        }

        var summary = string.Join("；", products.Take(2).Select(t =>
        {
            var title = string.Join(" ", new[] { t.sku, t.product_name }.Where(v => !string.IsNullOrWhiteSpace(v)));
            return $"{title} ×{t.quantity ?? 0}";
        }));
        return products.Count > 2 ? $"{summary}；等{products.Count}种" : summary;
    }

    private static List<ErpPendingReceiptProductViewModel> ParseProducts(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            return document.RootElement.EnumerateArray().Select(item => new ErpPendingReceiptProductViewModel
            {
                task_item_id = GetInt64(item, "taskItemId"),
                allocation_id = GetInt64(item, "allocationId"),
                commodity_id = GetInt64(item, "commodityId"),
                sku = GetString(item, "commoditySku"),
                product_name = GetString(item, "commodityName"),
                quantity = GetInt64(item, "shipmentQty") ?? GetInt64(item, "allocationQty"),
                usage_type = GetString(item, "usageType"),
                order_user_name = GetString(item, "userName"),
                dept_name = FirstNotEmpty(GetString(item, "deptName"), GetString(item, "groupName"))
            }).ToList();
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static string GetString(JsonElement item, string propertyName)
    {
        if (!item.TryGetProperty(propertyName, out var value))
        {
            return string.Empty;
        }

        return value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : value.ToString();
    }

    private static long? GetInt64(JsonElement item, string propertyName)
    {
        if (!item.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number))
        {
            return number;
        }

        return long.TryParse(value.ToString(), out number) ? number : null;
    }

    private static string FirstNotEmpty(params string[] values)
    {
        return values.FirstOrDefault(t => !string.IsNullOrWhiteSpace(t)) ?? string.Empty;
    }
}
