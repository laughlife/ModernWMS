using Dapper;
using ModernWMS.Core.JWT;
using ModernWMS.Core.Models;
using ModernWMS.WMS.Entities.ViewModels;

namespace ModernWMS.WMS.Services;

/// <summary>
/// 表示 ErpPendingReceiptService 类型。
/// </summary>
public partial class ErpPendingReceiptService
{
    /// <summary>
    /// 执行 ReceiptDetailsPageAsync 操作。
    /// </summary>
    public async Task<(List<ErpReceiptDetailViewModel> data, int totals)> ReceiptDetailsPageAsync(
        PageSearch pageSearch,
        CurrentUser currentUser)
    {
        var deptName = FindSearchText(pageSearch, "dept_name");
        var orderUserName = FindSearchText(pageSearch, "order_user_name");
        var productKeyword = FindSearchText(pageSearch, "product_keyword");
        var warehouseId = await ResolveWarehouseAsync(pageSearch, currentUser);
        if (warehouseId == null)
        {
            return ([], 0);
        }

        var clauses = new List<string> { "1=1" };
        if (!string.IsNullOrWhiteSpace(deptName)) clauses.Add("d.`dept_name` LIKE @deptName");
        if (!string.IsNullOrWhiteSpace(orderUserName)) clauses.Add("d.`order_user_name` LIKE @orderUserName");
        if (!string.IsNullOrWhiteSpace(productKeyword))
            clauses.Add("(d.`commodity_sku` LIKE @productKeyword OR d.`commodity_name` LIKE @productKeyword OR d.`shipment_batch_no` LIKE @productKeyword OR d.`purchase_no` LIKE @productKeyword)");
        var pageIndex = Math.Max(pageSearch.pageIndex, 1);
        var pageSize = Math.Clamp(pageSearch.pageSize, 1, 200);
        var parameters = new
        {
            warehouseId = warehouseId.Value,
            deptName = $"%{deptName}%",
            orderUserName = $"%{orderUserName}%",
            productKeyword = $"%{productKeyword}%",
            offset = (pageIndex - 1) * pageSize,
            pageSize
        };
        var receiptRowsSql = """
            WITH `wms_receipt_stocks` AS
            (
                SELECT DISTINCT receipt.`shipment_id`,item.`erp_stock_id`
                  FROM `wms_erp_receipt` receipt
                  JOIN `trk_logistics_info` receipt_shipment
                    ON receipt_shipment.`id`=receipt.`shipment_id`
                   AND receipt_shipment.`deleted`=b'0'
                   AND receipt_shipment.`to_warehouse_id`=@warehouseId
                  JOIN `wms_erp_receipt_item` item ON item.`receipt_id`=receipt.`id`
                 WHERE item.`erp_stock_id`>0
            ),
            `wms_ambiguous_receipts` AS
            (
                SELECT DISTINCT receipt.`shipment_id`
                  FROM `wms_erp_receipt` receipt
                  JOIN `trk_logistics_info` receipt_shipment
                    ON receipt_shipment.`id`=receipt.`shipment_id`
                   AND receipt_shipment.`deleted`=b'0'
                   AND receipt_shipment.`to_warehouse_id`=@warehouseId
                  JOIN `wms_erp_receipt_item` item ON item.`receipt_id`=receipt.`id`
                 WHERE COALESCE(item.`erp_stock_id`,0)<=0
            ),
            `erp_history_records` AS
            (
                SELECT r.`id`,r.`id` AS `stock_record_id`,r.`biz_id` AS `shipment_id`,
                       r.`stock_id` AS `erp_stock_id`,
                       COALESCE(r.`commodity_id`,s.`commodity_id`) AS `commodity_id`,
                       COALESCE(r.`commodity_sku`,s.`commodity_sku`,'') AS `commodity_sku`,
                       COALESCE(r.`commodity_name`,s.`commodity_name`,'') AS `commodity_name`,
                       COALESCE(r.`dept_id`,s.`dept_id`) AS `dept_id`,
                       COALESCE(r.`order_user_id`,s.`order_user_id`) AS `order_user_id`,
                       COALESCE(s.`dept_name`,'') AS `dept_name`,
                       COALESCE(s.`order_user_name`,'') AS `order_user_name`,
                       COALESCE(r.`operate_time`,r.`create_time`) AS `receipt_time`,
                       r.`change_qty` AS `inbound_qty`
                  FROM `trk_stock_record` r
                  JOIN `trk_logistics_info` hl
                    ON hl.`id`=r.`biz_id` AND hl.`deleted`=b'0'
                   AND hl.`lifecycle_status`='RECEIVED' AND hl.`to_warehouse_id`=@warehouseId
                  LEFT JOIN `trk_stock` s ON s.`id`=r.`stock_id`
                  LEFT JOIN `wms_receipt_stocks` covered
                    ON covered.`shipment_id`=r.`biz_id` AND covered.`erp_stock_id`=r.`stock_id`
                  LEFT JOIN `wms_ambiguous_receipts` ambiguous
                    ON ambiguous.`shipment_id`=r.`biz_id`
                 WHERE r.`deleted`=b'0' AND r.`biz_type`='RECEIPT_IN' AND r.`change_qty`>0
                   AND covered.`shipment_id` IS NULL AND ambiguous.`shipment_id` IS NULL
            ),
            `receipt_rows` AS
            (
                SELECT CAST(i.`id` AS SIGNED) AS `id`,NULL AS `stock_record_id`,
                       i.`shipment_id`,i.`erp_stock_id`,
                       COALESCE(l.`purchase_no`,'') AS `purchase_no`,
                       COALESCE(l.`shipment_batch_no`,'') AS `shipment_batch_no`,i.`commodity_id`,
                       i.`commodity_sku`,i.`commodity_name`,i.`dept_name`,i.`order_user_name`,
                       i.`warehouse_area_id`,i.`warehouse_area_name`,
                       i.`goods_location_id`,i.`goods_location_name`,i.`receipt_time`,
                       i.`actual_receipt_qty`,i.`loss_qty`,i.`inbound_qty`,i.`total_weight`,i.`total_volume`,
                       l.`product_snapshot_json`,l.`to_warehouse_id` AS `warehouse_id`,
                       COALESCE(l.`to_warehouse_name`,'') AS `warehouse_name`,
                       COALESCE(l.`lifecycle_status`,'') AS `lifecycle_status`,'WMS_RECEIPT' AS `data_source`,
                       CASE WHEN i.`inbound_qty`<=0 THEN 'NONE' ELSE 'ACTIVE' END AS `location_state`
                  FROM `wms_erp_receipt_item` i
                  JOIN `trk_logistics_info` l ON l.`id`=i.`shipment_id` AND l.`deleted`=b'0'
                 WHERE l.`to_warehouse_id`=@warehouseId

                UNION ALL

                SELECT h.`id`,h.`stock_record_id`,h.`shipment_id`,h.`erp_stock_id`,
                       COALESCE(l.`purchase_no`,'') AS `purchase_no`,
                       COALESCE(l.`shipment_batch_no`,'') AS `shipment_batch_no`,h.`commodity_id`,
                       h.`commodity_sku`,h.`commodity_name`,h.`dept_name`,h.`order_user_name`,
                       NULL AS `warehouse_area_id`,'' AS `warehouse_area_name`,
                       NULL AS `goods_location_id`,'' AS `goods_location_name`,
                       COALESCE(h.`receipt_time`,l.`receipt_time`,l.`update_time`) AS `receipt_time`,
                       h.`inbound_qty` AS `actual_receipt_qty`,0 AS `loss_qty`,h.`inbound_qty`,
                       NULL AS `total_weight`,NULL AS `total_volume`,l.`product_snapshot_json`,
                       l.`to_warehouse_id` AS `warehouse_id`,COALESCE(l.`to_warehouse_name`,'') AS `warehouse_name`,
                       l.`lifecycle_status`,'ERP_HISTORY' AS `data_source`,
                       'UNLOCATED' AS `location_state`
                  FROM `erp_history_records` h
                  JOIN `trk_logistics_info` l ON l.`id`=h.`shipment_id` AND l.`deleted`=b'0'
                 WHERE l.`lifecycle_status`='RECEIVED' AND l.`to_warehouse_id`=@warehouseId
            )
            """;
        await using var connection = await _connectionFactory.OpenConnectionAsync();
        using var grid = await connection.QueryMultipleAsync($"""
            {receiptRowsSql}
            SELECT COUNT(*) FROM `receipt_rows` d WHERE {string.Join(" AND ", clauses)};
            {receiptRowsSql}
            SELECT * FROM `receipt_rows` d WHERE {string.Join(" AND ", clauses)}
             ORDER BY d.`receipt_time` DESC,d.`id` DESC LIMIT @pageSize OFFSET @offset;
            """, parameters);
        var totals = await grid.ReadSingleAsync<int>();
        var rows = (await grid.ReadAsync<ReceiptDetailRow>()).AsList();

        var productsByShipment = new Dictionary<long, List<ErpPendingReceiptProductViewModel>>();
        var receiptAllocationsByItem = await ReadReceiptAllocationsAsync(
            rows.Where(t => t.data_source == "WMS_RECEIPT").Select(t => checked((int)t.id)).ToList());
        var data = rows.Select(row =>
        {
            if (!productsByShipment.TryGetValue(row.shipment_id, out var products))
            {
                products = ParseProducts(row.product_snapshot_json);
                productsByShipment[row.shipment_id] = products;
            }

            var product = row.commodity_id.HasValue
                ? products.FirstOrDefault(t => t.commodity_id == row.commodity_id)
                : null;
            product ??= products.FirstOrDefault(t => string.Equals(
                t.sku,
                row.commodity_sku,
                StringComparison.OrdinalIgnoreCase));

            List<ErpReceiptAllocationViewModel>? allocations;
            if (row.data_source == "WMS_RECEIPT")
            {
                allocations = receiptAllocationsByItem.GetValueOrDefault(checked((int)row.id));
                if (allocations == null || allocations.Count == 0)
                {
                    allocations = row.inbound_qty <= 0
                        ? []
                        :
                        [
                            new ErpReceiptAllocationViewModel
                            {
                                warehouse_area_id = row.warehouse_area_id,
                                warehouse_area_name = row.warehouse_area_name,
                                goods_location_id = row.goods_location_id,
                                goods_location_name = row.goods_location_name,
                                goods_owner_name = row.order_user_name,
                                qty = row.inbound_qty
                            }
                        ];
                }
            }
            else
            {
                allocations = [];
            }
            var locationState = row.location_state;

            return new ErpReceiptDetailViewModel
            {
                id = row.id,
                shipment_id = row.shipment_id,
                erp_stock_id = row.erp_stock_id,
                purchase_no = row.purchase_no,
                shipment_batch_no = row.shipment_batch_no,
                commodity_sku = row.commodity_sku,
                commodity_name = row.commodity_name,
                main_image = product?.main_image ?? string.Empty,
                dept_name = row.dept_name,
                order_user_name = row.order_user_name,
                warehouse_area_id = row.warehouse_area_id,
                warehouse_area_name = row.warehouse_area_name,
                goods_location_id = row.goods_location_id,
                goods_location_name = row.goods_location_name,
                warehouse_id = row.warehouse_id,
                warehouse_name = row.warehouse_name,
                lifecycle_status = row.lifecycle_status,
                location_state = locationState,
                data_source = row.data_source,
                unlocated = string.Equals(locationState, "UNLOCATED", StringComparison.Ordinal),
                receipt_time = row.receipt_time,
                actual_receipt_qty = row.actual_receipt_qty,
                loss_qty = row.loss_qty,
                inbound_qty = row.inbound_qty,
                total_weight = row.total_weight,
                total_volume = row.total_volume,
                allocation_list = allocations ?? []
            };
        }).ToList();
        return (data, totals);
    }

    private sealed record ReceiptDetailRow(
        long id, long? stock_record_id, long shipment_id, long? erp_stock_id,
        string purchase_no, string shipment_batch_no,
        long? commodity_id, string commodity_sku, string commodity_name, string dept_name,
        string order_user_name, int? warehouse_area_id, string warehouse_area_name,
        int? goods_location_id, string goods_location_name,
        DateTime receipt_time, long actual_receipt_qty, long loss_qty, long inbound_qty,
        decimal? total_weight, decimal? total_volume, string product_snapshot_json,
        long warehouse_id, string warehouse_name, string lifecycle_status, string data_source,
        string location_state);

}
