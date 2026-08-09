using Microsoft.EntityFrameworkCore;
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

        var query =
            from item in _ruoyiDbContext.ReceiptItems.AsNoTracking()
            join shipment in _ruoyiDbContext.LogisticsInfos.AsNoTracking()
                on item.shipment_id equals shipment.id
            where item.tenant_id == currentUser.tenant_id && !shipment.deleted
            select new { item, shipment };

        if (!string.IsNullOrWhiteSpace(deptName))
        {
            query = query.Where(t => t.item.dept_name.Contains(deptName));
        }

        if (!string.IsNullOrWhiteSpace(orderUserName))
        {
            query = query.Where(t => t.item.order_user_name.Contains(orderUserName));
        }

        if (!string.IsNullOrWhiteSpace(productKeyword))
        {
            query = query.Where(t => t.item.commodity_sku.Contains(productKeyword)
                || t.item.commodity_name.Contains(productKeyword)
                || t.shipment.shipment_batch_no.Contains(productKeyword)
                || (t.shipment.purchase_no != null && t.shipment.purchase_no.Contains(productKeyword)));
        }

        var totals = await query.CountAsync();
        var pageIndex = Math.Max(pageSearch.pageIndex, 1);
        var pageSize = Math.Clamp(pageSearch.pageSize, 1, 200);
        var rows = await query
            .OrderByDescending(t => t.item.receipt_time)
            .ThenByDescending(t => t.item.id)
            .Skip((pageIndex - 1) * pageSize)
            .Take(pageSize)
            .Select(t => new
            {
                t.item.id,
                t.item.shipment_id,
                purchase_no = t.shipment.purchase_no ?? string.Empty,
                shipment_batch_no = t.shipment.shipment_batch_no ?? string.Empty,
                t.item.commodity_id,
                t.item.commodity_sku,
                t.item.commodity_name,
                t.item.dept_name,
                t.item.order_user_name,
                t.item.warehouse_area_id,
                t.item.warehouse_area_name,
                t.item.receipt_time,
                t.item.actual_receipt_qty,
                t.item.loss_qty,
                t.item.inbound_qty,
                t.item.total_weight,
                t.item.total_volume,
                t.shipment.product_snapshot_json
            })
            .ToListAsync();

        var productsByShipment = new Dictionary<long, List<ErpPendingReceiptProductViewModel>>();
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
                total_volume = row.total_volume
            };
        }).ToList();
        return (data, totals);
    }
}
