using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using ModernWMS.Core.DBContext;
using ModernWMS.Core.DBContext.Entities;
using ModernWMS.Core.JWT;
using ModernWMS.Core.Models;
using ModernWMS.WMS.Entities.ViewModels;
using ModernWMS.WMS.Entities.ViewModels.PackingTask;
using ModernWMS.WMS.IServices;
using ModernWMS.WMS.Entities.Models;
using ModernWMS.WMS.Entities.Models.PackingTask;

namespace ModernWMS.WMS.Services;

/// <summary>
/// Reads formal packing-task snapshots without creating WMS or FBA business facts.
/// </summary>
public class PackingTaskQueryService : IPackingTaskQueryService
{
    private readonly RuoyiDbContext _ruoyiDbContext;
    private readonly IConfiguration _configuration;
    private readonly SqlDBContext? _wmsDbContext;
    private readonly IWarehouseAccessService? _warehouseAccessService;

    public PackingTaskQueryService(
        RuoyiDbContext ruoyiDbContext,
        IConfiguration configuration)
        : this(ruoyiDbContext, configuration, null, null)
    {
    }

    public PackingTaskQueryService(
        RuoyiDbContext ruoyiDbContext,
        IConfiguration configuration,
        SqlDBContext? wmsDbContext,
        IWarehouseAccessService? warehouseAccessService)
    {
        _ruoyiDbContext = ruoyiDbContext;
        _configuration = configuration;
        _wmsDbContext = wmsDbContext;
        _warehouseAccessService = warehouseAccessService;
    }

    public async Task<PackingTaskQueryResult> PageAsync(PageSearch pageSearch, CurrentUser currentUser)
    {
        if (!_configuration.GetValue("Features:PackingTaskFirstStep", false))
        {
            return Failure("装箱任务功能未启用");
        }

        var keyword = FindSearchText(pageSearch, "keyword");
        var warehouseText = FindSearchText(pageSearch, "warehouse_id");
        long? warehouseId = long.TryParse(warehouseText, out var parsedWarehouseId) && parsedWarehouseId > 0
            ? parsedWarehouseId
            : null;
        if (_warehouseAccessService != null)
        {
            if (warehouseId == null)
            {
                warehouseId = (await _warehouseAccessService.GetAllowedAsync(currentUser)).default_warehouse_id;
                if (warehouseId == null)
                {
                    return new PackingTaskQueryResult(true, string.Empty, [], 0);
                }
            }
            else
            {
                await _warehouseAccessService.EnsureAllowedAsync(warehouseId.Value, currentUser);
            }
        }

        var query = _ruoyiDbContext.PackingTasks.AsNoTracking()
            .Where(t => !t.source_deleted
                && !t.source_canceled);

        if (warehouseId != null)
        {
            query = query.Where(t => t.warehouse_id == warehouseId.Value);
        }

        if (_wmsDbContext != null)
        {
            var activeSourceTaskIds = await _wmsDbContext.GetDbSet<DispatchPackingTaskEntity>()
                .AsNoTracking()
                .Where(t => t.active_source_task_id != null)
                .Select(t => t.active_source_task_id!.Value)
                .ToListAsync();
            if (activeSourceTaskIds.Count > 0)
            {
                query = query.Where(t => !activeSourceTaskIds.Contains(t.sellfox_task_id));
            }
        }

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
        var stockAvailability = await ResolveStockAvailabilityAsync(items, currentUser.tenant_id);

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
                .Select(item => BuildItemViewModel(item, stockAvailability))
                .ToList()
        }).ToList();

        return new PackingTaskQueryResult(true, string.Empty, data, totals);
    }

    private static PackingTaskQueryItemViewModel BuildItemViewModel(
        ErpPackingTaskItemEntity item,
        IReadOnlyDictionary<long, StockAvailability> stockAvailability)
    {
        var availability = stockAvailability.GetValueOrDefault(item.id);
        return new PackingTaskQueryItemViewModel
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
            stock_available = item.stock_available,
            stock_sku_code = availability?.SkuCode,
            stock_available_qty = availability?.Qty
        };
    }

    /// <summary>
    /// Resolves the reference available quantity for each packing-task item from WMS stock.
    /// The source SKU follows the <c>xxxx-1</c>/<c>xxxx-2</c> variant convention: the trailing
    /// <c>-N</c> suffix is ignored and stock is summed across all variants of the same SPU.
    /// </summary>
    private async Task<Dictionary<long, StockAvailability>> ResolveStockAvailabilityAsync(
        List<ErpPackingTaskItemEntity> items,
        long tenantId)
    {
        var result = new Dictionary<long, StockAvailability>();
        if (_wmsDbContext == null)
        {
            return result;
        }

        var commodityIds = items
            .Where(t => t.commodity_id is > 0)
            .Select(t => t.commodity_id!.Value)
            .Distinct()
            .ToList();
        if (commodityIds.Count == 0)
        {
            return result;
        }

        var maps = await _ruoyiDbContext.CommodityMaps.AsNoTracking()
            .Where(t => t.tenant_id == tenantId && commodityIds.Contains(t.erp_commodity_id))
            .Select(t => new { t.erp_commodity_id, t.wms_sku_id })
            .ToListAsync();
        var skuIdByCommodityId = maps
            .Where(t => t.wms_sku_id > 0)
            .GroupBy(t => t.erp_commodity_id)
            .ToDictionary(t => t.Key, t => t.First().wms_sku_id);
        var skuIds = skuIdByCommodityId.Values.Distinct().ToList();
        if (skuIds.Count == 0)
        {
            return result;
        }

        var mappedSkus = await _wmsDbContext.GetDbSet<SkuEntity>().AsNoTracking()
            .Where(t => skuIds.Contains(t.id))
            .Select(t => new { t.id, t.sku_code, t.spu_id })
            .ToListAsync();
        var spuIds = mappedSkus.Select(t => t.spu_id).Distinct().ToList();
        if (spuIds.Count == 0)
        {
            return result;
        }

        var variantSkus = await _wmsDbContext.GetDbSet<SkuEntity>().AsNoTracking()
            .Where(t => spuIds.Contains(t.spu_id))
            .Select(t => new { t.id, t.spu_id })
            .ToListAsync();
        var variantSkuIds = variantSkus.Select(t => t.id).Distinct().ToList();
        var spuIdBySkuId = variantSkus.ToDictionary(t => t.id, t => t.spu_id);

        var stockQtyBySpuId = new Dictionary<int, int>();
        if (variantSkuIds.Count > 0)
        {
            var stockRows = await _wmsDbContext.GetDbSet<StockEntity>().AsNoTracking()
                .Where(t => t.tenant_id == tenantId && !t.is_freeze && variantSkuIds.Contains(t.sku_id))
                .Select(t => new { t.sku_id, t.qty })
                .ToListAsync();
            foreach (var stock in stockRows)
            {
                if (spuIdBySkuId.TryGetValue(stock.sku_id, out var spuId))
                {
                    stockQtyBySpuId[spuId] = stockQtyBySpuId.GetValueOrDefault(spuId) + stock.qty;
                }
            }
        }

        var skuById = mappedSkus.ToDictionary(t => t.id, t => t);
        foreach (var item in items)
        {
            if (item.commodity_id is not long commodityId
                || !skuIdByCommodityId.TryGetValue(commodityId, out var skuId)
                || !skuById.TryGetValue(skuId, out var sku))
            {
                continue;
            }

            var qty = stockQtyBySpuId.TryGetValue(sku.spu_id, out var total) ? total : 0;
            result[item.id] = new StockAvailability(StripVariantSuffix(sku.sku_code), qty);
        }

        return result;
    }

    /// <summary>
    /// Removes a trailing <c>-N</c> variant suffix such as <c>xxxx-1</c> or <c>xxxx-2</c>.
    /// </summary>
    private static string StripVariantSuffix(string skuCode)
    {
        if (string.IsNullOrWhiteSpace(skuCode))
        {
            return skuCode;
        }

        var dashIndex = skuCode.LastIndexOf('-');
        if (dashIndex > 0
            && dashIndex < skuCode.Length - 1
            && skuCode[(dashIndex + 1)..].All(char.IsDigit))
        {
            return skuCode[..dashIndex];
        }

        return skuCode;
    }

    private sealed record StockAvailability(string SkuCode, int Qty);

    /// <summary>
    /// 查询装箱任务明细行可选择的库存：任务绑定仓库内、当前人未锁定（可用>0）、
    /// 当前 SKU 按去掉 -N 变体后缀的基础 SKU 优先匹配；查看更多通过分页加载更多库存。
    /// </summary>
    public async Task<(List<SelectableStockViewModel> data, int totals)> SelectableStockPageAsync(
        PackingTaskStockPageRequest request,
        CurrentUser currentUser)
    {
        if (_wmsDbContext == null)
        {
            return ([], 0);
        }

        var item = await _ruoyiDbContext.PackingTaskItems.AsNoTracking()
            .FirstOrDefaultAsync(t => t.sellfox_item_id == request.sellfox_item_id
                && t.sellfox_task_id == request.sellfox_task_id
                && !t.source_deleted);
        if (item == null)
        {
            return ([], 0);
        }

        var task = await _ruoyiDbContext.PackingTasks.AsNoTracking()
            .FirstOrDefaultAsync(t => t.sellfox_task_id == request.sellfox_task_id
                && !t.source_deleted && !t.source_canceled);
        if (task?.warehouse_id == null)
        {
            return ([], 0);
        }

        var warehouse = await _wmsDbContext.GetDbSet<WarehouseEntity>().AsNoTracking()
            .FirstOrDefaultAsync(t => t.erp_warehouse_id == task.warehouse_id.Value && t.is_valid);
        if (warehouse == null)
        {
            return ([], 0);
        }

        var locationIds = await _wmsDbContext.GetDbSet<GoodslocationEntity>().AsNoTracking()
            .Where(t => t.warehouse_id == warehouse.id && t.is_valid && t.warehouse_area_property != 5)
            .Select(t => t.id)
            .ToListAsync();
        if (locationIds.Count == 0)
        {
            return ([], 0);
        }

        string? baseSkuCode = null;
        int? mappedSkuId = null;
        if (item.commodity_id is long commodityId)
        {
            var map = await _ruoyiDbContext.CommodityMaps.AsNoTracking()
                .FirstOrDefaultAsync(t => t.tenant_id == currentUser.tenant_id && t.erp_commodity_id == commodityId);
            if (map != null && map.wms_sku_id > 0)
            {
                mappedSkuId = map.wms_sku_id;
                var mappedSku = await _wmsDbContext.GetDbSet<SkuEntity>().AsNoTracking()
                    .FirstOrDefaultAsync(t => t.id == map.wms_sku_id);
                if (mappedSku != null)
                {
                    baseSkuCode = StripVariantSuffix(mappedSku.sku_code);
                }
            }
        }

        var stockRows = await (
            from stock in _wmsDbContext.GetDbSet<StockEntity>().AsNoTracking()
            join sku in _wmsDbContext.GetDbSet<SkuEntity>().AsNoTracking() on stock.sku_id equals sku.id
            join spu in _wmsDbContext.GetDbSet<SpuEntity>().AsNoTracking() on sku.spu_id equals spu.id
            join location in _wmsDbContext.GetDbSet<GoodslocationEntity>().AsNoTracking() on stock.goods_location_id equals location.id
            where stock.tenant_id == currentUser.tenant_id && locationIds.Contains(location.id)
            select new
            {
                stock.id,
                stock.sku_id,
                stock.qty,
                stock.is_freeze,
                stock.goods_location_id,
                stock.goods_owner_id,
                stock.series_number,
                stock.expiry_date,
                location.location_name,
                sku.sku_code,
                spu.spu_code,
                spu.spu_name
            })
            .ToListAsync();

        var skuIds = stockRows.Select(t => t.sku_id).Distinct().ToList();
        var locks = await LoadStockLocksAsync(skuIds, locationIds);

        var selections = await _wmsDbContext.GetDbSet<PackingTaskStockSelectionEntity>().AsNoTracking()
            .Where(t => t.tenant_id == currentUser.tenant_id
                && t.sellfox_task_id == request.sellfox_task_id
                && t.sellfox_item_id == request.sellfox_item_id)
            .ToListAsync();
        var selectedQtyByStockId = selections.GroupBy(t => t.stock_id)
            .ToDictionary(t => t.Key, t => t.Sum(x => x.qty));

        var ownerIds = stockRows.Select(t => t.goods_owner_id).Distinct().ToList();
        var ownerNames = ownerIds.Count == 0
            ? new Dictionary<int, string>()
            : await _wmsDbContext.GetDbSet<GoodsownerEntity>().AsNoTracking()
                .Where(t => ownerIds.Contains(t.id))
                .Select(t => new { t.id, t.goods_owner_name })
                .ToDictionaryAsync(t => t.id, t => t.goods_owner_name);
        var imageBySkuId = await LoadCommodityImageMapAsync(skuIds, currentUser.tenant_id);

        var rows = new List<SelectableStockViewModel>();
        foreach (var stock in stockRows)
        {
            var available = stock.is_freeze
                ? 0
                : Math.Max(0, stock.qty
                    - locks.GetValueOrDefault((stock.sku_id, stock.goods_location_id))
                    - selectedQtyByStockId.GetValueOrDefault(stock.id));
            var selected = selectedQtyByStockId.ContainsKey(stock.id);
            if (!selected && available <= 0)
            {
                continue;
            }

            var matched = mappedSkuId != null
                && (stock.sku_id == mappedSkuId
                    || (baseSkuCode != null
                        && string.Equals(StripVariantSuffix(stock.sku_code), baseSkuCode, StringComparison.OrdinalIgnoreCase)));
            rows.Add(new SelectableStockViewModel
            {
                stock_id = stock.id,
                sku_id = stock.sku_id,
                sku_code = stock.sku_code,
                spu_code = stock.spu_code,
                commodity_name = stock.spu_name,
                main_image = imageBySkuId.GetValueOrDefault(stock.sku_id) ?? string.Empty,
                goods_location_id = stock.goods_location_id,
                location_name = stock.location_name,
                warehouse_id = warehouse.id,
                warehouse_name = warehouse.warehouse_name,
                goods_owner_id = stock.goods_owner_id,
                goods_owner_name = ownerNames.GetValueOrDefault(stock.goods_owner_id) ?? string.Empty,
                qty = stock.qty,
                available_qty = available,
                series_number = stock.series_number,
                expiry_date = stock.expiry_date,
                matched = matched,
                selected = selected
            });
        }

        var ordered = rows
            .OrderByDescending(t => t.matched)
            .ThenByDescending(t => t.available_qty)
            .ThenBy(t => t.sku_code)
            .ToList();
        var totals = ordered.Count;
        var pageIndex = Math.Max(request.page_index, 1);
        var pageSize = Math.Clamp(request.page_size, 1, 200);
        var page = ordered.Skip((pageIndex - 1) * pageSize).Take(pageSize).ToList();
        return (page, totals);
    }

    /// <summary>
    /// 保存装箱任务明细行对某个库存行的选择（同任务+明细+库存行唯一，重复选择覆盖数量）。
    /// </summary>
    public async Task<(bool flag, string message)> SelectStockAsync(
        PackingTaskStockSelectRequest request,
        CurrentUser currentUser)
    {
        if (_wmsDbContext == null)
        {
            return (false, "WMS数据库不可用");
        }

        if (request.qty <= 0)
        {
            return (false, "选择数量必须大于0");
        }

        var item = await _ruoyiDbContext.PackingTaskItems.AsNoTracking()
            .FirstOrDefaultAsync(t => t.sellfox_item_id == request.sellfox_item_id
                && t.sellfox_task_id == request.sellfox_task_id
                && !t.source_deleted);
        if (item == null)
        {
            return (false, "装箱任务明细不存在");
        }

        var stock = await _wmsDbContext.GetDbSet<StockEntity>().AsNoTracking()
            .FirstOrDefaultAsync(t => t.id == request.stock_id && t.tenant_id == currentUser.tenant_id);
        if (stock == null)
        {
            return (false, "库存不存在");
        }
        if (stock.is_freeze)
        {
            return (false, "该库存已冻结，不能选择");
        }

        var sku = await _wmsDbContext.GetDbSet<SkuEntity>().AsNoTracking()
            .FirstOrDefaultAsync(t => t.id == stock.sku_id);

        var now = DateTime.Now;
        var dbSet = _wmsDbContext.GetDbSet<PackingTaskStockSelectionEntity>();
        var existing = await dbSet
            .FirstOrDefaultAsync(t => t.tenant_id == currentUser.tenant_id
                && t.sellfox_task_id == request.sellfox_task_id
                && t.sellfox_item_id == request.sellfox_item_id
                && t.stock_id == request.stock_id);
        if (existing == null)
        {
            dbSet.Add(new PackingTaskStockSelectionEntity
            {
                tenant_id = currentUser.tenant_id,
                sellfox_task_id = request.sellfox_task_id,
                sellfox_item_id = request.sellfox_item_id,
                wms_sku_id = stock.sku_id,
                stock_id = request.stock_id,
                qty = request.qty,
                goods_location_id = stock.goods_location_id,
                goods_owner_id = stock.goods_owner_id,
                sku_code = sku?.sku_code ?? string.Empty,
                selected_by = currentUser.user_id,
                selected_by_name = currentUser.user_name,
                create_time = now,
                last_update_time = now
            });
        }
        else
        {
            existing.wms_sku_id = stock.sku_id;
            existing.qty = request.qty;
            existing.goods_location_id = stock.goods_location_id;
            existing.goods_owner_id = stock.goods_owner_id;
            existing.sku_code = sku?.sku_code ?? string.Empty;
            existing.selected_by = currentUser.user_id;
            existing.selected_by_name = currentUser.user_name;
            existing.last_update_time = now;
        }

        await _wmsDbContext.SaveChangesAsync();
        return (true, "库存选择成功");
    }

    private async Task<Dictionary<(int SkuId, int LocationId), int>> LoadStockLocksAsync(
        List<int> skuIds,
        List<int> locationIds)
    {
        var locks = new Dictionary<(int, int), int>();
        if (skuIds.Count == 0 || locationIds.Count == 0)
        {
            return locks;
        }

        var dispatchRows = await (
            from detail in _wmsDbContext!.GetDbSet<DispatchlistEntity>().AsNoTracking()
            join pick in _wmsDbContext!.GetDbSet<DispatchpicklistEntity>().AsNoTracking()
                on detail.id equals pick.dispatchlist_id
            where detail.dispatch_status > 1 && detail.dispatch_status < 6
                && skuIds.Contains(pick.sku_id) && locationIds.Contains(pick.goods_location_id)
            group pick by new { pick.sku_id, pick.goods_location_id } into g
            select new { g.Key.sku_id, g.Key.goods_location_id, qty = g.Sum(t => t.pick_qty) })
            .ToListAsync();
        AddLocks(locks, dispatchRows.Select(t => (t.sku_id, t.goods_location_id, t.qty)));

        var processRows = await _wmsDbContext!.GetDbSet<StockprocessdetailEntity>().AsNoTracking()
            .Where(t => !t.is_update_stock && skuIds.Contains(t.sku_id) && locationIds.Contains(t.goods_location_id))
            .GroupBy(t => new { t.sku_id, t.goods_location_id })
            .Select(g => new { g.Key.sku_id, g.Key.goods_location_id, qty = g.Sum(t => t.qty) })
            .ToListAsync();
        AddLocks(locks, processRows.Select(t => (t.sku_id, t.goods_location_id, t.qty)));

        var moveRows = await _wmsDbContext!.GetDbSet<StockmoveEntity>().AsNoTracking()
            .Where(t => t.move_status == 0 && skuIds.Contains(t.sku_id) && locationIds.Contains(t.orig_goods_location_id))
            .GroupBy(t => new { t.sku_id, t.orig_goods_location_id })
            .Select(g => new { g.Key.sku_id, location_id = g.Key.orig_goods_location_id, qty = g.Sum(t => t.qty) })
            .ToListAsync();
        AddLocks(locks, moveRows.Select(t => (t.sku_id, t.location_id, t.qty)));

        return locks;
    }

    private static void AddLocks(
        Dictionary<(int, int), int> locks,
        IEnumerable<(int SkuId, int LocationId, int Qty)> rows)
    {
        foreach (var (skuId, locationId, qty) in rows)
        {
            locks[(skuId, locationId)] = locks.GetValueOrDefault((skuId, locationId)) + qty;
        }
    }

    private async Task<Dictionary<int, string>> LoadCommodityImageMapAsync(List<int> skuIds, long tenantId)
    {
        var result = new Dictionary<int, string>();
        if (skuIds.Count == 0)
        {
            return result;
        }

        var maps = await _ruoyiDbContext.CommodityMaps.AsNoTracking()
            .Where(t => t.tenant_id == tenantId && skuIds.Contains(t.wms_sku_id))
            .Select(t => new { t.wms_sku_id, t.erp_commodity_id })
            .ToListAsync();
        var commodityIds = maps.Select(t => t.erp_commodity_id.ToString()).Distinct().ToList();
        var images = commodityIds.Count == 0
            ? []
            : await _ruoyiDbContext.Commodities.AsNoTracking()
                .Where(t => commodityIds.Contains(t.id) && !string.IsNullOrEmpty(t.img_url))
                .Select(t => new { t.id, t.img_url })
                .ToListAsync();
        foreach (var map in maps)
        {
            var image = images.FirstOrDefault(t => t.id == map.erp_commodity_id.ToString());
            if (image?.img_url != null && !result.ContainsKey(map.wms_sku_id))
            {
                result[map.wms_sku_id] = image.img_url;
            }
        }

        return result;
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
