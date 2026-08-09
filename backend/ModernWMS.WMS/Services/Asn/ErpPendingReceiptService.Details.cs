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
            select new ErpReceiptDetailViewModel
            {
                id = item.id,
                shipment_id = item.shipment_id,
                shipment_batch_no = shipment.shipment_batch_no ?? string.Empty,
                commodity_sku = item.commodity_sku,
                commodity_name = item.commodity_name,
                dept_name = item.dept_name,
                order_user_name = item.order_user_name,
                warehouse_area_id = item.warehouse_area_id,
                warehouse_area_name = item.warehouse_area_name,
                receipt_time = item.receipt_time,
                actual_receipt_qty = item.actual_receipt_qty,
                loss_qty = item.loss_qty,
                inbound_qty = item.inbound_qty,
                total_weight = item.total_weight,
                total_volume = item.total_volume
            };

        if (!string.IsNullOrWhiteSpace(deptName))
        {
            query = query.Where(t => t.dept_name.Contains(deptName));
        }

        if (!string.IsNullOrWhiteSpace(orderUserName))
        {
            query = query.Where(t => t.order_user_name.Contains(orderUserName));
        }

        if (!string.IsNullOrWhiteSpace(productKeyword))
        {
            query = query.Where(t => t.commodity_sku.Contains(productKeyword)
                || t.commodity_name.Contains(productKeyword)
                || t.shipment_batch_no.Contains(productKeyword));
        }

        var totals = await query.CountAsync();
        var pageIndex = Math.Max(pageSearch.pageIndex, 1);
        var pageSize = Math.Clamp(pageSearch.pageSize, 1, 200);
        var data = await query
            .OrderByDescending(t => t.receipt_time)
            .ThenByDescending(t => t.id)
            .Skip((pageIndex - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();
        return (data, totals);
    }
}
