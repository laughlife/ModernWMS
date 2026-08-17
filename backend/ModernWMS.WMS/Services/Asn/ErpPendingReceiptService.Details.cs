using Dapper;
using ModernWMS.Core.JWT;
using ModernWMS.Core.Models;
using ModernWMS.WMS.Entities.ViewModels;

namespace ModernWMS.WMS.Services;

public partial class ErpPendingReceiptService
{
    public async Task<(List<ErpReceiptDetailViewModel> data, int totals)> ReceiptDetailsPageAsync(
        PageSearch pageSearch,
        CurrentUser currentUser)
    {
        var deptName = FindSearchText(pageSearch, "dept_name");
        var orderUserName = FindSearchText(pageSearch, "order_user_name");
        var productKeyword = FindSearchText(pageSearch, "product_keyword");

        var clauses = new List<string> { "i.`tenant_id`=@tenantId", "s.`deleted`=b'0'" };
        if (!string.IsNullOrWhiteSpace(deptName)) clauses.Add("i.`dept_name` LIKE @deptName");
        if (!string.IsNullOrWhiteSpace(orderUserName)) clauses.Add("i.`order_user_name` LIKE @orderUserName");
        if (!string.IsNullOrWhiteSpace(productKeyword))
            clauses.Add("(i.`commodity_sku` LIKE @productKeyword OR i.`commodity_name` LIKE @productKeyword OR s.`shipment_batch_no` LIKE @productKeyword OR s.`purchase_no` LIKE @productKeyword)");
        var pageIndex = Math.Max(pageSearch.pageIndex, 1);
        var pageSize = Math.Clamp(pageSearch.pageSize, 1, 200);
        var parameters = new { tenantId=currentUser.tenant_id, deptName=$"%{deptName}%",
            orderUserName=$"%{orderUserName}%", productKeyword=$"%{productKeyword}%",
            offset=(pageIndex - 1) * pageSize, pageSize };
        await using var connection = await _connectionFactory.OpenConnectionAsync();
        using var grid = await connection.QueryMultipleAsync($"""
            SELECT COUNT(*) FROM `wms_erp_receipt_item` i JOIN `trk_logistics_info` s ON s.`id`=i.`shipment_id`
            WHERE {string.Join(" AND ", clauses)};
            SELECT i.`id`,i.`shipment_id`,COALESCE(s.`purchase_no`,'') AS `purchase_no`,
                COALESCE(s.`shipment_batch_no`,'') AS `shipment_batch_no`,i.`commodity_id`,i.`commodity_sku`,
                i.`commodity_name`,i.`dept_name`,i.`order_user_name`,i.`warehouse_area_id`,i.`warehouse_area_name`,
                i.`receipt_time`,i.`actual_receipt_qty`,i.`loss_qty`,i.`inbound_qty`,i.`total_weight`,i.`total_volume`,
                s.`product_snapshot_json`
            FROM `wms_erp_receipt_item` i JOIN `trk_logistics_info` s ON s.`id`=i.`shipment_id`
            WHERE {string.Join(" AND ", clauses)} ORDER BY i.`receipt_time` DESC,i.`id` DESC LIMIT @pageSize OFFSET @offset;
            """, parameters);
        var totals = await grid.ReadSingleAsync<int>();
        var rows = (await grid.ReadAsync<ReceiptDetailRow>()).AsList();

        var productsByShipment = new Dictionary<long, List<ErpPendingReceiptProductViewModel>>();
        var allocationsByItem = await ReadReceiptAllocationsAsync(
            rows.Select(t => t.id).ToList(),
            currentUser.tenant_id);
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

            var allocations = allocationsByItem.GetValueOrDefault(row.id);
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
                            goods_owner_name = row.order_user_name,
                            qty = row.inbound_qty
                        }
                    ];
            }

            return new ErpReceiptDetailViewModel
            {
                id = row.id,
                shipment_id = row.shipment_id,
                purchase_no = row.purchase_no,
                shipment_batch_no = row.shipment_batch_no,
                commodity_sku = row.commodity_sku,
                commodity_name = row.commodity_name,
                main_image = product?.main_image ?? string.Empty,
                dept_name = row.dept_name,
                order_user_name = row.order_user_name,
                warehouse_area_id = row.warehouse_area_id,
                warehouse_area_name = row.warehouse_area_name,
                receipt_time = row.receipt_time,
                actual_receipt_qty = row.actual_receipt_qty,
                loss_qty = row.loss_qty,
                inbound_qty = row.inbound_qty,
                total_weight = row.total_weight,
                total_volume = row.total_volume,
                allocation_list = allocations
            };
        }).ToList();
        return (data, totals);
    }

    private sealed record ReceiptDetailRow(
        int id, long shipment_id, string purchase_no, string shipment_batch_no, long? commodity_id,
        string commodity_sku, string commodity_name, string dept_name, string order_user_name,
        int warehouse_area_id, string warehouse_area_name, DateTime receipt_time,
        long actual_receipt_qty, long loss_qty, long inbound_qty, decimal? total_weight,
        decimal? total_volume, string product_snapshot_json);
}
