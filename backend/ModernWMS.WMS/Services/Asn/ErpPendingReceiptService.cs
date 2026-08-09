using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ModernWMS.Core.DBContext;
using ModernWMS.Core.DBContext.Entities;
using ModernWMS.Core.JWT;
using ModernWMS.Core.Models;
using ModernWMS.WMS.Entities.ViewModels;
using ModernWMS.WMS.IServices;

namespace ModernWMS.WMS.Services;

/// <summary>
/// ERP-backed pending receipt query for the Shenzhen warehouse.
/// </summary>
public partial class ErpPendingReceiptService : IErpPendingReceiptService
{
    private const long ShenzhenWarehouseId = 320118;
    private const string WaitReceiptStatus = "WAIT_RECEIPT";
    private const string DeliveredTrackingStatus = "DELIVERED";
    private readonly RuoyiDbContext _ruoyiDbContext;

    public ErpPendingReceiptService(RuoyiDbContext ruoyiDbContext)
    {
        _ruoyiDbContext = ruoyiDbContext;
    }

    /// <summary>
    /// Returns one row per ERP logistics shipment and resolves product and tracking snapshots.
    /// </summary>
    public async Task<(List<ErpPendingReceiptViewModel> data, int totals)> PageAsync(
        PageSearch pageSearch,
        bool delivered)
    {
        var supplierName = FindSearchText(pageSearch, "supplier_name");
        var productKeyword = FindSearchText(pageSearch, "product_keyword");

        var query = _ruoyiDbContext.LogisticsInfos
            .AsNoTracking()
            .Where(t => !t.deleted
                && t.lifecycle_status == WaitReceiptStatus
                && t.to_warehouse_id == ShenzhenWarehouseId
                && !_ruoyiDbContext.ReceiptRecords.Any(receipt => receipt.shipment_id == t.id));

        // 中文说明：两个页签只按最新物流签收事实拆分；没有轨迹、空状态、在途及其它状态都留在待到货。
        query = query.Where(shipment => _ruoyiDbContext.Tracks
            .AsNoTracking()
            .Where(track => !track.deleted && track.track_number == shipment.tracking_no)
            .OrderByDescending(track => track.update_time)
            .ThenByDescending(track => track.id)
            .Select(track => track.tracking_status.Trim().ToUpper() == DeliveredTrackingStatus
                || track.actual_delivery_time != null
                || (track.provider_status_code != null
                    && (track.provider_status_code.Trim().ToUpper() == DeliveredTrackingStatus
                        || track.provider_status_code.Trim() == "3"))
                || (track.business_stage != null
                    && track.business_stage.Trim().ToUpper() == DeliveredTrackingStatus)
                || (track.last_event_stage != null
                    && (track.last_event_stage.Trim().ToUpper() == DeliveredTrackingStatus
                        || track.last_event_stage.Trim() == "3"))
                || (!((track.provider_status_name != null
                            && (track.provider_status_name.Contains("未签收")
                                || track.provider_status_name.Contains("未妥投")
                                || track.provider_status_name.Contains("拒签")
                                || track.provider_status_name.Contains("签收失败")
                                || track.provider_status_name.Contains("无法签收")
                                || track.provider_status_name.Contains("待签收")
                                || track.provider_status_name.Contains("等待签收")
                                || track.provider_status_name.Contains("签收异常")))
                        || (track.last_event_description != null
                            && (track.last_event_description.Contains("未签收")
                                || track.last_event_description.Contains("未妥投")
                                || track.last_event_description.Contains("拒签")
                                || track.last_event_description.Contains("签收失败")
                                || track.last_event_description.Contains("无法签收")
                                || track.last_event_description.Contains("待签收")
                                || track.last_event_description.Contains("等待签收")
                                || track.last_event_description.Contains("签收异常"))))
                    && ((track.provider_status_name != null
                            && (track.provider_status_name == "签收"
                                || track.provider_status_name == "本人签收"
                                || track.provider_status_name == "已签收"
                                || track.provider_status_name == "妥投"
                                || track.provider_status_name == "已妥投"
                                || track.provider_status_name.Contains("签收成功")
                                || track.provider_status_name.ToLower().Contains("delivered")
                                || track.provider_status_name.ToLower().Contains("signed for")))
                        || (track.last_event_description != null
                            && (track.last_event_description.Contains("本人签收")
                                || track.last_event_description.Contains("已签收")
                                || track.last_event_description.Contains("签收成功")
                                || track.last_event_description.Contains("妥投")
                                || track.last_event_description.ToLower().Contains("delivered")
                                || track.last_event_description.ToLower().Contains("signed for"))))))
            .FirstOrDefault() == delivered);

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
            return BuildViewModel(shipment, track, products, IsDeliveredTrack(track));
        }).ToList();

        return (result, totals);
    }

    public async Task<ErpPendingReceiptLogisticsViewModel?> GetLogisticsAsync(long shipmentId)
    {
        var shipment = await _ruoyiDbContext.LogisticsInfos
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.id == shipmentId
                && !t.deleted
                && t.lifecycle_status == WaitReceiptStatus
                && t.to_warehouse_id == ShenzhenWarehouseId);
        if (shipment == null)
        {
            return null;
        }

        var trackingNumber = shipment.tracking_no ?? string.Empty;
        var track = await _ruoyiDbContext.Tracks
            .AsNoTracking()
            .Where(t => !t.deleted && t.track_number == trackingNumber)
            .OrderByDescending(t => t.update_time)
            .ThenByDescending(t => t.id)
            .FirstOrDefaultAsync();

        List<ErpPendingReceiptTrackEventViewModel> events = [];
        if (track != null)
        {
            events = await _ruoyiDbContext.TrackEvents
                .AsNoTracking()
                .Where(t => !t.deleted && t.track_id == track.id)
                .OrderByDescending(t => t.event_time)
                .ThenByDescending(t => t.sort)
                .ThenByDescending(t => t.id)
                .Take(200)
                .Select(t => new ErpPendingReceiptTrackEventViewModel
                {
                    id = t.id,
                    event_time = t.event_time,
                    status_name = t.provider_status_name ?? string.Empty,
                    description = t.description ?? string.Empty,
                    location = t.location ?? string.Empty,
                    stage = t.stage ?? string.Empty
                })
                .ToListAsync();
        }

        var delivered = IsDeliveredTrack(track);
        return new ErpPendingReceiptLogisticsViewModel
        {
            shipment_id = shipment.id,
            logistics_name = shipment.carrier_name ?? string.Empty,
            tracking_no = trackingNumber,
            tracking_status = delivered ? DeliveredTrackingStatus : track?.tracking_status ?? "UNKNOWN",
            tracking_status_name = delivered
                ? "已签收"
                : FirstNotEmpty(track?.provider_status_name ?? string.Empty, track?.tracking_status ?? string.Empty, "未知"),
            latest_event_desc = track?.last_event_description ?? string.Empty,
            latest_event_time = track?.last_event_time,
            latest_event_location = track?.last_event_location ?? string.Empty,
            estimated_delivery_time = track?.estimated_delivery_time,
            actual_delivery_time = track?.actual_delivery_time,
            event_list = events
        };
    }

    /// <summary>
    /// Confirms product-level receipt and posts the same business event to ERP and WMS ledgers.
    /// </summary>
    public async Task<(bool flag, string message, long inboundQty)> ConfirmAsync(
        ErpReceiptConfirmInputViewModel input,
        CurrentUser currentUser)
    {
        await using var transaction = await _ruoyiDbContext.Database
            .BeginTransactionAsync(IsolationLevel.Serializable);
        try
        {
            var existingReceipt = await _ruoyiDbContext.ReceiptRecords
                .AsNoTracking()
                .FirstOrDefaultAsync(t => t.shipment_id == input.shipment_id);
            if (existingReceipt != null)
            {
                await transaction.CommitAsync();
                return (true, "该货件已完成签收入库", existingReceipt.inbound_qty);
            }
            var stockRecordCount = await ScalarAsync<long>(
                """
                SELECT COUNT(*) FROM trk_stock_record
                 WHERE biz_type='RECEIPT_IN' AND biz_id=@shipmentId AND deleted=b'0'
                """,
                ("@shipmentId", input.shipment_id));
            if (stockRecordCount > 0)
            {
                return (false, "该货件已生成入库流水，请勿重复提交", 0);
            }

            await LockShipmentAsync(input.shipment_id);
            var shipment = await _ruoyiDbContext.LogisticsInfos
                .FirstOrDefaultAsync(t => t.id == input.shipment_id
                    && !t.deleted
                    && t.lifecycle_status == WaitReceiptStatus
                    && t.to_warehouse_id == ShenzhenWarehouseId);
            if (shipment == null)
            {
                return (false, "未找到可收货的深圳自建仓货件", 0);
            }
            if (shipment.source_version != input.source_version)
            {
                return (false, "货件数据已更新，请刷新列表后重新确认", 0);
            }

            var products = ParseProducts(shipment.product_snapshot_json);
            if (products.Count == 0 || products.Any(t => t.quantity == null || t.quantity < 0))
            {
                return (false, "货件商品快照为空或数量无效，不能签收入库", 0);
            }
            if (products.Select(t => t.source_item_key).Distinct(StringComparer.Ordinal).Count() != products.Count
                || input.items.Count != products.Count
                || input.items.Select(t => t.source_item_key).Distinct(StringComparer.Ordinal).Count() != input.items.Count)
            {
                return (false, "收货商品行与货件快照不一致，请刷新后重试", 0);
            }

            var itemMap = input.items.ToDictionary(t => t.source_item_key, StringComparer.Ordinal);
            foreach (var product in products)
            {
                if (!itemMap.TryGetValue(product.source_item_key, out var item)
                    || item.commodity_id != product.commodity_id
                    || !string.Equals(item.commodity_sku.Trim(), product.sku.Trim(), StringComparison.Ordinal)
                    || item.shipment_qty != product.quantity)
                {
                    return (false, "收货商品行与货件快照不一致，请刷新后重试", 0);
                }
                if (item.actual_receipt_qty < 0 || item.actual_receipt_qty > item.shipment_qty)
                {
                    return (false, $"商品 {product.sku} 的实际收货数量必须在 0 到发货数量之间", 0);
                }
                if (item.loss_qty < 0 || item.loss_qty > item.actual_receipt_qty)
                {
                    return (false, $"商品 {product.sku} 的损耗数量必须在 0 到实际收货数量之间", 0);
                }
            }

            var snapshotShipmentQty = products.Sum(t => t.quantity ?? 0);
            if (snapshotShipmentQty != shipment.shipment_qty)
            {
                return (false, "货件商品数量合计与货件表头不一致，请先修正 ERP 数据", 0);
            }

            var actualReceiptQty = input.items.Sum(t => t.actual_receipt_qty);
            var lossQty = input.items.Sum(t => t.loss_qty);
            var inboundQty = checked(actualReceiptQty - lossQty);

            var freightPaymentStatus = input.receipt_freight_payment_status?.Trim().ToUpperInvariant() ?? string.Empty;
            if (freightPaymentStatus is not ("NO_PAY" or "PAY"))
            {
                return (false, "运费支付状态无效", 0);
            }
            if (freightPaymentStatus == "PAY"
                && (input.receipt_freight_amount == null || input.receipt_freight_amount <= 0))
            {
                return (false, "支付运费时必须填写大于 0 的金额", 0);
            }
            if (lossQty > 0 && string.IsNullOrWhiteSpace(input.loss_reason))
            {
                return (false, "存在损耗时必须填写损耗原因", 0);
            }
            if ((input.loss_reason?.Length ?? 0) > 500 || (input.receipt_remark?.Length ?? 0) > 500)
            {
                return (false, "损耗原因和收货备注不能超过 500 个字符", 0);
            }
            if (!ValidateImages(input.receipt_freight_files, input.shipment_id, "freight")
                || !ValidateImages(input.loss_files, input.shipment_id, "loss")
                || !ValidateImages(input.receipt_files, input.shipment_id, "receipt"))
            {
                return (false, "附件数量或 OSS 路径无效", 0);
            }

            var now = DateTime.Now;
            var entity = new ErpReceiptRecordEntity
            {
                shipment_id = input.shipment_id,
                source_version = input.source_version,
                actual_receipt_qty = actualReceiptQty,
                loss_qty = lossQty,
                inbound_qty = inboundQty,
                receipt_freight_payment_status = freightPaymentStatus,
                receipt_freight_amount = freightPaymentStatus == "PAY" ? input.receipt_freight_amount : null,
                receipt_freight_files_json = SerializeImages(
                    freightPaymentStatus == "PAY" ? input.receipt_freight_files : []),
                receipt_files_json = SerializeImages(input.receipt_files),
                loss_reason = lossQty > 0 ? input.loss_reason.Trim() : string.Empty,
                loss_files_json = SerializeImages(lossQty > 0 ? input.loss_files : []),
                receipt_remark = input.receipt_remark?.Trim() ?? string.Empty,
                creator = Truncate(currentUser.user_name, 64),
                create_time = now,
                last_update_time = now,
                tenant_id = currentUser.tenant_id
            };
            _ruoyiDbContext.ReceiptRecords.Add(entity);
            await _ruoyiDbContext.SaveChangesAsync();

            await ApplyInventoryReceiptAsync(
                shipment,
                products,
                input,
                entity.id,
                actualReceiptQty,
                lossQty,
                inboundQty,
                freightPaymentStatus,
                currentUser,
                now);
            await transaction.CommitAsync();
            return (true, "收货确认成功", inboundQty);
        }
        catch (InvalidOperationException ex)
        {
            return (false, ex.Message, 0);
        }
    }

    private static bool ValidateImages(
        List<OssFileUploadViewModel>? images,
        long shipmentId,
        string category)
    {
        images ??= [];
        if (images.Count > 9)
        {
            return false;
        }

        var requiredPrefix = $"modernwms/erp-receipt/{category}/{shipmentId}/";
        return images.All(image => !string.IsNullOrWhiteSpace(image.path)
            && image.path.StartsWith(requiredPrefix, StringComparison.Ordinal));
    }

    private static string SerializeImages(List<OssFileUploadViewModel>? images)
    {
        return JsonSerializer.Serialize(images ?? []);
    }

    private static string Truncate(string? value, int maxLength)
    {
        var normalized = value?.Trim() ?? string.Empty;
        return normalized.Length <= maxLength ? normalized : normalized[..maxLength];
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
        List<ErpPendingReceiptProductViewModel> products,
        bool delivered)
    {
        return new ErpPendingReceiptViewModel
        {
            id = shipment.id,
            source_type = shipment.source_type,
            source_stock_move_no = shipment.source_stock_move_no ?? string.Empty,
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
            source_freight_payment_type = shipment.source_freight_payment_type ?? string.Empty,
            provider_code = shipment.track_provider_code ?? string.Empty,
            logistics_code = shipment.carrier_code ?? string.Empty,
            logistics_name = shipment.carrier_name ?? string.Empty,
            tracking_no = shipment.tracking_no ?? string.Empty,
            lifecycle_status = shipment.lifecycle_status,
            tracking_status = delivered ? DeliveredTrackingStatus : track?.tracking_status ?? "UNKNOWN",
            tracking_status_name = delivered
                ? "已签收"
                : FirstNotEmpty(track?.provider_status_name ?? string.Empty, track?.tracking_status ?? string.Empty, "未知"),
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

            return document.RootElement.EnumerateArray().Select((item, index) =>
            {
                var taskItemId = GetInt64(item, "taskItemId");
                var allocationId = GetInt64(item, "allocationId");
                var commodityId = GetInt64(item, "commodityId");
                var sku = GetString(item, "commoditySku");
                return new ErpPendingReceiptProductViewModel
                {
                    source_item_key = BuildSourceItemKey(taskItemId, allocationId, commodityId, sku, index),
                    task_item_id = taskItemId,
                    allocation_id = allocationId,
                    commodity_id = commodityId,
                    order_user_id = GetInt64(item, "userId"),
                    dept_id = GetInt64(item, "deptId"),
                    sku = sku,
                    product_name = GetString(item, "commodityName"),
                    quantity = GetInt64(item, "shipmentQty") ?? GetInt64(item, "allocationQty"),
                    usage_type = GetString(item, "usageType"),
                    order_user_name = GetString(item, "userName"),
                    dept_name = FirstNotEmpty(GetString(item, "deptName"), GetString(item, "groupName"))
                };
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

    private static string BuildSourceItemKey(
        long? taskItemId,
        long? allocationId,
        long? commodityId,
        string sku,
        int index)
    {
        var skuFallback = commodityId == null ? Truncate(sku, 64) : string.Empty;
        return $"{taskItemId ?? 0}:{allocationId ?? 0}:{commodityId ?? 0}:{skuFallback}:{index}";
    }

    private static string FirstNotEmpty(params string[] values)
    {
        return values.FirstOrDefault(t => !string.IsNullOrWhiteSpace(t)) ?? string.Empty;
    }

    private static bool IsDeliveredTrack(ErpTrackEntity? track)
    {
        if (track == null)
        {
            return false;
        }
        if (string.Equals(track.tracking_status?.Trim(), DeliveredTrackingStatus, StringComparison.OrdinalIgnoreCase)
            || track.actual_delivery_time != null
            || string.Equals(track.provider_status_code?.Trim(), DeliveredTrackingStatus, StringComparison.OrdinalIgnoreCase)
            || string.Equals(track.provider_status_code?.Trim(), "3", StringComparison.OrdinalIgnoreCase)
            || string.Equals(track.business_stage?.Trim(), DeliveredTrackingStatus, StringComparison.OrdinalIgnoreCase)
            || string.Equals(track.last_event_stage?.Trim(), DeliveredTrackingStatus, StringComparison.OrdinalIgnoreCase)
            || string.Equals(track.last_event_stage?.Trim(), "3", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
        var evidence = $"{track.provider_status_name} {track.last_event_description}";
        var hasNegativeEvidence = new[] { "未签收", "未妥投", "拒签", "签收失败", "无法签收", "待签收", "等待签收", "签收异常" }
            .Any(evidence.Contains);
        if (hasNegativeEvidence)
        {
            return false;
        }
        var providerStatusName = track.provider_status_name?.Trim();
        return string.Equals(providerStatusName, "签收", StringComparison.OrdinalIgnoreCase)
            || string.Equals(providerStatusName, "本人签收", StringComparison.OrdinalIgnoreCase)
            || string.Equals(providerStatusName, "已签收", StringComparison.OrdinalIgnoreCase)
            || string.Equals(providerStatusName, "妥投", StringComparison.OrdinalIgnoreCase)
            || string.Equals(providerStatusName, "已妥投", StringComparison.OrdinalIgnoreCase)
            || evidence.Contains("本人签收", StringComparison.OrdinalIgnoreCase)
            || evidence.Contains("已签收", StringComparison.OrdinalIgnoreCase)
            || evidence.Contains("签收成功", StringComparison.OrdinalIgnoreCase)
            || evidence.Contains("妥投", StringComparison.OrdinalIgnoreCase)
            || evidence.Contains("delivered", StringComparison.OrdinalIgnoreCase)
            || evidence.Contains("signed for", StringComparison.OrdinalIgnoreCase);
    }
}
