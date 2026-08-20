using System.Data;
using System.Text.Json;
using Dapper;
using ModernWMS.Core.Database;
using ModernWMS.Core.DBContext.Entities;
using ModernWMS.Core.JWT;
using ModernWMS.Core.Models;
using ModernWMS.WMS.Entities.ViewModels;
using ModernWMS.WMS.IServices;

namespace ModernWMS.WMS.Services;

/// <summary>
/// ERP-backed pending receipt query scoped to the warehouse selected by the current user.
/// </summary>
public partial class ErpPendingReceiptService : IErpPendingReceiptService
{
    private const string WaitReceiptStatus = "WAIT_RECEIPT";
    private const string DeliveredTrackingStatus = "DELIVERED";
    private readonly IMySqlConnectionFactory _connectionFactory;
    private readonly IWarehouseAccessService _warehouseAccessService;
    private MySqlConnector.MySqlConnection? _activeConnection;
    private MySqlConnector.MySqlTransaction? _activeTransaction;

    public ErpPendingReceiptService(IMySqlConnectionFactory connectionFactory, IWarehouseAccessService warehouseAccessService)
    {
        _connectionFactory = connectionFactory;
        _warehouseAccessService = warehouseAccessService;
    }

    /// <summary>
    /// Returns one row per ERP logistics shipment and resolves product and tracking snapshots.
    /// </summary>
    public async Task<(List<ErpPendingReceiptViewModel> data, int totals)> PageAsync(
        PageSearch pageSearch,
        ErpPendingReceiptListKind kind,
        CurrentUser currentUser)
    {
        var supplierName = FindSearchText(pageSearch, "supplier_name");
        var productKeyword = FindSearchText(pageSearch, "product_keyword");

        var warehouseId = await ResolveWarehouseAsync(pageSearch, currentUser);
        if (warehouseId == null)
        {
            return ([], 0);
        }

        var clauses = new List<string>
        {
            "l.`deleted`=b'0'", "l.`lifecycle_status`=@status", "l.`to_warehouse_id`=@warehouseId",
            "NOT EXISTS(SELECT 1 FROM `wms_erp_receipt` r WHERE r.`shipment_id`=l.`id`)"
        };
        if (kind == ErpPendingReceiptListKind.ToShip) clauses.Add("l.`shipment_time` IS NULL");
        else clauses.Add("l.`shipment_time` IS NOT NULL");
        if (!string.IsNullOrWhiteSpace(supplierName)) clauses.Add("l.`supplier_name` LIKE @supplierName");
        if (!string.IsNullOrWhiteSpace(productKeyword))
            clauses.Add("(l.`product_snapshot_json` LIKE @productKeyword OR l.`purchase_no` LIKE @productKeyword OR l.`tracking_no` LIKE @productKeyword)");
        await using var connection = await _connectionFactory.OpenConnectionAsync();
        var shipments = (await connection.QueryAsync<ErpLogisticsInfoEntity>($"""
            SELECT l.* FROM `trk_logistics_info` l WHERE {string.Join(" AND ", clauses)}
            ORDER BY l.`shipment_time` DESC, l.`id` DESC;
            """, new { status=WaitReceiptStatus, warehouseId=warehouseId.Value,
                supplierName=$"%{supplierName}%", productKeyword=$"%{productKeyword}%" })).AsList();

        var trackingNumbers = shipments.Select(t => t.tracking_no).Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => t!).Distinct().ToList();
        var trackMap = trackingNumbers.Count == 0
            ? new Dictionary<string, ErpTrackEntity>()
            : (await connection.QueryAsync<ErpTrackEntity>("""
                SELECT t.* FROM `trk_track` t WHERE t.`deleted`=b'0' AND t.`track_number` IN @trackingNumbers
                ORDER BY t.`update_time` DESC, t.`id` DESC;
                """, new { trackingNumbers })).GroupBy(t => t.track_number).ToDictionary(t => t.Key, t => t.First());
        if (kind != ErpPendingReceiptListKind.ToShip)
        {
            shipments = shipments.Where(s =>
            {
                trackMap.TryGetValue(s.tracking_no ?? string.Empty, out var track);
                return IsDeliveredTrack(track) == (kind == ErpPendingReceiptListKind.Arrived);
            }).ToList();
        }
        var totals = shipments.Count;
        var pageIndex = Math.Max(pageSearch.pageIndex, 1);
        var pageSize = Math.Clamp(pageSearch.pageSize, 1, 200);
        shipments = shipments.Skip((pageIndex - 1) * pageSize).Take(pageSize).ToList();

        var wmsWarehouseId = await ScalarAsync<int?>(
            """
            SELECT id FROM wms_warehouse
             WHERE erp_warehouse_id=@erpId AND tenant_id=@tenantId AND is_valid=1
             LIMIT 1
            """,
            ("@erpId", warehouseId.Value), ("@tenantId", currentUser.tenant_id));

        var result = new List<ErpPendingReceiptViewModel>(shipments.Count);
        foreach (var shipment in shipments)
        {
            var products = ParseProducts(shipment.product_snapshot_json);
            if (wmsWarehouseId != null)
            {
                await FillDefaultReceiptAllocationsAsync(products, wmsWarehouseId.Value, currentUser);
            }
            trackMap.TryGetValue(shipment.tracking_no ?? string.Empty, out var track);
            result.Add(BuildViewModel(shipment, track, products, IsDeliveredTrack(track), wmsWarehouseId ?? 0));
        }

        return (result, totals);
    }

    private async Task<long?> ResolveWarehouseAsync(PageSearch pageSearch, CurrentUser currentUser)
    {
        var warehouseText = FindSearchText(pageSearch, "warehouse_id");
        long? warehouseId = long.TryParse(warehouseText, out var parsed) && parsed > 0 ? parsed : null;
        if (warehouseId == null)
        {
            return (await _warehouseAccessService.GetAllowedAsync(currentUser)).default_warehouse_id;
        }

        await _warehouseAccessService.EnsureAllowedAsync(warehouseId.Value, currentUser);
        return warehouseId;
    }

    public async Task<ErpPendingReceiptLogisticsViewModel?> GetLogisticsAsync(long shipmentId)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync();
        var shipment = await connection.QuerySingleOrDefaultAsync<ErpLogisticsInfoEntity>("""
            SELECT * FROM `trk_logistics_info` WHERE `id`=@shipmentId AND `deleted`=b'0'
              AND `lifecycle_status`=@status AND `to_warehouse_id` IS NOT NULL LIMIT 1;
            """, new { shipmentId, status=WaitReceiptStatus });
        if (shipment == null)
        {
            return null;
        }

        var trackingNumber = shipment.tracking_no ?? string.Empty;
        var track = await connection.QuerySingleOrDefaultAsync<ErpTrackEntity>("""
            SELECT * FROM `trk_track` WHERE `deleted`=b'0' AND `track_number`=@trackingNumber
            ORDER BY `update_time` DESC, `id` DESC LIMIT 1;
            """, new { trackingNumber });

        List<ErpPendingReceiptTrackEventViewModel> events = [];
        if (track != null)
        {
            events = (await connection.QueryAsync<ErpPendingReceiptTrackEventViewModel>("""
                SELECT `id`,`event_time`,COALESCE(`provider_status_name`,'') AS `status_name`,
                       COALESCE(`description`,'') AS `description`,COALESCE(`location`,'') AS `location`,COALESCE(`stage`,'') AS `stage`
                FROM `trk_track_event` WHERE `deleted`=b'0' AND `track_id`=@trackId
                ORDER BY `event_time` DESC, `sort` DESC, `id` DESC LIMIT 200;
                """, new { trackId=track.id })).AsList();
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
    /// Confirms product-level receipt and atomically posts the ERP balance plus WMS location allocation.
    /// </summary>
    public async Task<(bool flag, string message, long inboundQty)> ConfirmAsync(
        ErpReceiptConfirmInputViewModel input,
        CurrentUser currentUser)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.Serializable);
        _activeConnection = connection;
        _activeTransaction = transaction;
        try
        {
            var existingReceipt = await connection.QuerySingleOrDefaultAsync<ErpReceiptRecordEntity>("""
                SELECT * FROM `wms_erp_receipt` WHERE `shipment_id`=@shipmentId LIMIT 1;
                """, new { shipmentId=input.shipment_id }, transaction);
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
            var shipment = await connection.QuerySingleOrDefaultAsync<ErpLogisticsInfoEntity>("""
                SELECT * FROM `trk_logistics_info` WHERE `id`=@shipmentId AND `deleted`=b'0'
                  AND `lifecycle_status`=@status AND `to_warehouse_id` IS NOT NULL LIMIT 1 FOR UPDATE;
                """, new { shipmentId=input.shipment_id, status=WaitReceiptStatus }, transaction);
            if (shipment == null)
            {
                return (false, "未找到可收货的货件", 0);
            }
            var access = await _warehouseAccessService.GetAllowedAsync(currentUser);
            if (!access.warehouses.Any(t => t.id == shipment.to_warehouse_id))
            {
                return (false, "无权操作该仓库的货件", 0);
            }
            if (shipment.source_version != input.source_version)
            {
                return (false, "货件数据已更新，请刷新列表后重新确认", 0);
            }

            await EnsureCanonicalInventoryWriteEnabledAsync(
                shipment.to_warehouse_id!.Value,
                currentUser.tenant_id);
            if (string.Equals(shipment.source_type, "STOCK_DISPATCH", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "调度收货尚未完成来源库存库位分配迁移，当前禁止签收入库，避免 ERP 库存与库位分配失衡");
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
            entity.id = await connection.ExecuteScalarAsync<int>("""
                INSERT INTO `wms_erp_receipt`
                    (`shipment_id`,`source_version`,`actual_receipt_qty`,`loss_qty`,`inbound_qty`,
                     `receipt_freight_payment_status`,`receipt_freight_amount`,`receipt_freight_files_json`,
                     `receipt_files_json`,`loss_reason`,`loss_files_json`,`receipt_remark`,`creator`,
                     `create_time`,`last_update_time`,`tenant_id`)
                VALUES (@shipment_id,@source_version,@actual_receipt_qty,@loss_qty,@inbound_qty,
                     @receipt_freight_payment_status,@receipt_freight_amount,@receipt_freight_files_json,
                     @receipt_files_json,@loss_reason,@loss_files_json,@receipt_remark,@creator,
                     @create_time,@last_update_time,@tenant_id); SELECT LAST_INSERT_ID();
                """, entity, transaction);

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
            await transaction.RollbackAsync();
            return (false, ex.Message, 0);
        }
        finally
        {
            _activeTransaction = null;
            _activeConnection = null;
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
        bool delivered,
        int wmsWarehouseId)
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
            warehouse_id = shipment.to_warehouse_id ?? 0,
            warehouse_name = shipment.to_warehouse_name ?? string.Empty,
            wms_warehouse_id = wmsWarehouseId,
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
                    main_image = GetString(item, "mainImage"),
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
