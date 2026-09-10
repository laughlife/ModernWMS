using System.Data;
using Dapper;
using Microsoft.Extensions.Localization;
using ModernWMS.Core.Database;
using ModernWMS.Core.JWT;
using ModernWMS.Core.Models;
using ModernWMS.Core.Services;
using ModernWMS.WMS.Entities.Models;
using ModernWMS.WMS.Entities.ViewModels;
using ModernWMS.WMS.IServices;

namespace ModernWMS.WMS.Services;

/// <summary>Provides SPU and SKU catalog operations.</summary>
public class SpuService : BaseService<SpuEntity>, ISpuService
{
    private const string SpuColumns = """
        s.`id`,s.`spu_code`,s.`spu_name`,s.`spu_description`,s.`supplier_id`,s.`supplier_name`,
        s.`brand`,s.`origin`,s.`length_unit`,s.`volume_unit`,s.`weight_unit`,s.`creator`,
        s.`create_time`,s.`last_update_time`,s.`is_valid`
        """;
    private const string SkuColumns = """
        k.`id`,k.`spu_id`,k.`sku_code`,k.`sku_name`,k.`bar_code`,k.`weight`,k.`lenght`,k.`width`,
        k.`height`,k.`volume`,k.`unit`,k.`cost`,k.`price`,k.`create_time`,k.`last_update_time`
        """;
    private static readonly IReadOnlyDictionary<string, string> SpuSearchColumns =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["id"] = "s.`id`", ["spu_code"] = "s.`spu_code`", ["spu_name"] = "s.`spu_name`",
            ["spu_description"] = "s.`spu_description`", ["supplier_id"] = "s.`supplier_id`",
            ["supplier_name"] = "s.`supplier_name`", ["brand"] = "s.`brand`", ["origin"] = "s.`origin`",
            ["length_unit"] = "s.`length_unit`", ["volume_unit"] = "s.`volume_unit`",
            ["weight_unit"] = "s.`weight_unit`", ["creator"] = "s.`creator`",
            ["create_time"] = "s.`create_time`", ["last_update_time"] = "s.`last_update_time`",
            ["is_valid"] = "s.`is_valid`"
        };
    private static readonly IReadOnlyDictionary<string, string> CatalogSearchColumns =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["sku_id"] = "k.`id`", ["sku_code"] = "k.`sku_code`", ["sku_name"] = "k.`sku_name`",
            ["volume_cm3"] = "CASE s.`volume_unit` WHEN 1 THEN k.`volume`*1000 WHEN 2 THEN k.`volume`*1000000 ELSE k.`volume` END"
        };

    private readonly IMySqlConnectionFactory _connectionFactory;
    private readonly IStringLocalizer<Core.MultiLanguage> _stringLocalizer;

    /// <summary>Initializes the service.</summary>
    public SpuService(IMySqlConnectionFactory connectionFactory, IStringLocalizer<Core.MultiLanguage> stringLocalizer)
    {
        _connectionFactory = connectionFactory;
        _stringLocalizer = stringLocalizer;
    }

    /// <summary>Gets a paged list of SPU records.</summary>
    public async Task<(List<SpuBothViewModel> data, int totals)> PageAsync(PageSearch pageSearch, CurrentUser currentUser)
    {
        var filter = DapperSearchBuilder.Build(pageSearch.searchObjects, SpuSearchColumns);
        var where = string.IsNullOrWhiteSpace(filter.Sql) ? "1=1" : filter.Sql;
        filter.Parameters.Add("offset", (pageSearch.pageIndex - 1) * pageSearch.pageSize);
        filter.Parameters.Add("pageSize", pageSearch.pageSize);
        await using var connection = await _connectionFactory.OpenConnectionAsync();
        using var result = await connection.QueryMultipleAsync($"""
            SELECT COUNT(*) FROM `wms_spu` s WHERE {where};
            SELECT {SpuColumns} FROM `wms_spu` s WHERE {where}
             ORDER BY s.`create_time` DESC LIMIT @pageSize OFFSET @offset;
            """, filter.Parameters);
        var totals = await result.ReadSingleAsync<int>();
        var rows = (await result.ReadAsync<SpuBothViewModel>()).AsList();
        await PopulateSpuDetailsAsync(connection, rows);
        return (rows, totals);
    }

    /// <summary>Gets a paged commodity catalog.</summary>
    public async Task<(List<CommodityCatalogViewModel> data, int totals)> PageCatalogAsync(PageSearch pageSearch, CurrentUser currentUser)
    {
        var filter = DapperSearchBuilder.Build(pageSearch.searchObjects, CatalogSearchColumns);
        var where = "s.`is_valid`=1" +
                    (string.IsNullOrWhiteSpace(filter.Sql) ? string.Empty : $" AND {filter.Sql}");
        filter.Parameters.Add("offset", (pageSearch.pageIndex - 1) * pageSearch.pageSize);
        filter.Parameters.Add("pageSize", pageSearch.pageSize);
        await using var connection = await _connectionFactory.OpenConnectionAsync();
        using var result = await connection.QueryMultipleAsync($"""
            SELECT COUNT(*) FROM `wms_sku` k INNER JOIN `wms_spu` s ON s.`id`=k.`spu_id` WHERE {where};
            SELECT k.`id` AS `sku_id`,k.`sku_code`,k.`sku_name`,
                   CASE s.`volume_unit` WHEN 1 THEN k.`volume`*1000 WHEN 2 THEN k.`volume`*1000000 ELSE k.`volume` END AS `volume_cm3`
              FROM `wms_sku` k INNER JOIN `wms_spu` s ON s.`id`=k.`spu_id`
             WHERE {where} ORDER BY k.`sku_code` LIMIT @pageSize OFFSET @offset;
            """, filter.Parameters);
        var totals = await result.ReadSingleAsync<int>();
        var rows = (await result.ReadAsync<CommodityCatalogViewModel>()).AsList();
        await PopulateCatalogDetailsAsync(connection, rows);
        return (rows, totals);
    }

    private static async Task PopulateCatalogDetailsAsync(System.Data.Common.DbConnection connection,
        List<CommodityCatalogViewModel> rows)
    {
        if (rows.Count == 0) return;
        var skuIds = rows.Select(t => t.sku_id).Distinct().ToArray();
        using var result = await connection.QueryMultipleAsync("""
            SELECT m.`wms_sku_id`,c.`img_url` FROM `wms_erp_commodity_map` m
              INNER JOIN `erp_commodity` c ON c.`id`=CAST(m.`erp_commodity_id` AS CHAR)
             WHERE m.`wms_sku_id` IN @skuIds AND c.`img_url` IS NOT NULL AND c.`img_url`<>'';
            SELECT r.`wms_sku_id`,r.`task_item_id`,r.`dept_name`,r.`order_user_name`,r.`inbound_qty`,r.`receipt_time`
              FROM `wms_erp_receipt_item` r WHERE r.`wms_sku_id` IN @skuIds;
            SELECT r.`wms_sku_id`,r.`task_item_id`,DATE(r.`receipt_time`) AS `batch_date`,
                   COALESCE(t.`purchaser_name`,'') AS `purchaser_name`,COALESCE(i.`per_purchase`,0) AS `unit_cost`,r.`inbound_qty` AS `quantity`
              FROM `wms_erp_receipt_item` r
              INNER JOIN `erp_purchase_task_item` i ON i.`id`=r.`task_item_id` AND i.`deleted`=0
              INNER JOIN `erp_purchase_task` t ON t.`id`=i.`task_id` AND t.`deleted`=0
             WHERE r.`wms_sku_id` IN @skuIds AND r.`inbound_qty`>0;
            """, new { skuIds });
        var images = (await result.ReadAsync<CatalogImageRow>()).AsList();
        var receipts = (await result.ReadAsync<CatalogReceiptRow>()).AsList();
        var batches = (await result.ReadAsync<CatalogBatchRow>()).AsList();
        var imageBySku = images.GroupBy(t => t.wms_sku_id).ToDictionary(t => t.Key, t => t.First().img_url);
        var ownersBySku = receipts
            .Where(t => !string.IsNullOrWhiteSpace(t.dept_name) || !string.IsNullOrWhiteSpace(t.order_user_name))
            .GroupBy(t => t.wms_sku_id).ToDictionary(t => t.Key, t => t.Select(x => new CommodityOwnershipViewModel
            {
                dept_name = (x.dept_name ?? string.Empty).Trim(),
                order_user_name = (x.order_user_name ?? string.Empty).Trim()
            }).DistinctBy(x => new { x.dept_name, x.order_user_name }).OrderBy(x => x.dept_name)
              .ThenBy(x => x.order_user_name).ToList());
        var quantityBySku = receipts.Where(t => t.inbound_qty > 0).GroupBy(t => t.wms_sku_id)
            .ToDictionary(t => t.Key, t => t.Sum(x => x.inbound_qty));
        var batchesBySku = batches.GroupBy(t => t.wms_sku_id).ToDictionary(t => t.Key, t => t
            .GroupBy(x => new { x.task_item_id, x.batch_date, purchaser_name = x.purchaser_name.Trim(), x.unit_cost })
            .Select(x => new CommodityCostBatchViewModel
            {
                batch_date = x.Key.batch_date, purchaser_name = x.Key.purchaser_name, unit_cost = x.Key.unit_cost,
                quantity = x.Sum(y => y.quantity)
            }).OrderBy(x => x.batch_date).ThenBy(x => x.purchaser_name).ThenBy(x => x.unit_cost).ToList());
        foreach (var row in rows)
        {
            if (imageBySku.TryGetValue(row.sku_id, out var image)) row.product_image = image;
            if (ownersBySku.TryGetValue(row.sku_id, out var owners)) row.ownerships = owners;
            if (quantityBySku.TryGetValue(row.sku_id, out var quantity)) row.total_qty = quantity;
            if (batchesBySku.TryGetValue(row.sku_id, out var skuBatches))
            {
                row.cost_batches = skuBatches;
                row.total_value = skuBatches.Sum(t => t.unit_cost * t.quantity);
            }
        }
    }

    /// <summary>Gets an SPU by id.</summary>
    public async Task<SpuBothViewModel> GetAsync(int id)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync();
        var row = await connection.QuerySingleOrDefaultAsync<SpuBothViewModel>($"SELECT {SpuColumns} FROM `wms_spu` s WHERE s.`id`=@id LIMIT 1;", new { id });
        if (row == null) return new SpuBothViewModel();
        await PopulateSpuDetailsAsync(connection, [row]);
        return row;
    }

    /// <summary>Gets SKU details by id.</summary>
    public async Task<SkuDetailViewModel> GetSkuAsync(int sku_id)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync();
        return await connection.QuerySingleOrDefaultAsync<SkuDetailViewModel>($"{SkuDetailSql} WHERE k.`id`=@sku_id LIMIT 1;", new { sku_id }) ?? new SkuDetailViewModel();
    }

    /// <summary>Gets SKU details by barcode.</summary>
    public async Task<SkuDetailViewModel> GetSkuByBarCodeAsync(string bar_code)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync();
        return await connection.QueryFirstOrDefaultAsync<SkuDetailViewModel>($"{SkuDetailSql} WHERE k.`bar_code`=@bar_code LIMIT 1;", new { bar_code }) ?? new SkuDetailViewModel();
    }

    /// <summary>Inserts, updates, or removes SKU safety-stock settings.</summary>
    public async Task<(bool flag, string msg)> InsertOrUpdateSkuSafetyStockAsync(SkuSafetyStockPutViewModel viewModel)
    {
        if (viewModel.detailList.Count == 0) return (false, _stringLocalizer["save_failed"]);
        await using var connection = await _connectionFactory.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.Serializable);
        try
        {
            foreach (var warehouseId in viewModel.detailList.Where(t => t.id >= 0).Select(t => t.warehouse_id).Distinct())
            {
                if (!await connection.ExecuteScalarAsync<bool>("""
                    SELECT EXISTS(SELECT 1 FROM `erp_warehouse`
                    WHERE `id`=@warehouseId AND `id`=320118 AND `deleted`=0 AND `attr`='国内仓库');
                    """, new { warehouseId }, transaction))
                {
                    await transaction.RollbackAsync();
                    return (false, "安全库存仓库必须是有效的ERP深圳仓");
                }
            }
            foreach (var item in viewModel.detailList)
            {
                if (item.id == 0)
                    await connection.ExecuteAsync("INSERT INTO `wms_sku_safety_stock` (`sku_id`,`warehouse_id`,`safety_stock_qty`) VALUES (@skuId,@warehouse_id,@safety_stock_qty);",
                        new { skuId = viewModel.sku_id, item.warehouse_id, item.safety_stock_qty }, transaction);
                else if (item.id < 0)
                    await connection.ExecuteAsync("DELETE FROM `wms_sku_safety_stock` WHERE `id`=@id AND `sku_id`=@skuId;", new { id = -item.id, skuId = viewModel.sku_id }, transaction);
                else
                    await connection.ExecuteAsync("UPDATE `wms_sku_safety_stock` SET `warehouse_id`=@warehouse_id,`safety_stock_qty`=@safety_stock_qty WHERE `id`=@id AND `sku_id`=@skuId;",
                        new { item.id, skuId = viewModel.sku_id, item.warehouse_id, item.safety_stock_qty }, transaction);
            }
            await transaction.CommitAsync();
            return (true, _stringLocalizer["save_success"]);
        }
        catch { await transaction.RollbackAsync(); throw; }
    }

    private static async Task PopulateSpuDetailsAsync(System.Data.Common.DbConnection connection, List<SpuBothViewModel> rows)
    {
        if (rows.Count == 0) return;
        var spuIds = rows.Select(t => t.id).ToArray();
        using var result = await connection.QueryMultipleAsync($"""
            SELECT {SkuColumns} FROM `wms_sku` k WHERE k.`spu_id` IN @spuIds ORDER BY k.`id`;
            SELECT ss.`id`,ss.`sku_id`,ss.`safety_stock_qty`,ss.`warehouse_id`,w.`name` AS `warehouse_name`
              FROM `wms_sku_safety_stock` ss INNER JOIN `erp_warehouse` w ON w.`id`=ss.`warehouse_id` AND w.`deleted`=0 AND w.`id`=320118
              INNER JOIN `wms_sku` k ON k.`id`=ss.`sku_id` WHERE k.`spu_id` IN @spuIds ORDER BY ss.`id`;
            """, new { spuIds });
        var skus = (await result.ReadAsync<SkuViewModel>()).AsList();
        var stocks = (await result.ReadAsync<SkuSafetyStockViewModel>()).AsList();
        var stocksBySku = stocks.GroupBy(t => t.sku_id).ToDictionary(t => t.Key, t => t.ToList());
        foreach (var sku in skus) if (stocksBySku.TryGetValue(sku.id, out var skuStocks)) sku.detailList = skuStocks;
        var skusBySpu = skus.GroupBy(t => t.spu_id).ToDictionary(t => t.Key, t => t.ToList());
        foreach (var row in rows) if (skusBySpu.TryGetValue(row.id, out var spuSkus)) row.detailList = spuSkus;
    }

    private const string SkuDetailSql = """
        SELECT s.`id` AS `spu_id`,s.`spu_code`,s.`spu_name`,s.`spu_description`,s.`supplier_id`,s.`supplier_name`,
               s.`brand`,s.`origin`,s.`length_unit`,s.`volume_unit`,s.`weight_unit`,k.`id` AS `sku_id`,
               k.`sku_code`,k.`sku_name`,k.`bar_code`,k.`weight`,k.`lenght`,k.`width`,k.`height`,k.`volume`,k.`unit`,k.`cost`,k.`price`
          FROM `wms_spu` s INNER JOIN `wms_sku` k ON k.`spu_id`=s.`id`
        """;
    private sealed class CatalogImageRow { public int wms_sku_id { get; set; } public string img_url { get; set; } = string.Empty; }
    private sealed class CatalogReceiptRow
    {
        public int wms_sku_id { get; set; }
        public long? task_item_id { get; set; }
        public string? dept_name { get; set; }
        public string? order_user_name { get; set; }
        public long inbound_qty { get; set; }
        public DateTime receipt_time { get; set; }
    }
    private sealed class CatalogBatchRow
    {
        public int wms_sku_id { get; set; }
        public long task_item_id { get; set; }
        public DateTime batch_date { get; set; }
        public string purchaser_name { get; set; } = string.Empty;
        public decimal unit_cost { get; set; }
        public long quantity { get; set; }
    }
}
