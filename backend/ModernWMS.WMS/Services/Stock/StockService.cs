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
        "goods_owner_name", "series_number", "goods_location_id", "expiry_date", "price", "putaway_date");
    private static readonly IReadOnlyDictionary<string, string> SafetyColumns = Columns(
        "warehouse_name", "spu_code", "spu_name", "sku_code", "sku_name", "sku_id", "qty",
        "qty_available", "qty_locked", "qty_frozen", "safety_stock_qty");
    private static readonly IReadOnlyDictionary<string, string> StockSelectColumns = Columns(
        "id", "sku_id", "goods_location_id", "qty", "goods_owner_id", "is_freeze", "last_update_time",
        "tenant_id", "warehouse_name", "location_name", "spu_code", "spu_name", "sku_code", "sku_name",
        "unit", "qty_available", "goods_owner_name", "series_number", "expiry_date", "price", "putaway_date");
    private static readonly IReadOnlyDictionary<string, string> SkuColumns = Columns(
        "sku_id", "spu_id", "spu_code", "spu_name", "sku_code", "sku_name", "supplier_id",
        "supplier_name", "brand", "origin", "unit");

    public StockService(IMySqlConnectionFactory connectionFactory,
        IStringLocalizer<ModernWMS.Core.MultiLanguage> stringLocalizer)
    {
        _connectionFactory = connectionFactory;
        _stringLocalizer = stringLocalizer;
    }

    public async Task<(List<StockManagementViewModel> data, int totals)> StockPageAsync(PageSearch page, CurrentUser user)
    {
        var filter = DapperSearchBuilder.Build(page.searchObjects.Where(x =>
            !string.Equals(x.Name, "product_keyword", StringComparison.OrdinalIgnoreCase)), StockColumns);
        var keyword = page.searchObjects.FirstOrDefault(x =>
            string.Equals(x.Name, "product_keyword", StringComparison.OrdinalIgnoreCase))?.Text?.Trim() ?? "";
        AddPage(filter.Parameters, page, user.tenant_id);
        filter.Parameters.Add("keyword", $"%{EscapeLike(keyword)}%");
        var where = "(q.`qty_asn`>0 OR q.`qty`>0)";
        if (keyword.Length > 0) where += " AND (q.`spu_name` LIKE @keyword ESCAPE '!' OR q.`sku_code` LIKE @keyword ESCAPE '!')";
        if (filter.Sql.Length > 0) where += " AND " + filter.Sql;
        var result = await QueryPageAsync<StockManagementViewModel>(StockSummaryCte, StockSummarySelect, where, "q.`sku_code`", filter.Parameters);
        await PopulateProductImagesAsync(result.data, user.tenant_id, x => x.sku_id, (x, url) => x.product_image = url);
        return result;
    }

    public async Task<(List<LocationStockManagementViewModel> data, int totals)> LocationStockPageAsync(PageSearch page, CurrentUser user)
    {
        var memberFilter = page.searchObjects.FirstOrDefault(x =>
            string.Equals(x.Name, "member_id", StringComparison.OrdinalIgnoreCase));
        var columnFilters = page.searchObjects.Where(x =>
            !string.Equals(x.Name, "member_id", StringComparison.OrdinalIgnoreCase)).ToList();
        var filter = DapperSearchBuilder.Build(columnFilters, LocationColumns);
        AddPage(filter.Parameters, page, user.tenant_id);
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
                        WHERE b.`tenant_id`=@tenantId AND b.`dept_id` IN @groupIds
                     )
                    """;
            }
            else
            {
                where += " AND 1=0";
            }
        }

        var result = await QueryPageAsync<LocationStockManagementViewModel>(LocationInventoryCte, LocationInventorySelect, where, "q.`sku_code`", filter.Parameters);
        await PopulateProductImagesAsync(result.data, user.tenant_id, x => x.sku_id, (x, url) => x.product_image = url);
        return result;
    }

    public async Task<(List<SafetyStockManagementViewModel> data, int totals)> SafetyStockPageAsync(PageSearch page, CurrentUser user)
    {
        var filter = DapperSearchBuilder.Build(page.searchObjects, SafetyColumns);
        AddPage(filter.Parameters, page, user.tenant_id);
        return await QueryPageAsync<SafetyStockManagementViewModel>(SafetyCte, SafetySelect,
            filter.Sql.Length == 0 ? "1=1" : filter.Sql, "q.`sku_code`", filter.Parameters);
    }

    public async Task<(List<StockViewModel> data, int totals)> SelectPageAsync(PageSearch page, CurrentUser user)
    {
        var filter = DapperSearchBuilder.Build(page.searchObjects, StockSelectColumns);
        AddPage(filter.Parameters, page, user.tenant_id);
        var clauses = new List<string>();
        if (page.sqlTitle == "") clauses.Add("q.`qty_available`>0");
        else if (page.sqlTitle == "frozen") clauses.Add("q.`is_freeze`=1");
        if (filter.Sql.Length > 0) clauses.Add(filter.Sql);
        return await QueryPageAsync<StockViewModel>(DetailLocksCte, StockSelectSql,
            clauses.Count == 0 ? "1=1" : string.Join(" AND ", clauses), "q.`sku_code`", filter.Parameters);
    }

    public async Task<(List<SkuSelectViewModel> data, int totals)> SkuSelectPageAsync(PageSearch page, CurrentUser user)
    {
        var filter = DapperSearchBuilder.Build(page.searchObjects, SkuColumns);
        AddPage(filter.Parameters, page, user.tenant_id);
        const string select = """
            SELECT sku.`spu_id`,sku.`sku_code`,sku.`sku_name`,sku.`unit`,spu.`spu_code`,spu.`spu_name`,
                   spu.`supplier_id`,spu.`supplier_name`,spu.`brand`,spu.`origin`,sku.`id` sku_id
            FROM `wms_sku` sku JOIN `wms_spu` spu ON spu.`id`=sku.`spu_id` WHERE spu.`tenant_id`=@tenantId
            """;
        return await QueryPageAsync<SkuSelectViewModel>("", select,
            filter.Sql.Length == 0 ? "1=1" : filter.Sql, "q.`sku_code`", filter.Parameters);
    }

    public async Task<List<LocationStockManagementViewModel>> LocationStockForPhoneAsync(LocationStockForPhoneSearchViewModel input, CurrentUser user)
    {
        var p = new DynamicParameters(new
        {
            tenantId = user.tenant_id, input.sku_id, input.goods_location_id, input.warehouse_id,
            spuName = $"%{EscapeLike(input.spu_name)}%", locationName = $"%{EscapeLike(input.location_name)}%", input.series_number
        });
        const string stockCte = """
            WITH stock_group AS (
              SELECT s.`sku_id`,s.`goods_location_id`,s.`goods_owner_id`,COALESCE(go.`goods_owner_name`,'') goods_owner_name,
                     s.`series_number`,s.`expiry_date`,s.`price`,s.`putaway_date`,SUM(CASE WHEN s.`is_freeze`=1 THEN s.`qty` ELSE 0 END) qty_frozen,SUM(s.`qty`) qty
              FROM `wms_stock` s LEFT JOIN `wms_goodsowner` go ON go.`id`=s.`goods_owner_id`
              JOIN `wms_goodslocation` gl ON gl.`id`=s.`goods_location_id` JOIN `wms_sku` sku ON sku.`id`=s.`sku_id` JOIN `wms_spu` spu ON spu.`id`=sku.`spu_id`
              WHERE s.`tenant_id`=@tenantId AND (@sku_id=0 OR s.`sku_id`=@sku_id) AND (@goods_location_id=0 OR s.`goods_location_id`=@goods_location_id)
                AND (@warehouse_id=0 OR gl.`warehouse_id`=@warehouse_id) AND spu.`spu_name` LIKE @spuName ESCAPE '!'
                AND gl.`location_name` LIKE @locationName ESCAPE '!' AND (@series_number='' OR s.`series_number`=@series_number)
              GROUP BY s.`sku_id`,s.`goods_location_id`,s.`goods_owner_id`,go.`goods_owner_name`,s.`series_number`,s.`expiry_date`,s.`price`,s.`putaway_date`
            ),
            """;
        await using var connection = await _connectionFactory.OpenConnectionAsync();
        return (await connection.QueryAsync<LocationStockManagementViewModel>(
            stockCte + DetailLocksCte[5..] + PhoneInventorySelect + " ORDER BY `sku_code`;", p)).AsList();
    }

    public async Task<(List<DeliveryStatisticViewModel> datas, int totals)> DeliveryStatistic(DeliveryStatisticSearchViewModel input, CurrentUser user)
    {
        var p = new DynamicParameters(new
        {
            tenantId = user.tenant_id, spuName = Like(input.spu_name), spuCode = Like(input.spu_code),
            skuName = Like(input.sku_name), skuCode = Like(input.sku_code), warehouseName = Like(input.warehouse_name),
            input.delivery_date_from, input.delivery_date_to, minDate = UtilConvert.MinDate,
            offset = (input.pageIndex - 1) * input.pageSize, pageSize = input.pageSize
        });
        const string select = """
            SELECT d.`dispatch_no`,wh.`warehouse_name`,gl.`location_name`,spu.`spu_name`,spu.`spu_code`,sku.`sku_name`,sku.`sku_code`,
                   p.`series_number`,p.`price`,p.`expiry_date`,p.`putaway_date`,d.`create_time` delivery_date,go.`goods_owner_name`,
                   SUM(p.`picked_qty`) delivery_qty,SUM(p.`picked_qty`*sku.`price`) delivery_amount
            FROM `wms_dispatchlist` d JOIN `wms_dispatchpicklist` p ON p.`dispatchlist_id`=d.`id`
            JOIN `wms_sku` sku ON sku.`id`=d.`sku_id` JOIN `wms_spu` spu ON spu.`id`=sku.`spu_id`
            JOIN `wms_goodslocation` gl ON gl.`id`=p.`goods_location_id` JOIN `wms_warehouse` wh ON wh.`id`=gl.`warehouse_id`
            JOIN `wms_goodsowner` go ON go.`id`=p.`goods_owner_id`
            WHERE d.`tenant_id`=@tenantId AND d.`dispatch_status`>=6
              AND (@delivery_date_from=@minDate OR d.`create_time`>=@delivery_date_from) AND (@delivery_date_to=@minDate OR d.`create_time`<=@delivery_date_to)
              AND spu.`spu_name` LIKE @spuName ESCAPE '!' AND spu.`spu_code` LIKE @spuCode ESCAPE '!'
              AND sku.`sku_name` LIKE @skuName ESCAPE '!' AND sku.`sku_code` LIKE @skuCode ESCAPE '!' AND wh.`warehouse_name` LIKE @warehouseName ESCAPE '!'
            GROUP BY d.`dispatch_no`,wh.`warehouse_name`,gl.`location_name`,spu.`spu_name`,spu.`spu_code`,sku.`sku_name`,sku.`sku_code`,
                     p.`series_number`,p.`price`,p.`expiry_date`,p.`putaway_date`,d.`create_time`,p.`goods_owner_id`,go.`goods_owner_name`
            """;
        return await QueryPageAsync<DeliveryStatisticViewModel>("", select, "1=1", "q.`delivery_date` DESC", p);
    }

    public async Task<(List<StockAgeViewModel> data, int totals)> StockAgePageAsync(StockAgeSearchViewModel input, CurrentUser user)
    {
        var p = new DynamicParameters(new
        {
            tenantId = user.tenant_id, spuName = Like(input.spu_name), spuCode = Like(input.spu_code), skuName = Like(input.sku_name),
            skuCode = Like(input.sku_code), warehouseName = Like(input.warehouse_name), input.expiry_date_from, input.expiry_date_to,
            input.stock_age_from, input.stock_age_to, minDate = UtilConvert.MinDate,
            today = DateTime.Today,
            offset = (input.pageIndex - 1) * input.pageSize, pageSize = input.pageSize
        });
        const string cte = """
            WITH stock_group AS (
              SELECT s.`sku_id`,s.`goods_location_id`,s.`goods_owner_id`,COALESCE(go.`goods_owner_name`,'') goods_owner_name,
                     s.`series_number`,s.`expiry_date`,s.`price`,s.`putaway_date`,SUM(s.`qty`) qty
              FROM `wms_stock` s LEFT JOIN `wms_goodsowner` go ON go.`id`=s.`goods_owner_id`
              WHERE s.`tenant_id`=@tenantId AND (@expiry_date_from=@minDate OR s.`expiry_date`>=@expiry_date_from)
                AND (@expiry_date_to=@minDate OR s.`expiry_date`<=@expiry_date_to)
              GROUP BY s.`sku_id`,s.`goods_location_id`,s.`goods_owner_id`,go.`goods_owner_name`,s.`series_number`,s.`expiry_date`,s.`price`,s.`putaway_date`
            )
            """;
        const string select = """
            SELECT sg.`sku_id`,sg.`goods_owner_name`,spu.`spu_name`,spu.`spu_code`,sku.`sku_code`,sku.`sku_name`,sg.`qty`,
                   gl.`location_name`,gl.`warehouse_name`,sg.`series_number`,sg.`expiry_date`,sg.`price`,sg.`putaway_date`,
                   CASE WHEN sg.`putaway_date`=@minDate THEN 0 ELSE DATEDIFF(@today,DATE(sg.`putaway_date`)) END stock_age
            FROM stock_group sg JOIN `wms_sku` sku ON sku.`id`=sg.`sku_id` JOIN `wms_spu` spu ON spu.`id`=sku.`spu_id`
            JOIN `wms_goodslocation` gl ON gl.`id`=sg.`goods_location_id`
            WHERE spu.`spu_name` LIKE @spuName ESCAPE '!' AND sku.`sku_name` LIKE @skuName ESCAPE '!'
              AND sku.`sku_code` LIKE @skuCode ESCAPE '!' AND spu.`spu_code` LIKE @spuCode ESCAPE '!' AND gl.`warehouse_name` LIKE @warehouseName ESCAPE '!'
            """;
        const string where = "q.`qty`>0 AND (@stock_age_from<=0 OR q.`stock_age`>=@stock_age_from) AND (@stock_age_to<=0 OR q.`stock_age`<=@stock_age_to)";
        return await QueryPageAsync<StockAgeViewModel>(cte, select, where, "q.`sku_code`", p);
    }

    private async Task PopulateProductImagesAsync<T>(List<T> rows, long tenantId, Func<T, int> skuId, Action<T, string> setImage)
    {
        if (rows.Count == 0) return;
        await using var connection = await _connectionFactory.OpenConnectionAsync();
        var images = await connection.QueryAsync<ProductImageRow>("""
            SELECT m.`wms_sku_id`,c.`img_url` FROM `wms_erp_commodity_map` m
            JOIN `erp_commodity` c ON c.`id`=CAST(m.`erp_commodity_id` AS CHAR)
            WHERE m.`tenant_id`=@tenantId AND m.`wms_sku_id` IN @skuIds AND c.`img_url` IS NOT NULL AND c.`img_url`<>'';
            """, new { tenantId, skuIds = rows.Select(skuId).Distinct().ToArray() });
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

    private static void AddPage(DynamicParameters p, PageSearch page, long tenantId)
    {
        p.Add("tenantId", tenantId); p.Add("offset", (page.pageIndex - 1) * page.pageSize); p.Add("pageSize", page.pageSize);
    }
    private static IReadOnlyDictionary<string, string> Columns(params string[] names) =>
        names.ToDictionary(x => x, x => $"q.`{x}`", StringComparer.OrdinalIgnoreCase);
    private static string Like(string value) => $"%{EscapeLike(value)}%";
    private static string EscapeLike(string value) => value.Replace("!", "!!").Replace("%", "!%").Replace("_", "!_");

    private const string DetailLocksCte = """
        WITH dispatch_lock AS (
          SELECT p.`sku_id`,p.`goods_location_id`,p.`goods_owner_id`,p.`series_number`,p.`expiry_date`,p.`price`,p.`putaway_date`,SUM(p.`pick_qty`) qty_locked
          FROM `wms_dispatchlist` d JOIN `wms_dispatchpicklist` p ON p.`dispatchlist_id`=d.`id`
          WHERE d.`tenant_id`=@tenantId AND d.`dispatch_status`>1 AND d.`dispatch_status`<6
          GROUP BY p.`sku_id`,p.`goods_location_id`,p.`goods_owner_id`,p.`series_number`,p.`expiry_date`,p.`price`,p.`putaway_date`
        ), process_lock AS (
          SELECT p.`sku_id`,p.`goods_location_id`,p.`goods_owner_id`,p.`series_number`,p.`expiry_date`,p.`price`,p.`putaway_date`,SUM(p.`qty`) qty_locked
          FROM `wms_stockprocessdetail` p WHERE p.`is_update_stock`=0 AND p.`is_source`=1
          GROUP BY p.`sku_id`,p.`goods_location_id`,p.`goods_owner_id`,p.`series_number`,p.`expiry_date`,p.`price`,p.`putaway_date`
        ), move_lock AS (
          SELECT m.`sku_id`,m.`orig_goods_location_id` goods_location_id,m.`goods_owner_id`,m.`series_number`,m.`expiry_date`,m.`price`,m.`putaway_date`,SUM(m.`qty`) qty_locked
          FROM `wms_stockmove` m WHERE m.`move_status`=0
          GROUP BY m.`sku_id`,m.`orig_goods_location_id`,m.`goods_owner_id`,m.`series_number`,m.`expiry_date`,m.`price`,m.`putaway_date`
        )
        """;

    private const string StockSelectSql = """
        SELECT s.`sku_id`,spu.`spu_name`,spu.`spu_code`,sku.`sku_code`,sku.`sku_name`,
          CASE WHEN s.`is_freeze`=1 THEN 0 ELSE s.`qty`-COALESCE(dl.`qty_locked`,0)-COALESCE(pl.`qty_locked`,0)-COALESCE(ml.`qty_locked`,0) END qty_available,
          s.`qty`,s.`goods_location_id`,s.`goods_owner_id`,gl.`location_name`,gl.`warehouse_name`,s.`series_number`,s.`expiry_date`,s.`price`,s.`putaway_date`,
          s.`is_freeze`,s.`id`,s.`tenant_id`,s.`last_update_time`,sku.`unit`,COALESCE(go.`goods_owner_name`,'') goods_owner_name
        FROM `wms_stock` s
        LEFT JOIN dispatch_lock dl ON dl.`sku_id`=s.`sku_id` AND dl.`goods_location_id`=s.`goods_location_id` AND dl.`goods_owner_id`=s.`goods_owner_id` AND dl.`series_number`<=>s.`series_number` AND dl.`expiry_date`<=>s.`expiry_date` AND dl.`price`<=>s.`price` AND dl.`putaway_date`<=>s.`putaway_date`
        LEFT JOIN process_lock pl ON pl.`sku_id`=s.`sku_id` AND pl.`goods_location_id`=s.`goods_location_id` AND pl.`goods_owner_id`=s.`goods_owner_id` AND pl.`series_number`<=>s.`series_number` AND pl.`expiry_date`<=>s.`expiry_date` AND pl.`price`<=>s.`price` AND pl.`putaway_date`<=>s.`putaway_date`
        LEFT JOIN move_lock ml ON ml.`sku_id`=s.`sku_id` AND ml.`goods_location_id`=s.`goods_location_id` AND ml.`goods_owner_id`=s.`goods_owner_id` AND ml.`series_number`<=>s.`series_number` AND ml.`expiry_date`<=>s.`expiry_date` AND ml.`price`<=>s.`price` AND ml.`putaway_date`<=>s.`putaway_date`
        JOIN `wms_sku` sku ON sku.`id`=s.`sku_id` JOIN `wms_spu` spu ON spu.`id`=sku.`spu_id` JOIN `wms_goodslocation` gl ON gl.`id`=s.`goods_location_id`
        LEFT JOIN `wms_goodsowner` go ON go.`id`=s.`goods_owner_id` WHERE s.`tenant_id`=@tenantId
        """;

    private const string LocationInventoryCte = """
        WITH stock_group AS (
          SELECT s.`sku_id`,s.`goods_location_id`,s.`goods_owner_id`,COALESCE(go.`goods_owner_name`,'') goods_owner_name,s.`expiry_date`,s.`price`,s.`putaway_date`,
                 SUM(CASE WHEN s.`is_freeze`=1 THEN s.`qty` ELSE 0 END) qty_frozen,SUM(s.`qty`) qty
          FROM `wms_stock` s LEFT JOIN `wms_goodsowner` go ON go.`id`=s.`goods_owner_id` WHERE s.`tenant_id`=@tenantId
          GROUP BY s.`sku_id`,s.`goods_location_id`,s.`goods_owner_id`,go.`goods_owner_name`,s.`expiry_date`,s.`price`,s.`putaway_date`
        ), dispatch_lock AS (
          SELECT p.`sku_id`,p.`goods_location_id`,p.`goods_owner_id`,p.`expiry_date`,p.`price`,p.`putaway_date`,SUM(p.`pick_qty`) qty_locked
          FROM `wms_dispatchlist` d JOIN `wms_dispatchpicklist` p ON p.`dispatchlist_id`=d.`id`
          WHERE d.`tenant_id`=@tenantId AND d.`dispatch_status`>1 AND d.`dispatch_status`<6
          GROUP BY p.`sku_id`,p.`goods_location_id`,p.`goods_owner_id`,p.`expiry_date`,p.`price`,p.`putaway_date`
        ), process_lock AS (
          SELECT p.`sku_id`,p.`goods_location_id`,p.`goods_owner_id`,p.`expiry_date`,p.`price`,p.`putaway_date`,SUM(p.`qty`) qty_locked
          FROM `wms_stockprocessdetail` p WHERE p.`is_update_stock`=0 AND p.`is_source`=1
          GROUP BY p.`sku_id`,p.`goods_location_id`,p.`goods_owner_id`,p.`expiry_date`,p.`price`,p.`putaway_date`
        ), move_lock AS (
          SELECT m.`sku_id`,m.`orig_goods_location_id` goods_location_id,m.`goods_owner_id`,m.`expiry_date`,m.`price`,m.`putaway_date`,SUM(m.`qty`) qty_locked
          FROM `wms_stockmove` m WHERE m.`move_status`=0
          GROUP BY m.`sku_id`,m.`orig_goods_location_id`,m.`goods_owner_id`,m.`expiry_date`,m.`price`,m.`putaway_date`
        )
        """;

    private const string LocationInventorySelect = """
        SELECT sg.`sku_id`,sg.`goods_owner_name`,spu.`spu_name`,spu.`spu_code`,sku.`sku_code`,sku.`sku_name`,
          CASE WHEN gl.`warehouse_area_property`=5 THEN 0 ELSE sg.`qty`-sg.`qty_frozen`-COALESCE(dl.`qty_locked`,0)-COALESCE(pl.`qty_locked`,0)-COALESCE(ml.`qty_locked`,0) END qty_available,
          sg.`qty_frozen`+COALESCE(dl.`qty_locked`,0)+COALESCE(pl.`qty_locked`,0)+COALESCE(ml.`qty_locked`,0) qty_locked,sg.`qty`,
          gl.`location_name`,gl.`warehouse_area_id`,gl.`warehouse_area_name`,wh.`id` warehouse_id,wh.`warehouse_name`,sg.`expiry_date`,sg.`price`,sg.`putaway_date`,sg.`goods_location_id`
        FROM stock_group sg
        LEFT JOIN dispatch_lock dl ON dl.`sku_id`=sg.`sku_id` AND dl.`goods_location_id`=sg.`goods_location_id` AND dl.`goods_owner_id`=sg.`goods_owner_id` AND dl.`expiry_date`<=>sg.`expiry_date` AND dl.`price`<=>sg.`price` AND dl.`putaway_date`<=>sg.`putaway_date`
        LEFT JOIN process_lock pl ON pl.`sku_id`=sg.`sku_id` AND pl.`goods_location_id`=sg.`goods_location_id` AND pl.`goods_owner_id`=sg.`goods_owner_id` AND pl.`expiry_date`<=>sg.`expiry_date` AND pl.`price`<=>sg.`price` AND pl.`putaway_date`<=>sg.`putaway_date`
        LEFT JOIN move_lock ml ON ml.`sku_id`=sg.`sku_id` AND ml.`goods_location_id`=sg.`goods_location_id` AND ml.`goods_owner_id`=sg.`goods_owner_id` AND ml.`expiry_date`<=>sg.`expiry_date` AND ml.`price`<=>sg.`price` AND ml.`putaway_date`<=>sg.`putaway_date`
        JOIN `wms_sku` sku ON sku.`id`=sg.`sku_id` JOIN `wms_spu` spu ON spu.`id`=sku.`spu_id`
        JOIN `wms_goodslocation` gl ON gl.`id`=sg.`goods_location_id` JOIN `wms_warehouse` wh ON wh.`id`=gl.`warehouse_id`
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
        SELECT sg.`sku_id`,sg.`goods_owner_name`,spu.`spu_name`,spu.`spu_code`,sku.`sku_code`,sku.`sku_name`,
          CASE WHEN gl.`warehouse_area_property`=5 THEN 0 ELSE sg.`qty`-sg.`qty_frozen`-COALESCE(dl.`qty_locked`,0)-COALESCE(pl.`qty_locked`,0)-COALESCE(ml.`qty_locked`,0) END qty_available,
          sg.`qty_frozen`+COALESCE(dl.`qty_locked`,0)+COALESCE(pl.`qty_locked`,0)+COALESCE(ml.`qty_locked`,0) qty_locked,sg.`qty`,
          gl.`location_name`,gl.`warehouse_id`,gl.`warehouse_name`,sg.`series_number`,sg.`expiry_date`,sg.`price`,sg.`putaway_date`,sg.`goods_location_id`
        FROM stock_group sg
        LEFT JOIN dispatch_lock dl ON dl.`sku_id`=sg.`sku_id` AND dl.`goods_location_id`=sg.`goods_location_id` AND dl.`goods_owner_id`=sg.`goods_owner_id` AND dl.`series_number`<=>sg.`series_number` AND dl.`expiry_date`<=>sg.`expiry_date` AND dl.`price`<=>sg.`price` AND dl.`putaway_date`<=>sg.`putaway_date`
        LEFT JOIN process_lock pl ON pl.`sku_id`=sg.`sku_id` AND pl.`goods_location_id`=sg.`goods_location_id` AND pl.`goods_owner_id`=sg.`goods_owner_id` AND pl.`series_number`<=>sg.`series_number` AND pl.`expiry_date`<=>sg.`expiry_date` AND pl.`price`<=>sg.`price` AND pl.`putaway_date`<=>sg.`putaway_date`
        LEFT JOIN move_lock ml ON ml.`sku_id`=sg.`sku_id` AND ml.`goods_location_id`=sg.`goods_location_id` AND ml.`goods_owner_id`=sg.`goods_owner_id` AND ml.`series_number`<=>sg.`series_number` AND ml.`expiry_date`<=>sg.`expiry_date` AND ml.`price`<=>sg.`price` AND ml.`putaway_date`<=>sg.`putaway_date`
        JOIN `wms_sku` sku ON sku.`id`=sg.`sku_id` JOIN `wms_spu` spu ON spu.`id`=sku.`spu_id`
        JOIN `wms_goodslocation` gl ON gl.`id`=sg.`goods_location_id`
        """;

    private const string StockSummaryCte = """
        WITH stock_group AS (
          SELECT s.`sku_id`,SUM(CASE WHEN s.`is_freeze`=1 THEN s.`qty` ELSE 0 END) qty_frozen,SUM(s.`qty`) qty,
            SUM(CASE WHEN gl.`warehouse_area_property`<>5 THEN s.`qty` ELSE 0 END) qty_normal,
            SUM(CASE WHEN gl.`warehouse_area_property`<>5 AND s.`is_freeze`=1 THEN s.`qty` ELSE 0 END) qty_normal_frozen
          FROM `wms_stock` s JOIN `wms_goodslocation` gl ON gl.`id`=s.`goods_location_id` WHERE s.`tenant_id`=@tenantId GROUP BY s.`sku_id`
        ), asn_group AS (
          SELECT a.`sku_id`,SUM(CASE WHEN a.`asn_status`=0 THEN a.`asn_qty` ELSE 0 END) qty_asn,SUM(CASE WHEN a.`asn_status`=1 THEN a.`asn_qty` ELSE 0 END) qty_to_unload,
            SUM(CASE WHEN a.`asn_status`=2 THEN a.`asn_qty` ELSE 0 END) qty_to_sort,SUM(CASE WHEN a.`asn_status`=3 THEN a.`sorted_qty` ELSE 0 END) qty_sorted,
            SUM(CASE WHEN a.`asn_status`=4 THEN a.`shortage_qty` ELSE 0 END) shortage_qty FROM `wms_asn` a WHERE a.`tenant_id`=@tenantId GROUP BY a.`sku_id`
        ), dispatch_lock AS (SELECT d.`sku_id`,SUM(d.`lock_qty`) qty_locked FROM `wms_dispatchlist` d WHERE d.`tenant_id`=@tenantId GROUP BY d.`sku_id`),
        process_lock AS (
          SELECT p.`sku_id`,SUM(p.`qty`) qty_locked,SUM(CASE WHEN gl.`warehouse_area_property`<>5 THEN p.`qty` ELSE 0 END) qty_normal_locked
          FROM `wms_stockprocessdetail` p JOIN `wms_goodslocation` gl ON gl.`id`=p.`goods_location_id` WHERE p.`is_update_stock`=0 AND p.`is_source`=1 GROUP BY p.`sku_id`
        ), move_lock AS (
          SELECT m.`sku_id`,SUM(m.`qty`) qty_locked,SUM(CASE WHEN gl.`warehouse_area_property`<>5 THEN m.`qty` ELSE 0 END) qty_normal_locked
          FROM `wms_stockmove` m JOIN `wms_goodslocation` gl ON gl.`id`=m.`orig_goods_location_id` WHERE m.`move_status`=0 GROUP BY m.`sku_id`
        )
        """;

    private const string StockSummarySelect = """
        SELECT sku.`id` sku_id,spu.`spu_name`,spu.`spu_code`,sku.`sku_code`,COALESCE(ag.`qty_asn`,0) qty_asn,
          COALESCE(sg.`qty_normal`,0)-COALESCE(sg.`qty_normal_frozen`,0)-COALESCE(dl.`qty_locked`,0)-COALESCE(pl.`qty_normal_locked`,0)-COALESCE(ml.`qty_normal_locked`,0) qty_available,
          COALESCE(sg.`qty_frozen`,0)+COALESCE(dl.`qty_locked`,0)+COALESCE(pl.`qty_locked`,0)+COALESCE(ml.`qty_locked`,0) qty_locked,
          COALESCE(ag.`qty_sorted`,0) qty_sorted,COALESCE(ag.`qty_to_sort`,0) qty_to_sort,COALESCE(ag.`shortage_qty`,0) shortage_qty,
          COALESCE(ag.`qty_to_unload`,0) qty_to_unload,COALESCE(sg.`qty`,0) qty
        FROM `wms_sku` sku LEFT JOIN asn_group ag ON ag.`sku_id`=sku.`id` LEFT JOIN stock_group sg ON sg.`sku_id`=sku.`id`
        LEFT JOIN dispatch_lock dl ON dl.`sku_id`=sg.`sku_id` LEFT JOIN process_lock pl ON pl.`sku_id`=sku.`id` LEFT JOIN move_lock ml ON ml.`sku_id`=sku.`id`
        JOIN `wms_spu` spu ON spu.`id`=sku.`spu_id` WHERE spu.`tenant_id`=@tenantId
        """;

    private const string SafetyCte = """
        WITH stock_group AS (
          SELECT s.`sku_id`,gl.`warehouse_id`,SUM(CASE WHEN s.`is_freeze`=1 THEN s.`qty` ELSE 0 END) qty_frozen,SUM(s.`qty`) qty
          FROM `wms_stock` s JOIN `wms_goodslocation` gl ON gl.`id`=s.`goods_location_id` WHERE s.`tenant_id`=@tenantId GROUP BY s.`sku_id`,gl.`warehouse_id`
        ), dispatch_lock AS (
          SELECT p.`sku_id`,gl.`warehouse_id`,SUM(p.`pick_qty`) qty_locked FROM `wms_dispatchlist` d JOIN `wms_dispatchpicklist` p ON p.`dispatchlist_id`=d.`id`
          JOIN `wms_goodslocation` gl ON gl.`id`=p.`goods_location_id` WHERE d.`tenant_id`=@tenantId AND d.`dispatch_status`>1 AND d.`dispatch_status`<6 GROUP BY p.`sku_id`,gl.`warehouse_id`
        ), process_lock AS (
          SELECT p.`sku_id`,gl.`warehouse_id`,SUM(p.`qty`) qty_locked FROM `wms_stockprocessdetail` p JOIN `wms_goodslocation` gl ON gl.`id`=p.`goods_location_id`
          WHERE p.`is_update_stock`=0 AND p.`is_source`=1 GROUP BY p.`sku_id`,gl.`warehouse_id`
        ), move_lock AS (
          SELECT m.`sku_id`,gl.`warehouse_id`,SUM(m.`qty`) qty_locked FROM `wms_stockmove` m JOIN `wms_goodslocation` gl ON gl.`id`=m.`orig_goods_location_id`
          WHERE m.`move_status`=0 GROUP BY m.`sku_id`,gl.`warehouse_id`
        )
        """;

    // Keep the legacy goods-location-id lookup used for the warehouse display fields.
    private const string SafetySelect = """
        SELECT sg.`sku_id`,spu.`spu_name`,spu.`spu_code`,sku.`sku_code`,sku.`sku_name`,
          CASE WHEN gl.`warehouse_area_property`=5 THEN 0 ELSE sg.`qty`-sg.`qty_frozen`-COALESCE(dl.`qty_locked`,0)-COALESCE(pl.`qty_locked`,0)-COALESCE(ml.`qty_locked`,0) END qty_available,
          sg.`qty_frozen`,COALESCE(dl.`qty_locked`,0)+COALESCE(pl.`qty_locked`,0)+COALESCE(ml.`qty_locked`,0) qty_locked,sg.`qty`,gl.`warehouse_name`,COALESCE(sss.`safety_stock_qty`,0) safety_stock_qty
        FROM stock_group sg LEFT JOIN dispatch_lock dl ON dl.`sku_id`=sg.`sku_id` AND dl.`warehouse_id`=sg.`warehouse_id`
        LEFT JOIN process_lock pl ON pl.`sku_id`=sg.`sku_id` AND pl.`warehouse_id`=sg.`warehouse_id` LEFT JOIN move_lock ml ON ml.`sku_id`=sg.`sku_id` AND ml.`warehouse_id`=sg.`warehouse_id`
        JOIN `wms_sku` sku ON sku.`id`=sg.`sku_id` JOIN `wms_spu` spu ON spu.`id`=sku.`spu_id` JOIN `wms_goodslocation` gl ON gl.`id`=sg.`warehouse_id`
        LEFT JOIN `wms_sku_safety_stock` sss ON sss.`sku_id`=sg.`sku_id` AND sss.`warehouse_id`=sg.`warehouse_id`
        """;

    private sealed class ProductImageRow
    {
        public int wms_sku_id { get; set; }
        public string img_url { get; set; } = "";
    }
}
