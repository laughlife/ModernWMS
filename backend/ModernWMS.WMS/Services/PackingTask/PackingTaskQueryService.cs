using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using ModernWMS.Core.DBContext;
using ModernWMS.Core.JWT;
using ModernWMS.Core.Models;
using ModernWMS.WMS.Entities.ViewModels;
using ModernWMS.WMS.IServices;

namespace ModernWMS.WMS.Services;

/// <summary>
/// Reads formal packing-task snapshots without creating WMS or FBA business facts.
/// </summary>
public class PackingTaskQueryService : IPackingTaskQueryService
{
    private readonly RuoyiDbContext _ruoyiDbContext;
    private readonly IConfiguration _configuration;

    public PackingTaskQueryService(
        RuoyiDbContext ruoyiDbContext,
        IConfiguration configuration)
    {
        _ruoyiDbContext = ruoyiDbContext;
        _configuration = configuration;
    }

    public async Task<PackingTaskQueryResult> PageAsync(PageSearch pageSearch, CurrentUser _)
    {
        if (!_configuration.GetValue("Features:PackingTaskFirstStep", false))
        {
            return Failure("装箱任务功能未启用");
        }

        var keyword = FindSearchText(pageSearch, "keyword");
        var query = _ruoyiDbContext.PackingTasks.AsNoTracking()
            .Where(t => !t.source_deleted
                && !t.source_canceled);

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            query = query.Where(t => t.packing_task_sn.Contains(keyword)
                || _ruoyiDbContext.PackingTaskItems.Any(i => !i.source_deleted
                    && i.sellfox_task_id == t.sellfox_task_id
                    && ((i.commodity_name != null && i.commodity_name.Contains(keyword))
                        || (i.commodity_sku != null && i.commodity_sku.Contains(keyword))
                        || (i.sku != null && i.sku.Contains(keyword))
                        || (i.fn_sku != null && i.fn_sku.Contains(keyword))
                        || (i.msku != null && i.msku.Contains(keyword)))));
        }

        var totals = await query.CountAsync();
        var pageIndex = Math.Max(pageSearch.pageIndex, 1);
        var pageSize = Math.Clamp(pageSearch.pageSize, 1, 200);
        var tasks = await query
            .OrderByDescending(t => t.source_create_time)
            .ThenByDescending(t => t.id)
            .Skip((pageIndex - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        if (tasks.Count == 0)
        {
            return new PackingTaskQueryResult(true, string.Empty, [], totals);
        }

        var taskIds = tasks.Select(t => t.sellfox_task_id).ToList();
        var items = await _ruoyiDbContext.PackingTaskItems.AsNoTracking()
            .Where(t => !t.source_deleted && taskIds.Contains(t.sellfox_task_id))
            .OrderBy(t => t.id)
            .ToListAsync();
        var itemsByTask = items.GroupBy(t => t.sellfox_task_id).ToDictionary(t => t.Key, t => t.ToList());

        var data = tasks.Select(task => new PackingTaskQueryViewModel
        {
            id = task.id,
            sellfox_task_id = task.sellfox_task_id,
            packing_task_sn = task.packing_task_sn,
            warehouse_id = task.warehouse_id,
            warehouse_name = task.warehouse_name,
            complete_num = task.complete_num,
            task_num = task.task_num,
            create_name = task.create_name,
            source_create_time = task.source_create_time,
            item_count = task.item_count,
            shop_name = task.shop_name,
            marketplace_name = task.marketplace_name,
            item_list = (itemsByTask.GetValueOrDefault(task.sellfox_task_id) ?? [])
                .Select(item => new PackingTaskQueryItemViewModel
                {
                    id = item.id,
                    sellfox_item_id = item.sellfox_item_id,
                    commodity_id = item.commodity_id,
                    commodity_sku = item.commodity_sku,
                    commodity_name = item.commodity_name,
                    main_image = item.main_image,
                    fn_sku = item.fn_sku,
                    sku = item.sku,
                    msku = item.msku,
                    task_num = item.task_num,
                    quantity_shipped = item.quantity_shipped,
                    stock_available = item.stock_available
                }).ToList()
        }).ToList();

        return new PackingTaskQueryResult(true, string.Empty, data, totals);
    }

    private static PackingTaskQueryResult Failure(string message) => new(false, message, [], 0);

    private static string FindSearchText(PageSearch pageSearch, string name)
    {
        return pageSearch.searchObjects
            .FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase))
            ?.Text
            ?.Trim() ?? string.Empty;
    }
}
