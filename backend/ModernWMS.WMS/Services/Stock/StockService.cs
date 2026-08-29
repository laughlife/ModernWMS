using Dapper;
using Microsoft.Extensions.Localization;
using ModernWMS.Core.Database;
using ModernWMS.Core.JWT;
using ModernWMS.Core.Models;
using ModernWMS.Core.Services;
using ModernWMS.Core.Utility;
using ModernWMS.WMS.Entities.Models;
using ModernWMS.WMS.Entities.ViewModels;
using ModernWMS.WMS.Entities.ViewModels.Stock;
using ModernWMS.WMS.IServices;

namespace ModernWMS.WMS.Services;

/// <summary>
/// 提供库存汇总、库位库存、安全库存及库存统计查询。
/// </summary>
public class StockService : BaseService<StockEntity>, IStockService
{
    private readonly IMySqlConnectionFactory _connectionFactory;
    private readonly IStringLocalizer<ModernWMS.Core.MultiLanguage> _stringLocalizer;

    private static readonly IReadOnlyDictionary<string, string> StockColumns = Columns(
        "spu_code", "spu_name", "sku_code", "sku_id", "qty", "qty_available", "qty_locked",
        "qty_asn", "qty_to_unload", "qty_to_sort", "qty_sorted", "shortage_qty", "expiry_date");
    private static readonly IReadOnlyDictionary<string, string> LocationColumns = Columns(
        "warehouse_name", "warehouse_id", "warehouse_area_id", "warehouse_area_name", "location_name",
        "spu_code", "spu_name", "sku_id", "sku_code", "sku_name", "qty", "qty_available", "qty_locked",
        "goods_owner_name", "series_number", "goods_location_id", "expiry_date", "price", "putaway_date",
        "erp_stock_id", "stock_allocation_id", "inventory_mode", "location_state", "is_pending_location");
    private static readonly IReadOnlyDictionary<string, string> SafetyColumns = Columns(
        "warehouse_name", "spu_code", "spu_name", "sku_code", "sku_name", "sku_id", "qty",
        "qty_available", "qty_locked", "qty_frozen", "safety_stock_qty");
    private static readonly IReadOnlyDictionary<string, string> StockSelectColumns = Columns(
        "id", "sku_id", "goods_location_id", "qty", "goods_owner_id", "is_freeze", "last_update_time",
        "warehouse_name", "location_name", "spu_code", "spu_name", "sku_code", "sku_name",
        "unit", "qty_available", "goods_owner_name", "series_number", "expiry_date", "price", "putaway_date",
        "erp_stock_id", "stock_allocation_id", "inventory_mode", "location_state", "is_pending_location");
    private static readonly IReadOnlyDictionary<string, string> SkuColumns = Columns(
        "sku_id", "spu_id", "spu_code", "spu_name", "sku_code", "sku_name", "supplier_id",
        "supplier_name", "brand", "origin", "unit");

    /// <summary>
    /// 初始化库存服务。
    /// </summary>
    /// <param name="connectionFactory">MySQL 连接工厂。</param>
    /// <param name="stringLocalizer">多语言文本提供器。</param>
    public StockService(IMySqlConnectionFactory connectionFactory,
        IStringLocalizer<ModernWMS.Core.MultiLanguage> stringLocalizer)
    {
        _connectionFactory = connectionFactory;
        _stringLocalizer = stringLocalizer;
    }

    /// <inheritdoc />
    public async Task<(List<StockManagementViewModel> data, int totals)> StockPageAsync(PageSearch page, CurrentUser user)
    {
        var filter = DapperSearchBuilder.Build(page.searchObjects.Where(x =>
            !string.Equals(x.Name, "product_keyword", StringComparison.OrdinalIgnoreCase)), StockColumns);
        var keyword = page.searchObjects.FirstOrDefault(x =>
            string.Equals(x.Name, "product_keyword", StringComparison.OrdinalIgnoreCase))?.Text?.Trim() ?? "";
        AddPage(filter.Parameters, page);
        filter.Parameters.Add("keyword", $"%{EscapeLike(keyword)}%");
        var where = "(q.`qty_asn`>0 OR q.`qty`>0)";
        if (keyword.Length > 0) where += " AND (q.`spu_name` LIKE @keyword ESCAPE '!' OR q.`sku_code` LIKE @keyword ESCAPE '!')";
        if (filter.Sql.Length > 0) where += " AND " + filter.Sql;
        var result = await QueryPageAsync<StockManagementViewModel>(StockSummaryCte, StockSummarySelect, where, "q.`sku_code`", filter.Parameters);
        await PopulateProductImagesAsync(result.data, x => x.sku_id, (x, url) => x.product_image = url);
        return result;
    }

    /// <inheritdoc />
    public async Task<(List<LocationStockManagementViewModel> data, int totals)> LocationStockPageAsync(PageSearch page, CurrentUser user)
    {
        var memberFilter = page.searchObjects.FirstOrDefault(x =>
            string.Equals(x.Name, "member_id", StringComparison.OrdinalIgnoreCase));
        var columnFilters = page.searchObjects.Where(x =>
            !string.Equals(x.Name, "member_id", StringComparison.OrdinalIgnoreCase)).ToList();
        var filter = DapperSearchBuilder.Build(columnFilters, LocationColumns);
        AddPage(filter.Parameters, page);
        var where = "q.`qty`>0" + (filter.Sql.Length == 0 ? "" : " AND " + filter.Sql);

        if (memberFilter != null && long.TryParse(memberFilter.Text, out var memberId) && memberId > 0)
        {
            await using var connection = await _connectionFactory.OpenConnectionAsync();
            var groupIds = (await connection.QueryAsync<long>(MemberOperatorGroupsCte, new { member_id = memberId })).AsList();
            if (groupIds.Count > 0)
            {
                filter.Parameters.Add("groupIds", groupIds);
                where += """
                     AND q.`warehouse_area_id` IN (
                        SELECT b.`warehouse_area_id` FROM `wms_warehousearea_operator_group` b
                        WHERE b.`dept_id` IN @groupIds
                     )
                    """;
            }
            else
            {
                where += " AND 1=0";
            }
        }

        var result = await QueryPageAsync<LocationStockManagementViewModel>(LocationInventoryCte, LocationInventorySelect, where, "q.`sku_code`", filter.Parameters);
        await PopulateProductImagesAsync(result.data, x => x.sku_id, (x, url) => x.product_image = url);
        return result;
    }

    /// <inheritdoc />
    public async Task<(List<SafetyStockManagementViewModel> data, int totals)> SafetyStockPageAsync(PageSearch page, CurrentUser user)
    {
        var filter = DapperSearchBuilder.Build(page.searchObjects, SafetyColumns);
        AddPage(filter.Parameters, page);
        return await QueryPageAsync<SafetyStockManagementViewModel>(SafetyCte, SafetySelect,
            filter.Sql.Length == 0 ? "1=1" : filter.Sql, "q.`sku_code`", filter.Parameters);
    }

    /// <inheritdoc />
    public async Task<(List<StockViewModel> data, int totals)> SelectPageAsync(PageSearch page, CurrentUser user)
    {
        var filter = DapperSearchBuilder.Build(page.searchObjects, StockSelectColumns);
        AddPage(filter.Parameters, page);
        var clauses = new List<string>();
        if (page.sqlTitle == "") clauses.Add("(q.`qty_available`>0 OR q.`qty_pending_location`>0)");
        else if (page.sqlTitle == "frozen") clauses.Add("q.`is_freeze`=1");
        if (filter.Sql.Length > 0) clauses.Add(filter.Sql);
        return await QueryPageAsync<StockViewModel>(UnifiedInventoryCte, StockSelectSql,
            clauses.Count == 0 ? "1=1" : string.Join(" AND ", clauses), "q.`sku_code`", filter.Parameters);
    }

    /// <inheritdoc />
    public async Task<(List<SkuSelectViewModel> data, int totals)> SkuSelectPageAsync(PageSearch page, CurrentUser user)
    {
        var filter = DapperSearchBuilder.Build(page.searchObjects, SkuColumns);
        AddPage(filter.Parameters, page);
        const string select = """
            SELECT sku.`spu_id`,sku.`sku_code`,sku.`sku_name`,sku.`unit`,spu.`spu_code`,spu.`spu_name`,
                   spu.`supplier_id`,spu.`supplier_name`,spu.`brand`,spu.`origin`,sku.`id` sku_id
            FROM `wms_sku` sku JOIN `wms_spu` spu ON spu.`id`=sku.`spu_id`
            """;
        return await QueryPageAsync<SkuSelectViewModel>("", select,
            filter.Sql.Length == 0 ? "1=1" : filter.Sql, "q.`sku_code`", filter.Parameters);
    }

    /// <inheritdoc />
    public async Task<List<LocationStockManagementViewModel>> LocationStockForPhoneAsync(LocationStockForPhoneSearchViewModel input, CurrentUser user)
    {
        var p = new DynamicParameters(new
        {
            spuName = $"%{EscapeLike(input.spu_name)}%", locationName = $"%{EscapeLike(input.location_name)}%", input.series_number
        });
        await using var connection = await _connectionFactory.OpenConnectionAsync();
        return (await connection.QueryAsync<LocationStockManagementViewModel>(
            UnifiedInventoryCte + PhoneInventorySelect + " ORDER BY `sku_code`;", p)).AsList();
    }

    /// <inheritdoc />
    public async Task<(List<DeliveryStatisticViewModel> datas, int totals)> DeliveryStatistic(DeliveryStatisticSearchViewModel input, CurrentUser user)
    {
        var p = new DynamicParameters(new
        {
            skuName = Like(input.sku_name), skuCode = Like(input.sku_code), warehouseName = Like(input.warehouse_name),
            input.delivery_date_from, input.delivery_date_to, minDate = UtilConvert.MinDate,
            offset = (input.pageIndex - 1) * input.pageSize, pageSize = input.pageSize
        });
        const string select = """
            SELECT d.`dispatch_no`,COALESCE(wh_location.`warehouse_name`,wh_erp.`warehouse_name`,'') `warehouse_name`,
                   COALESCE(gl.`location_name`,'') `location_name`,spu.`spu_name`,spu.`spu_code`,sku.`sku_name`,sku.`sku_code`,
                   p.`series_number`,p.`price`,p.`expiry_date`,p.`putaway_date`,d.`create_time` delivery_date,go.`goods_owner_name`,
                   SUM(p.`picked_qty`) delivery_qty,SUM(p.`picked_qty`*sku.`price`) delivery_amount
            FROM `wms_dispatchlist` d JOIN `wms_dispatchpicklist` p ON p.`dispatchlist_id`=d.`id`
            JOIN `wms_sku` sku ON sku.`id`=d.`sku_id` JOIN `wms_spu` spu ON spu.`id`=sku.`spu_id`
            LEFT JOIN `wms_goodslocation` gl ON gl.`id`=p.`goods_location_id`
            LEFT JOIN `wms_warehouse` wh_location ON wh_location.`id`=gl.`warehouse_id`
            LEFT JOIN `wms_erp_stock_allocation` allocation ON allocation.`id`=p.`stock_allocation_id`
            LEFT JOIN `trk_stock` stock ON stock.`id`=COALESCE(p.`erp_stock_id`,allocation.`erp_stock_id`) AND stock.`deleted`=b'0'
            LEFT JOIN `wms_warehouse` wh_erp ON wh_erp.`erp_warehouse_id`=stock.`warehouse_id` AND wh_erp.`is_valid`=1
            JOIN `wms_goodsowner` go ON go.`id`=p.`goods_owner_id`
            WHERE d.`dispatch_status`>=6
              AND (@delivery_date_from=@minDate OR d.`create_time`>=@delivery_date_from) AND (@delivery_date_to=@minDate OR d.`create_time`<=@delivery_date_to)
              AND spu.`spu_name` LIKE @spuName ESCAPE '!' AND spu.`spu_code` LIKE @spuCode ESCAPE '!'
              AND sku.`sku_name` LIKE @skuName ESCAPE '!' AND sku.`sku_code` LIKE @skuCode ESCAPE '!'
              AND COALESCE(wh_location.`warehouse_name`,wh_erp.`warehouse_name`,'') LIKE @warehouseName ESCAPE '!'
            GROUP BY d.`dispatch_no`,COALESCE(wh_location.`warehouse_name`,wh_erp.`warehouse_name`,''),COALESCE(gl.`location_name`,''),
                     spu.`spu_name`,spu.`spu_code`,sku.`sku_name`,sku.`sku_code`,
                     p.`series_number`,p.`price`,p.`expiry_date`,p.`putaway_date`,d.`create_time`,p.`goods_owner_id`,go.`goods_owner_name`
            """;
        return await QueryPageAsync<DeliveryStatisticViewModel>("", select, "1=1", "q.`delivery_date` DESC", p);
    }

    /// <inheritdoc />
    public async Task<(List<StockAgeViewModel> data, int totals)> StockAgePageAsync(StockAgeSearchViewModel input, CurrentUser user)
    {
        var p = new DynamicParameters(new
        {
            skuCode = Like(input.sku_code), warehouseName = Like(input.warehouse_name), input.expiry_date_from, input.expiry_date_to,
            input.stock_age_from, input.stock_age_to, minDate = UtilConvert.MinDate,
            today = DateTime.Today,
            offset = (input.pageIndex - 1) * input.pageSize, pageSize = input.pageSize
        });
        const string select = """
            SELECT i.`erp_stock_id`,i.`stock_allocation_id`,i.`inventory_mode`,i.`location_state`,i.`is_pending_location`,
                   i.`sku_id`,i.`goods_owner_name`,i.`spu_name`,i.`spu_code`,i.`sku_code`,i.`sku_name`,i.`qty`,
                   i.`location_name`,i.`warehouse_name`,i.`series_number`,i.`expiry_date`,i.`price`,i.`putaway_date`,i.`goods_location_id`,
                   CASE WHEN i.`putaway_date`=@minDate THEN 0 ELSE DATEDIFF(@today,DATE(i.`putaway_date`)) END stock_age
            FROM inventory_detail i
            WHERE (@expiry_date_from=@minDate OR i.`expiry_date`>=@expiry_date_from)
              AND (@expiry_date_to=@minDate OR i.`expiry_date`<=@expiry_date_to)
              AND i.`spu_name` LIKE @spuName ESCAPE '!' AND i.`sku_name` LIKE @skuName ESCAPE '!'
              AND i.`sku_code` LIKE @skuCode ESCAPE '!' AND i.`spu_code` LIKE @spuCode ESCAPE '!' AND i.`warehouse_name` LIKE @warehouseName ESCAPE '!'
            """;
        const string where = "q.`qty`>0 AND (@stock_age_from<=0 OR q.`stock_age`>=@stock_age_from) AND (@stock_age_to<=0 OR q.`stock_age`<=@stock_age_to)";
        return await QueryPageAsync<StockAgeViewModel>(UnifiedInventoryCte, select, where, "q.`sku_code`", p);
    }

    private async Task PopulateProductImagesAsync<T>(List<T> rows, Func<T, int> skuId, Action<T, string> setImage)
    {
        if (rows.Count == 0) return;
        await using var connection = await _connectionFactory.OpenConnectionAsync();
        var images = await connection.QueryAsync<ProductImageRow>("""
            SELECT m.`wms_sku_id`,c.`img_url` FROM `wms_erp_commodity_map` m
            JOIN `erp_commodity` c ON c.`id`=CAST(m.`erp_commodity_id` AS CHAR)
            WHERE m.`wms_sku_id` IN @skuIds AND c.`img_url` IS NOT NULL AND c.`img_url`<>'';
            """, new { skuIds = rows.Select(skuId).Distinct().ToArray() });
        var lookup = images.GroupBy(x => x.wms_sku_id).ToDictionary(x => x.Key, x => x.First().img_url);
        foreach (var row in rows) if (lookup.TryGetValue(skuId(row), out var url)) setImage(row, url);
    }

    private async Task<(List<T> data, int totals)> QueryPageAsync<T>(string cte, string select, string where, string order, DynamicParameters p)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync();
        using var result = await connection.QueryMultipleAsync($"""
            {cte} SELECT COUNT(*) FROM ({select}) q WHERE {where};
            {cte} SELECT q.* FROM ({select}) q WHERE {where} ORDER BY {order} LIMIT @pageSize OFFSET @offset;
            """, p);
        var total = await result.ReadSingleAsync<int>();
        return ((await result.ReadAsync<T>()).AsList(), total);
    }

    private static void AddPage(DynamicParameters p, PageSearch page)
    {
        p.Add("offset", (page.pageIndex - 1) * page.pageSize); p.Add("pageSize", page.pageSize);
    }
    private static IReadOnlyDictionary<string, string> Columns(params string[] names) =>
        names.ToDictionary(x => x, x => $"q.`{x}`", StringComparer.OrdinalIgnoreCase);
    private static string Like(string value) => $"%{EscapeLike(value)}%";
    private static string EscapeLike(string value) => value.Replace("!", "!!").Replace("%", "!%").Replace("_", "!_");

    private const string UnifiedInventoryCte = """
        WITH inventory_detail AS (
          SELECT 0 id,stock.`id` erp_stock_id,NULL stock_allocation_id,
                 'ERP_STOCK' inventory_mode,'DIRECT' location_state,
                 FALSE is_pending_location,TRUE allocation_consistent,
                 COALESCE(map.`wms_sku_id`,0) sku_id,0 goods_location_id,0 goods_owner_id,
                 0 warehouse_area_id,'' warehouse_area_name,COALESCE(wh.`id`,0) warehouse_id,
                 stock.`warehouse_name`,'' location_name,
                 COALESCE(spu.`spu_code`,stock.`commodity_sku`) spu_code,
                 COALESCE(spu.`spu_name`,stock.`commodity_name`) spu_name,
                 COALESCE(sku.`sku_code`,stock.`commodity_sku`) sku_code,
                 COALESCE(sku.`sku_name`,stock.`commodity_name`) sku_name,
                 COALESCE(sku.`unit`,'') unit,stock.`order_user_name` goods_owner_name,
                 '' series_number,TIMESTAMP('1970-01-01') expiry_date,0 price,
                 TIMESTAMP('1970-01-01') putaway_date,
                 stock.`total_qty` qty,stock.`available_qty` qty_available,
                 stock.`occupied_qty` qty_locked,0 qty_frozen,0 qty_pending_location,
                 stock.`total_qty` erp_total_qty,stock.`available_qty` erp_available_qty,
                 stock.`occupied_qty` erp_occupied_qty,FALSE is_freeze,
                 stock.`update_time` last_update_time
            FROM `trk_stock` stock
            LEFT JOIN `wms_erp_commodity_map` map ON map.`erp_commodity_id`=stock.`commodity_id`
            LEFT JOIN `wms_sku` sku ON sku.`id`=map.`wms_sku_id`
            LEFT JOIN `wms_spu` spu ON spu.`id`=sku.`spu_id`
            LEFT JOIN `wms_warehouse` wh
              ON wh.`erp_warehouse_id`=stock.`warehouse_id` AND wh.`is_valid`=1
           WHERE stock.`deleted`=b'0'
        )
        """;

    private const string StockSelectSql = """
        SELECT i.`id`,i.`erp_stock_id`,i.`stock_allocation_id`,i.`inventory_mode`,i.`location_state`,i.`is_pending_location`,
          i.`allocation_consistent`,i.`sku_id`,i.`spu_name`,i.`spu_code`,i.`sku_code`,i.`sku_name`,i.`qty_available`,i.`qty_locked`,i.`qty`,
          i.`goods_location_id`,i.`goods_owner_id`,i.`location_name`,i.`warehouse_name`,i.`series_number`,i.`expiry_date`,i.`price`,i.`putaway_date`,
          i.`is_freeze`,i.`last_update_time`,i.`unit`,i.`goods_owner_name`,i.`erp_total_qty`,i.`erp_available_qty`,i.`erp_occupied_qty`
        FROM inventory_detail i
        """;

    private const string LocationInventoryCte = UnifiedInventoryCte;

    private const string LocationInventorySelect = """
        SELECT i.`erp_stock_id`,i.`stock_allocation_id`,i.`inventory_mode`,i.`location_state`,i.`is_pending_location`,i.`allocation_consistent`,
          i.`sku_id`,i.`goods_owner_name`,i.`spu_name`,i.`spu_code`,i.`sku_code`,i.`sku_name`,i.`qty_available`,i.`qty_locked`,i.`qty`,
          i.`location_name`,i.`warehouse_area_id`,i.`warehouse_area_name`,i.`warehouse_id`,i.`warehouse_name`,i.`series_number`,
          i.`expiry_date`,i.`price`,i.`putaway_date`,i.`goods_location_id`,i.`erp_total_qty`,i.`erp_available_qty`,i.`erp_occupied_qty`
        FROM inventory_detail i
        """;

    private const string MemberOperatorGroupsCte = """
        WITH RECURSIVE ancestors AS (
            SELECT d.`id`, d.`parent_id`, d.`dept`, 0 AS depth
            FROM `system_dept` d
            JOIN `system_users` u ON u.`dept_id` = d.`id`
            WHERE u.`id`=@member_id AND u.`deleted`=0
            UNION ALL
            SELECT p.`id`, p.`parent_id`, p.`dept`, a.depth + 1
            FROM `system_dept` p
            JOIN ancestors a ON p.`id` = a.`parent_id`
            WHERE p.`deleted`=0 AND a.`parent_id`<>0 AND a.depth < 20
        )
        SELECT DISTINCT `id` FROM ancestors WHERE `dept`='operator';
        """;

    private const string PhoneInventorySelect = """
        SELECT i.`erp_stock_id`,i.`stock_allocation_id`,i.`inventory_mode`,i.`location_state`,i.`is_pending_location`,i.`allocation_consistent`,
          i.`sku_id`,i.`goods_owner_name`,i.`spu_name`,i.`spu_code`,i.`sku_code`,i.`sku_name`,i.`qty_available`,i.`qty_locked`,i.`qty`,
          i.`location_name`,i.`warehouse_id`,i.`warehouse_name`,i.`warehouse_area_id`,i.`warehouse_area_name`,i.`series_number`,
          i.`expiry_date`,i.`price`,i.`putaway_date`,i.`goods_location_id`,i.`erp_total_qty`,i.`erp_available_qty`,i.`erp_occupied_qty`
        FROM inventory_detail i
        WHERE (@sku_id=0 OR i.`sku_id`=@sku_id) AND (@goods_location_id=0 OR i.`goods_location_id`=@goods_location_id)
          AND (@warehouse_id=0 OR i.`warehouse_id`=@warehouse_id) AND i.`spu_name` LIKE @spuName ESCAPE '!'
          AND i.`location_name` LIKE @locationName ESCAPE '!' AND (@series_number='' OR i.`series_number`=@series_number)
        """;

    private const string StockSummaryCte = UnifiedInventoryCte + """
        , stock_group AS (
          SELECT i.`sku_id`,SUM(i.`qty`) qty,SUM(i.`qty_available`) qty_available,SUM(i.`qty_locked`) qty_locked,
                 SUM(i.`qty_pending_location`) qty_pending_location,MIN(i.`allocation_consistent`) allocation_consistent
          FROM inventory_detail i GROUP BY i.`sku_id`
        ), canonical_stock_unique AS (
          SELECT i.`erp_stock_id`,i.`sku_id`,MAX(i.`erp_total_qty`) erp_total_qty,
                 MAX(i.`erp_available_qty`) erp_available_qty,MAX(i.`erp_occupied_qty`) erp_occupied_qty
          FROM inventory_detail i WHERE i.`inventory_mode`='ERP_STOCK'
          GROUP BY i.`erp_stock_id`,i.`sku_id`
        ), erp_group AS (
          SELECT sku_id,SUM(erp_total_qty) erp_total_qty,SUM(erp_available_qty) erp_available_qty,
                 SUM(erp_occupied_qty) erp_occupied_qty FROM canonical_stock_unique GROUP BY sku_id
        ), asn_group AS (
          SELECT a.`sku_id`,SUM(CASE WHEN a.`asn_status`=0 THEN a.`asn_qty` ELSE 0 END) qty_asn,SUM(CASE WHEN a.`asn_status`=1 THEN a.`asn_qty` ELSE 0 END) qty_to_unload,
            SUM(CASE WHEN a.`asn_status`=2 THEN a.`asn_qty` ELSE 0 END) qty_to_sort,SUM(CASE WHEN a.`asn_status`=3 THEN a.`sorted_qty` ELSE 0 END) qty_sorted,
            SUM(CASE WHEN a.`asn_status`=4 THEN a.`shortage_qty` ELSE 0 END) shortage_qty FROM `wms_asn` a GROUP BY a.`sku_id`
        )
        """;

    private const string StockSummarySelect = """
        SELECT sku.`id` sku_id,spu.`spu_name`,spu.`spu_code`,sku.`sku_code`,COALESCE(ag.`qty_asn`,0) qty_asn,
          COALESCE(sg.`qty_available`,0) qty_available,COALESCE(sg.`qty_locked`,0) qty_locked,
          COALESCE(ag.`qty_sorted`,0) qty_sorted,COALESCE(ag.`qty_to_sort`,0) qty_to_sort,COALESCE(ag.`shortage_qty`,0) shortage_qty,
          COALESCE(ag.`qty_to_unload`,0) qty_to_unload,COALESCE(sg.`qty`,0) qty,COALESCE(sg.`qty_pending_location`,0) qty_pending_location,
          COALESCE(eg.`erp_total_qty`,0) erp_total_qty,COALESCE(eg.`erp_available_qty`,0) erp_available_qty,
          COALESCE(eg.`erp_occupied_qty`,0) erp_occupied_qty,COALESCE(sg.`allocation_consistent`,TRUE) allocation_consistent
        FROM `wms_sku` sku LEFT JOIN asn_group ag ON ag.`sku_id`=sku.`id` LEFT JOIN stock_group sg ON sg.`sku_id`=sku.`id`
        LEFT JOIN erp_group eg ON eg.`sku_id`=sku.`id`
        JOIN `wms_spu` spu ON spu.`id`=sku.`spu_id`
        """;

    private const string SafetyCte = UnifiedInventoryCte + """
        , stock_group AS (
          SELECT i.`sku_id`,i.`warehouse_id`,MAX(i.`warehouse_name`) warehouse_name,SUM(i.`qty`) qty,
                 SUM(i.`qty_available`) qty_available,SUM(i.`qty_locked`) qty_locked,SUM(i.`qty_frozen`) qty_frozen,
                 SUM(i.`qty_pending_location`) qty_pending_location,MIN(i.`allocation_consistent`) allocation_consistent
          FROM inventory_detail i GROUP BY i.`sku_id`,i.`warehouse_id`
        ), canonical_stock_unique AS (
          SELECT i.`erp_stock_id`,i.`sku_id`,i.`warehouse_id`,MAX(i.`erp_total_qty`) erp_total_qty,
                 MAX(i.`erp_available_qty`) erp_available_qty,MAX(i.`erp_occupied_qty`) erp_occupied_qty
          FROM inventory_detail i WHERE i.`inventory_mode`='ERP_STOCK'
          GROUP BY i.`erp_stock_id`,i.`sku_id`,i.`warehouse_id`
        ), erp_group AS (
          SELECT sku_id,warehouse_id,SUM(erp_total_qty) erp_total_qty,SUM(erp_available_qty) erp_available_qty,
                 SUM(erp_occupied_qty) erp_occupied_qty FROM canonical_stock_unique GROUP BY sku_id,warehouse_id
        )
        """;

    private const string SafetySelect = """
        SELECT sg.`sku_id`,spu.`spu_name`,spu.`spu_code`,sku.`sku_code`,sku.`sku_name`,
          sg.`qty_available`,sg.`qty_frozen`,sg.`qty_locked`,sg.`qty`,sg.`qty_pending_location`,sg.`warehouse_name`,
          COALESCE(eg.`erp_total_qty`,0) erp_total_qty,COALESCE(eg.`erp_available_qty`,0) erp_available_qty,
          COALESCE(eg.`erp_occupied_qty`,0) erp_occupied_qty,sg.`allocation_consistent`,COALESCE(sss.`safety_stock_qty`,0) safety_stock_qty
        FROM stock_group sg
        JOIN `wms_sku` sku ON sku.`id`=sg.`sku_id` JOIN `wms_spu` spu ON spu.`id`=sku.`spu_id`
        LEFT JOIN erp_group eg ON eg.`sku_id`=sg.`sku_id` AND eg.`warehouse_id`=sg.`warehouse_id`
        LEFT JOIN `wms_sku_safety_stock` sss ON sss.`sku_id`=sg.`sku_id` AND sss.`warehouse_id`=sg.`warehouse_id`
        """;

    private sealed class ProductImageRow
    {
        public int wms_sku_id { get; set; }
        public string img_url { get; set; } = "";
    }
}
