using System.Globalization;
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
/// Builds the FBA shipment list from ERP stock-move preparations and FBA snapshots.
/// </summary>
public class FbaShipmentService : IFbaShipmentService
{
    private const long ShenzhenWarehouseId = 320118;
    private const string FbaTransferType = "OVERSEA_FBA_SHIPMENT";
    private const string WaitShipmentStatus = "WAIT_SHIPMENT";

    private readonly IMySqlConnectionFactory _connectionFactory;
    private readonly IDispatchlistService _dispatchlistService;

    public FbaShipmentService(
        IMySqlConnectionFactory connectionFactory,
        IDispatchlistService dispatchlistService)
    {
        _connectionFactory = connectionFactory;
        _dispatchlistService = dispatchlistService;
    }

    public async Task<(List<FbaShipmentViewModel> data, int totals)> PageAsync(PageSearch pageSearch, CurrentUser currentUser)
    {
        var keyword = FindSearchText(pageSearch, "keyword");
        var deptName = FindSearchText(pageSearch, "dept_name");
        var orderUserName = FindSearchText(pageSearch, "order_user_name");

        var pageIndex = Math.Max(pageSearch.pageIndex, 1);
        var pageSize = Math.Clamp(pageSearch.pageSize, 1, 200);
        var where = new List<string>
        {
            "move.deleted = 0",
            "move.transfer_type = @transferType",
            "move.status = @status",
            "move.shipment_status = @shipmentStatus",
            "move.from_warehouse_id = @warehouseId",
            "NOT EXISTS (SELECT 1 FROM wms_dispatchlist detail WHERE detail.tenant_id = @tenantId AND detail.dispatch_no = move.no)"
        };
        var parameters = new DynamicParameters(new
        {
            transferType = FbaTransferType,
            status = WaitShipmentStatus,
            shipmentStatus = WaitShipmentStatus,
            warehouseId = ShenzhenWarehouseId,
            tenantId = currentUser.tenant_id,
            offset = (pageIndex - 1) * pageSize,
            pageSize
        });
        if (!string.IsNullOrWhiteSpace(keyword))
        {
            where.Add("""(move.no LIKE @keyword OR move.remark LIKE @keyword OR EXISTS (SELECT 1 FROM trk_stock_move_item item WHERE item.deleted = 0 AND item.stock_move_id = move.id AND (item.product_snapshot_json LIKE @keyword OR item.commodity_sku LIKE @keyword OR item.commodity_name LIKE @keyword)))""");
            parameters.Add("keyword", $"%{keyword}%");
        }
        if (!string.IsNullOrWhiteSpace(deptName))
        {
            where.Add("move.dept_name LIKE @deptName");
            parameters.Add("deptName", $"%{deptName}%");
        }
        if (!string.IsNullOrWhiteSpace(orderUserName))
        {
            where.Add("move.order_user_name LIKE @orderUserName");
            parameters.Add("orderUserName", $"%{orderUserName}%");
        }

        var whereSql = string.Join(" AND ", where);
        await using var connection = await _connectionFactory.OpenConnectionAsync();
        using var result = await connection.QueryMultipleAsync($"""
            SELECT COUNT(*) FROM trk_stock_move move WHERE {whereSql};
            SELECT move.* FROM trk_stock_move move
            WHERE {whereSql}
            ORDER BY move.create_time DESC, move.id DESC
            LIMIT @pageSize OFFSET @offset;
            """, parameters);
        var totals = await result.ReadSingleAsync<int>();
        var moves = (await result.ReadAsync<ErpStockMoveEntity>()).AsList();

        if (moves.Count == 0)
        {
            return ([], totals);
        }

        var moveIds = moves.Select(t => t.id).ToList();
        var moveItems = (await connection.QueryAsync<ErpStockMoveItemEntity>("""
            SELECT * FROM trk_stock_move_item
            WHERE deleted = 0 AND stock_move_id IN @moveIds
            ORDER BY id
            """, new { moveIds })).AsList();
        var stockIds = moveItems.Where(t => t.stock_id.HasValue).Select(t => t.stock_id!.Value).Distinct().ToList();
        var stocks = stockIds.Count == 0
            ? new Dictionary<long, ErpBusinessStockEntity>()
            : (await connection.QueryAsync<ErpBusinessStockEntity>("""
                SELECT * FROM trk_stock WHERE deleted = 0 AND id IN @stockIds
                """, new { stockIds })).ToDictionary(t => t.id);

        var snapshots = moveItems.ToDictionary(t => t.id, t => ParseSnapshot(t.product_snapshot_json, t.remark));
        var fbaShipmentIds = snapshots.Values
            .Where(t => t.fba_shipment_id.HasValue)
            .Select(t => t.fba_shipment_id!.Value)
            .Distinct()
            .ToList();
        var fbaShipments = fbaShipmentIds.Count == 0
            ? new Dictionary<long, ErpFbaShipmentEntity>()
            : (await connection.QueryAsync<ErpFbaShipmentEntity>("""
                SELECT * FROM erp_fba_shipment WHERE deleted = 0 AND id IN @fbaShipmentIds
                """, new { fbaShipmentIds })).ToDictionary(t => t.id);
        var itemsByMove = moveItems.GroupBy(t => t.stock_move_id).ToDictionary(t => t.Key, t => t.ToList());

        var data = moves.Select(move => BuildViewModel(
            move,
            itemsByMove.GetValueOrDefault(move.id) ?? [],
            snapshots,
            stocks,
            fbaShipments)).ToList();
        return (data, totals);
    }

    public async Task<(bool flag, string msg)> PreparePickingAsync(long stockMoveId, CurrentUser currentUser)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync();
        var move = await connection.QuerySingleOrDefaultAsync<ErpStockMoveEntity>("""
            SELECT * FROM trk_stock_move
            WHERE id = @stockMoveId AND deleted = 0
              AND transfer_type = @transferType AND status = @status
              AND shipment_status = @shipmentStatus AND from_warehouse_id = @warehouseId
            LIMIT 1
            """, new
        {
            stockMoveId,
            transferType = FbaTransferType,
            status = WaitShipmentStatus,
            shipmentStatus = WaitShipmentStatus,
            warehouseId = ShenzhenWarehouseId
        });
        if (move == null)
        {
            return (false, "FBA发货单不存在、状态已变化或不属于深圳自建仓");
        }
        if (string.IsNullOrWhiteSpace(move.no) || move.no.Length > 32)
        {
            return (false, "ERP发货准备单号为空或超过WMS发货单号长度限制");
        }

        var moveItems = (await connection.QueryAsync<ErpStockMoveItemEntity>("""
            SELECT * FROM trk_stock_move_item
            WHERE deleted = 0 AND stock_move_id = @stockMoveId
            ORDER BY id
            """, new { stockMoveId })).AsList();
        if (moveItems.Count == 0)
        {
            return (false, "FBA发货单没有可拣货的商品明细");
        }

        var commodityIds = moveItems
            .Where(t => t.commodity_id.HasValue)
            .Select(t => t.commodity_id!.Value)
            .Distinct()
            .ToList();
        var commodityMaps = (await connection.QueryAsync<ErpCommodityMapEntity>("""
            SELECT erp_commodity_id, wms_spu_id, wms_sku_id, commodity_sku, tenant_id
            FROM wms_erp_commodity_map
            WHERE tenant_id = @tenantId AND erp_commodity_id IN @commodityIds
            """, new { tenantId = currentUser.tenant_id, commodityIds })).ToDictionary(t => t.erp_commodity_id);
        var missingItem = moveItems.FirstOrDefault(t => !t.commodity_id.HasValue || !commodityMaps.ContainsKey(t.commodity_id.Value));
        if (missingItem != null)
        {
            return (false, $"商品 {missingItem.commodity_sku ?? missingItem.commodity_name ?? missingItem.id.ToString()} 未匹配WMS SKU");
        }

        var requestedItems = new List<DispatchlistAddViewModel>();
        foreach (var group in moveItems.GroupBy(t => commodityMaps[t.commodity_id!.Value].wms_sku_id))
        {
            var quantity = group.Sum(t => ParseSnapshot(t.product_snapshot_json, t.remark).shipment_total_qty ?? t.qty);
            if (quantity <= 0 || quantity > int.MaxValue)
            {
                return (false, $"WMS SKU {group.Key} 的拣货数量无效");
            }
            requestedItems.Add(new DispatchlistAddViewModel { sku_id = group.Key, qty = (int)quantity });
        }

        var warehouseId = await connection.QuerySingleOrDefaultAsync<int?>("""
            SELECT id FROM wms_warehouse
            WHERE tenant_id = @tenantId AND erp_warehouse_id = @erpWarehouseId AND is_valid = 1
            LIMIT 1
            """, new { tenantId = currentUser.tenant_id, erpWarehouseId = ShenzhenWarehouseId });
        if (!warehouseId.HasValue)
        {
            return (false, "有座山深圳仓尚未绑定有效的WMS仓库");
        }

        var goodsOwnerId = await connection.QuerySingleOrDefaultAsync<int?>("""
            SELECT mapping.wms_goods_owner_id
            FROM wms_erp_goods_owner_map mapping
            INNER JOIN wms_goodsowner owner
              ON owner.id = mapping.wms_goods_owner_id
             AND owner.tenant_id = mapping.tenant_id
             AND owner.is_valid = 1
            WHERE mapping.tenant_id = @tenantId
              AND mapping.erp_dept_id = @deptId
              AND mapping.erp_order_user_id = @orderUserId
            LIMIT 1
            """, new
        {
            tenantId = currentUser.tenant_id,
            deptId = move.dept_id ?? 0,
            orderUserId = move.order_user_id ?? 0
        });
        if (!goodsOwnerId.HasValue)
        {
            return (false, "发货归属尚未匹配有效的WMS库存所属人");
        }

        return await _dispatchlistService.PreparePickingAsync(
            move.no,
            warehouseId.Value,
            goodsOwnerId.Value,
            requestedItems,
            currentUser);
    }

    private static FbaShipmentViewModel BuildViewModel(
        ErpStockMoveEntity move,
        List<ErpStockMoveItemEntity> moveItems,
        Dictionary<long, PreparedItemSnapshot> snapshots,
        Dictionary<long, ErpBusinessStockEntity> stocks,
        Dictionary<long, ErpFbaShipmentEntity> fbaShipments)
    {
        var itemViewModels = moveItems.Select(item =>
        {
            var snapshot = snapshots.GetValueOrDefault(item.id) ?? new PreparedItemSnapshot();
            var stock = item.stock_id.HasValue ? stocks.GetValueOrDefault(item.stock_id.Value) : null;
            var shipmentTotalQty = snapshot.shipment_total_qty ?? item.qty;
            var inventoryReady = stock != null
                && item.qty == shipmentTotalQty
                && stock.occupied_qty >= shipmentTotalQty;
            return new FbaShipmentItemViewModel
            {
                stock_move_item_id = item.id,
                stock_id = item.stock_id,
                commodity_id = item.commodity_id,
                fba_shipment_item_id = snapshot.fba_shipment_item_id,
                main_image = snapshot.main_image,
                commodity_name = FirstNotEmpty(snapshot.commodity_name, item.commodity_name ?? string.Empty),
                stock_sku = FirstNotEmpty(snapshot.stock_sku, item.commodity_sku ?? string.Empty),
                fba_sku = snapshot.fba_sku,
                qty = snapshot.qty ?? item.qty,
                variant_qty = snapshot.variant_qty ?? 1,
                shipment_total_qty = shipmentTotalQty,
                sku_matched = string.Equals(snapshot.stock_sku, snapshot.fba_sku, StringComparison.OrdinalIgnoreCase),
                sku_mismatch_confirmed = snapshot.sku_mismatch_confirmed,
                stock_available_qty = stock?.available_qty ?? 0,
                stock_occupied_qty = stock?.occupied_qty ?? 0,
                stock_total_qty = stock?.total_qty ?? 0,
                inventory_ready = inventoryReady
            };
        }).ToList();

        var firstSnapshot = moveItems
            .Select(t => snapshots.GetValueOrDefault(t.id))
            .FirstOrDefault(t => t?.fba_shipment_id.HasValue == true) ?? new PreparedItemSnapshot();
        var fbaShipmentId = firstSnapshot.fba_shipment_id ?? 0;
        var shipment = fbaShipmentId > 0 ? fbaShipments.GetValueOrDefault(fbaShipmentId) : null;
        var inventoryReady = itemViewModels.Count > 0 && itemViewModels.All(t => t.inventory_ready);

        return new FbaShipmentViewModel
        {
            stock_move_id = move.id,
            stock_move_no = move.no,
            fba_shipment_id = fbaShipmentId,
            fba_no = FirstNotEmpty(shipment?.amazon_shipment_id ?? string.Empty, firstSnapshot.fba_no),
            shipment_name = FirstNotEmpty(shipment?.name ?? string.Empty, firstSnapshot.fba_shipment_name),
            fba_status = FirstNotEmpty(shipment?.shipment_status ?? string.Empty, firstSnapshot.fba_shipment_status),
            fulfillment_center_id = FirstNotEmpty(shipment?.fulfillment_center_id ?? string.Empty, firstSnapshot.fulfillment_center_id),
            shop_name = shipment?.shop_name ?? string.Empty,
            marketplace_name = shipment?.marketplace_name ?? string.Empty,
            shipping_mode = shipment?.shipping_mode ?? string.Empty,
            shipping_solution = shipment?.shipping_solution ?? string.Empty,
            dept_id = move.dept_id,
            dept_name = move.dept_name ?? string.Empty,
            order_user_id = move.order_user_id,
            order_user_name = move.order_user_name ?? string.Empty,
            creator = move.creator ?? string.Empty,
            from_warehouse_id = move.from_warehouse_id,
            from_warehouse_name = move.from_warehouse_name ?? string.Empty,
            freight_forwarder_id = move.to_freight_forwarder_id,
            freight_forwarder_name = move.to_freight_forwarder_name ?? string.Empty,
            logistics_name = move.logistics_name ?? string.Empty,
            product_count = itemViewModels.Count,
            shipment_total_qty = itemViewModels.Sum(t => t.shipment_total_qty),
            locked_qty = move.frozen_qty,
            inventory_ready = inventoryReady,
            inventory_status_name = inventoryReady ? "库存已锁定" : "库存待核对",
            prepared_time = firstSnapshot.prepared_time ?? move.create_time,
            source_update_time = move.update_time,
            item_list = itemViewModels
        };
    }

    private static PreparedItemSnapshot ParseSnapshot(string? productSnapshotJson, string? remark)
    {
        var json = !string.IsNullOrWhiteSpace(productSnapshotJson) ? productSnapshotJson : remark;
        if (string.IsNullOrWhiteSpace(json))
        {
            return new PreparedItemSnapshot();
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            var item = document.RootElement;
            if (item.ValueKind != JsonValueKind.Object)
            {
                return new PreparedItemSnapshot();
            }

            return new PreparedItemSnapshot
            {
                fba_shipment_id = GetInt64(item, "fbaShipmentId"),
                fba_no = GetString(item, "fbaNo"),
                fba_shipment_name = GetString(item, "fbaShipmentName"),
                fba_shipment_status = GetString(item, "fbaShipmentStatus"),
                fulfillment_center_id = GetString(item, "fulfillmentCenterId"),
                fba_shipment_item_id = GetInt64(item, "fbaShipmentItemId"),
                main_image = GetString(item, "mainImage"),
                commodity_name = GetString(item, "commodityName"),
                stock_sku = GetString(item, "stockSku"),
                fba_sku = GetString(item, "fbaSku"),
                qty = GetInt64(item, "qty"),
                variant_qty = GetInt64(item, "variantQty"),
                shipment_total_qty = GetInt64(item, "shipmentTotalQty"),
                sku_mismatch_confirmed = GetBoolean(item, "skuMismatchConfirmed"),
                prepared_time = GetDateTime(item, "preparedTime")
            };
        }
        catch (JsonException)
        {
            return new PreparedItemSnapshot();
        }
    }

    private static string FindSearchText(PageSearch pageSearch, string name)
    {
        return pageSearch.searchObjects
            .FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase))
            ?.Text
            ?.Trim() ?? string.Empty;
    }

    private static string GetString(JsonElement item, string propertyName)
    {
        return item.TryGetProperty(propertyName, out var value)
            ? value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : value.ToString()
            : string.Empty;
    }

    private static long? GetInt64(JsonElement item, string propertyName)
    {
        if (!item.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        return value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number)
            ? number
            : long.TryParse(value.ToString(), out number) ? number : null;
    }

    private static bool GetBoolean(JsonElement item, string propertyName)
    {
        if (!item.TryGetProperty(propertyName, out var value))
        {
            return false;
        }

        return value.ValueKind == JsonValueKind.True
            || (value.ValueKind == JsonValueKind.String && bool.TryParse(value.GetString(), out var result) && result);
    }

    private static DateTime? GetDateTime(JsonElement item, string propertyName)
    {
        var value = GetString(item, propertyName);
        return DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var result)
            ? result
            : null;
    }

    private static string FirstNotEmpty(params string[] values)
    {
        return values.FirstOrDefault(t => !string.IsNullOrWhiteSpace(t)) ?? string.Empty;
    }

    private sealed class PreparedItemSnapshot
    {
        public long? fba_shipment_id { get; set; }
        public string fba_no { get; set; } = string.Empty;
        public string fba_shipment_name { get; set; } = string.Empty;
        public string fba_shipment_status { get; set; } = string.Empty;
        public string fulfillment_center_id { get; set; } = string.Empty;
        public long? fba_shipment_item_id { get; set; }
        public string main_image { get; set; } = string.Empty;
        public string commodity_name { get; set; } = string.Empty;
        public string stock_sku { get; set; } = string.Empty;
        public string fba_sku { get; set; } = string.Empty;
        public long? qty { get; set; }
        public long? variant_qty { get; set; }
        public long? shipment_total_qty { get; set; }
        public bool sku_mismatch_confirmed { get; set; }
        public DateTime? prepared_time { get; set; }
    }
}
