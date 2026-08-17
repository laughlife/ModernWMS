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

    public SpuService(IMySqlConnectionFactory connectionFactory, IStringLocalizer<Core.MultiLanguage> stringLocalizer)
    {
        _connectionFactory = connectionFactory;
        _stringLocalizer = stringLocalizer;
    }

    public async Task<(List<SpuBothViewModel> data, int totals)> PageAsync(PageSearch pageSearch, CurrentUser currentUser)
    {
        var filter = DapperSearchBuilder.Build(pageSearch.searchObjects, SpuSearchColumns);
        var where = "s.`tenant_id`=@tenantId" + (string.IsNullOrWhiteSpace(filter.Sql) ? string.Empty : $" AND {filter.Sql}");
        filter.Parameters.Add("tenantId", currentUser.tenant_id);
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

    public async Task<(List<CommodityCatalogViewModel> data, int totals)> PageCatalogAsync(PageSearch pageSearch, CurrentUser currentUser)
    {
        var filter = DapperSearchBuilder.Build(pageSearch.searchObjects, CatalogSearchColumns);
        var where = "s.`tenant_id`=@tenantId AND s.`is_valid`=1" +
                    (string.IsNullOrWhiteSpace(filter.Sql) ? string.Empty : $" AND {filter.Sql}");
        filter.Parameters.Add("tenantId", currentUser.tenant_id);
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
        await PopulateCatalogDetailsAsync(connection, rows, currentUser.tenant_id);
        return (rows, totals);
    }

    private static async Task PopulateCatalogDetailsAsync(System.Data.Common.DbConnection connection,
        List<CommodityCatalogViewModel> rows, long tenantId)
    {
        if (rows.Count == 0) return;
        var skuIds = rows.Select(t => t.sku_id).Distinct().ToArray();
        using var result = await connection.QueryMultipleAsync("""
            SELECT m.`wms_sku_id`,c.`img_url` FROM `wms_erp_commodity_map` m
              INNER JOIN `erp_commodity` c ON c.`id`=CAST(m.`erp_commodity_id` AS CHAR)
             WHERE m.`tenant_id`=@tenantId AND m.`wms_sku_id` IN @skuIds AND c.`img_url` IS NOT NULL AND c.`img_url`<>'';
            SELECT r.`wms_sku_id`,r.`task_item_id`,r.`dept_name`,r.`order_user_name`,r.`inbound_qty`,r.`receipt_time`
              FROM `wms_erp_receipt_item` r WHERE r.`tenant_id`=@tenantId AND r.`wms_sku_id` IN @skuIds;
            SELECT r.`wms_sku_id`,r.`task_item_id`,DATE(r.`receipt_time`) AS `batch_date`,
                   COALESCE(t.`purchaser_name`,'') AS `purchaser_name`,COALESCE(i.`per_purchase`,0) AS `unit_cost`,r.`inbound_qty` AS `quantity`
              FROM `wms_erp_receipt_item` r
              INNER JOIN `erp_purchase_task_item` i ON i.`id`=r.`task_item_id` AND i.`deleted`=0
              INNER JOIN `erp_purchase_task` t ON t.`id`=i.`task_id` AND t.`deleted`=0
             WHERE r.`tenant_id`=@tenantId AND r.`wms_sku_id` IN @skuIds AND r.`inbound_qty`>0;
            """, new { tenantId, skuIds });
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

    public async Task<SpuBothViewModel> GetAsync(int id)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync();
        var row = await connection.QuerySingleOrDefaultAsync<SpuBothViewModel>($"SELECT {SpuColumns} FROM `wms_spu` s WHERE s.`id`=@id LIMIT 1;", new { id });
        if (row == null) return new SpuBothViewModel();
        await PopulateSpuDetailsAsync(connection, [row]);
        return row;
    }

    public async Task<SkuDetailViewModel> GetSkuAsync(int sku_id)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync();
        return await connection.QuerySingleOrDefaultAsync<SkuDetailViewModel>($"{SkuDetailSql} WHERE k.`id`=@sku_id LIMIT 1;", new { sku_id }) ?? new SkuDetailViewModel();
    }

    public async Task<SkuDetailViewModel> GetSkuByBarCodeAsync(string bar_code)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync();
        return await connection.QueryFirstOrDefaultAsync<SkuDetailViewModel>($"{SkuDetailSql} WHERE k.`bar_code`=@bar_code LIMIT 1;", new { bar_code }) ?? new SkuDetailViewModel();
    }

    public async Task<(int id, string msg)> AddAsync(SpuBothViewModel viewModel, CurrentUser currentUser)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.Serializable);
        try
        {
            if (await connection.ExecuteScalarAsync<bool>("SELECT EXISTS(SELECT 1 FROM `wms_spu` WHERE `tenant_id`=@tenantId AND `spu_code`=@spuCode);",
                    new { tenantId = currentUser.tenant_id, spuCode = viewModel.spu_code }, transaction))
            {
                await transaction.RollbackAsync();
                return (0, DuplicateMessage(viewModel.spu_code));
            }
            var now = DateTime.Now;
            var id = await connection.ExecuteScalarAsync<int>("""
                INSERT INTO `wms_spu` (`spu_code`,`spu_name`,`spu_description`,`supplier_id`,`supplier_name`,`brand`,`origin`,
                    `length_unit`,`volume_unit`,`weight_unit`,`creator`,`create_time`,`last_update_time`,`is_valid`,`tenant_id`)
                VALUES (@spu_code,@spu_name,@spu_description,@supplier_id,@supplier_name,@brand,@origin,@length_unit,@volume_unit,
                    @weight_unit,@creator,@create_time,@last_update_time,@is_valid,@tenant_id); SELECT LAST_INSERT_ID();
                """, new
                {
                    viewModel.spu_code, viewModel.spu_name, viewModel.spu_description, viewModel.supplier_id,
                    viewModel.supplier_name, viewModel.brand, viewModel.origin, viewModel.length_unit, viewModel.volume_unit,
                    viewModel.weight_unit, creator = currentUser.user_name, create_time = now, last_update_time = now,
                    viewModel.is_valid, tenant_id = currentUser.tenant_id
                }, transaction);
            if (viewModel.detailList.Count > 0)
                await InsertSkusAsync(connection, transaction, id, viewModel.detailList, viewModel.length_unit, viewModel.volume_unit, now);
            await transaction.CommitAsync();
            return id > 0 ? (id, _stringLocalizer["save_success"]) : (0, _stringLocalizer["save_failed"]);
        }
        catch { await transaction.RollbackAsync(); throw; }
    }

    public async Task<(bool flag, string msg)> UpdateAsync(SpuBothViewModel viewModel)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.Serializable);
        try
        {
            var tenantId = await connection.QuerySingleOrDefaultAsync<long?>("SELECT `tenant_id` FROM `wms_spu` WHERE `id`=@id FOR UPDATE;", new { viewModel.id }, transaction);
            if (!tenantId.HasValue)
            {
                await transaction.RollbackAsync();
                return (false, _stringLocalizer["not_exists_entity"]);
            }
            if (await connection.ExecuteScalarAsync<bool>("SELECT EXISTS(SELECT 1 FROM `wms_spu` WHERE `id`<>@id AND `tenant_id`=@tenantId AND `spu_code`=@spuCode);",
                    new { viewModel.id, tenantId, spuCode = viewModel.spu_code }, transaction))
            {
                await transaction.RollbackAsync();
                return (false, DuplicateMessage(viewModel.spu_code));
            }
            var now = DateTime.Now;
            var qty = await connection.ExecuteAsync("""
                UPDATE `wms_spu` SET `spu_code`=@spu_code,`spu_name`=@spu_name,`spu_description`=@spu_description,
                    `supplier_id`=@supplier_id,`supplier_name`=@supplier_name,`brand`=@brand,`origin`=@origin,
                    `length_unit`=@length_unit,`volume_unit`=@volume_unit,`weight_unit`=@weight_unit,
                    `is_valid`=@is_valid,`last_update_time`=@lastUpdate WHERE `id`=@id;
                """, new
                {
                    viewModel.id, viewModel.spu_code, viewModel.spu_name, viewModel.spu_description, viewModel.supplier_id,
                    viewModel.supplier_name, viewModel.brand, viewModel.origin, viewModel.length_unit, viewModel.volume_unit,
                    viewModel.weight_unit, viewModel.is_valid, lastUpdate = now
                }, transaction);
            foreach (var sku in viewModel.detailList.Where(t => t.id > 0))
                qty += await connection.ExecuteAsync("""
                    UPDATE `wms_sku` SET `sku_code`=@sku_code,`sku_name`=@sku_name,`bar_code`=@bar_code,`weight`=@weight,
                        `lenght`=@lenght,`width`=@width,`height`=@height,`unit`=@unit,`cost`=@cost,`price`=@price,
                        `last_update_time`=@lastUpdate WHERE `id`=@id AND `spu_id`=@spuId;
                    """, new { sku.id, spuId = viewModel.id, sku.sku_code, sku.sku_name, sku.bar_code, sku.weight,
                        sku.lenght, sku.width, sku.height, sku.unit, sku.cost, sku.price, lastUpdate = now }, transaction);
            var newSkus = viewModel.detailList.Where(t => t.id == 0).ToList();
            if (newSkus.Count > 0)
                qty += await InsertSkusAsync(connection, transaction, viewModel.id, newSkus, viewModel.length_unit, viewModel.volume_unit, now);
            var deletedIds = viewModel.detailList.Where(t => t.id < 0).Select(t => -t.id).ToArray();
            if (deletedIds.Length > 0)
            {
                await connection.ExecuteAsync("DELETE FROM `wms_sku_safety_stock` WHERE `sku_id` IN @deletedIds;", new { deletedIds }, transaction);
                qty += await connection.ExecuteAsync("DELETE FROM `wms_sku` WHERE `spu_id`=@spuId AND `id` IN @deletedIds;", new { spuId = viewModel.id, deletedIds }, transaction);
            }
            var factor = ChangeLengthUnit(viewModel.length_unit, viewModel.volume_unit);
            qty += await connection.ExecuteAsync("""
                UPDATE `wms_sku` SET `volume`=ROUND(`lenght`*@factor*`width`*@factor*`height`*@factor,3) WHERE `spu_id`=@spuId;
                """, new { factor, spuId = viewModel.id }, transaction);
            await transaction.CommitAsync();
            return qty > 0 ? (true, _stringLocalizer["save_success"]) : (false, _stringLocalizer["save_failed"]);
        }
        catch { await transaction.RollbackAsync(); throw; }
    }

    public async Task<(bool flag, string msg)> DeleteAsync(int id)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.Serializable);
        try
        {
            if (await connection.ExecuteScalarAsync<bool>("SELECT EXISTS(SELECT 1 FROM `wms_asn` WHERE `spu_id`=@id);", new { id }, transaction))
            {
                await transaction.RollbackAsync();
                return (false, _stringLocalizer["delete_referenced"]);
            }
            await connection.ExecuteAsync("DELETE ss FROM `wms_sku_safety_stock` ss INNER JOIN `wms_sku` k ON k.`id`=ss.`sku_id` WHERE k.`spu_id`=@id;", new { id }, transaction);
            var qty = await connection.ExecuteAsync("DELETE FROM `wms_sku` WHERE `spu_id`=@id;", new { id }, transaction);
            qty += await connection.ExecuteAsync("DELETE FROM `wms_spu` WHERE `id`=@id;", new { id }, transaction);
            await transaction.CommitAsync();
            return qty > 0 ? (true, _stringLocalizer["delete_success"]) : (false, _stringLocalizer["delete_failed"]);
        }
        catch { await transaction.RollbackAsync(); throw; }
    }

    public async Task<(bool flag, string msg)> InsertOrUpdateSkuSafetyStockAsync(SkuSafetyStockPutViewModel viewModel)
    {
        if (viewModel.detailList.Count == 0) return (false, _stringLocalizer["save_failed"]);
        await using var connection = await _connectionFactory.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.Serializable);
        try
        {
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
            SELECT ss.`id`,ss.`sku_id`,ss.`safety_stock_qty`,ss.`warehouse_id`,w.`warehouse_name`
              FROM `wms_sku_safety_stock` ss INNER JOIN `wms_warehouse` w ON w.`id`=ss.`warehouse_id`
              INNER JOIN `wms_sku` k ON k.`id`=ss.`sku_id` WHERE k.`spu_id` IN @spuIds ORDER BY ss.`id`;
            """, new { spuIds });
        var skus = (await result.ReadAsync<SkuViewModel>()).AsList();
        var stocks = (await result.ReadAsync<SkuSafetyStockViewModel>()).AsList();
        var stocksBySku = stocks.GroupBy(t => t.sku_id).ToDictionary(t => t.Key, t => t.ToList());
        foreach (var sku in skus) if (stocksBySku.TryGetValue(sku.id, out var skuStocks)) sku.detailList = skuStocks;
        var skusBySpu = skus.GroupBy(t => t.spu_id).ToDictionary(t => t.Key, t => t.ToList());
        foreach (var row in rows) if (skusBySpu.TryGetValue(row.id, out var spuSkus)) row.detailList = spuSkus;
    }

    private static async Task<int> InsertSkusAsync(System.Data.Common.DbConnection connection,
        System.Data.Common.DbTransaction transaction, int spuId, List<SkuViewModel> skus, byte lengthUnit,
        byte volumeUnit, DateTime now)
    {
        var factor = ChangeLengthUnit(lengthUnit, volumeUnit);
        var rows = skus.Select(t => new
        {
            spu_id = spuId, t.sku_code, t.sku_name, t.bar_code, t.weight, t.lenght, t.width, t.height,
            volume = Math.Round(t.lenght * factor * t.width * factor * t.height * factor, 3),
            t.unit, t.cost, t.price, create_time = now, last_update_time = now
        }).ToList();
        return await connection.ExecuteAsync("""
            INSERT INTO `wms_sku` (`spu_id`,`sku_code`,`sku_name`,`bar_code`,`weight`,`lenght`,`width`,`height`,
                `volume`,`unit`,`cost`,`price`,`create_time`,`last_update_time`)
            VALUES (@spu_id,@sku_code,@sku_name,@bar_code,@weight,@lenght,@width,@height,@volume,@unit,@cost,@price,@create_time,@last_update_time);
            """, rows, transaction);
    }

    private string DuplicateMessage(string code) => string.Format(_stringLocalizer["exists_entity"], _stringLocalizer["spu_code"], code);
    private static decimal ChangeLengthUnit(byte lengthUnit, byte volumeUnit) => volumeUnit switch
    {
        0 => lengthUnit switch { 0 => 0.1M, 2 => 10M, 3 => 100M, _ => 1M },
        1 => lengthUnit switch { 0 => 0.01M, 2 => 1M, 3 => 10M, _ => 0.1M },
        2 => lengthUnit switch { 0 => 0.001M, 2 => 0.1M, 3 => 1M, _ => 0.01M },
        _ => 1M
    };
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
