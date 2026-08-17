using System.Data;
using Dapper;
using Microsoft.Extensions.Localization;
using ModernWMS.Core;
using ModernWMS.Core.Database;
using ModernWMS.Core.JWT;
using ModernWMS.Core.Models;
using ModernWMS.Core.Services;
using ModernWMS.WMS.Entities.Models;
using ModernWMS.WMS.Entities.ViewModels;
using ModernWMS.WMS.IServices;
using MySqlConnector;

namespace ModernWMS.WMS.Services;

public class StockprocessService : BaseService<StockprocessEntity>, IStockprocessService
{
    private static readonly IReadOnlyDictionary<string, string> SearchColumns =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["id"] = "m.`id`", ["job_code"] = "m.`job_code`", ["job_type"] = "m.`job_type`",
            ["process_status"] = "m.`process_status`", ["processor"] = "m.`processor`",
            ["process_time"] = "m.`process_time`", ["creator"] = "m.`creator`",
            ["create_time"] = "m.`create_time`", ["last_update_time"] = "m.`last_update_time`",
            ["tenant_id"] = "m.`tenant_id`",
            ["adjust_status"] = "(m.`process_status` AND EXISTS(SELECT 1 FROM `wms_stockadjust` sa INNER JOIN `wms_stockprocessdetail` sd ON sd.`id`=sa.`source_table_id` WHERE sa.`job_type`=2 AND sd.`stock_process_id`=m.`id`))"
        };

    private const string MasterProjection = """
        m.`id`,m.`job_code`,m.`job_type`,m.`process_status`,m.`processor`,m.`process_time`,
        m.`creator`,m.`create_time`,m.`last_update_time`,m.`tenant_id`,
        (m.`process_status` AND EXISTS(
            SELECT 1 FROM `wms_stockadjust` a
            INNER JOIN `wms_stockprocessdetail` d ON d.`id`=a.`source_table_id`
            WHERE a.`job_type`=2 AND d.`stock_process_id`=m.`id`)) AS `adjust_status`
        """;

    private readonly IMySqlConnectionFactory _connectionFactory;
    private readonly IStringLocalizer<Core.MultiLanguage> _stringLocalizer;
    private readonly FunctionHelper _functionHelper;

    public StockprocessService(IMySqlConnectionFactory connectionFactory,
        IStringLocalizer<Core.MultiLanguage> stringLocalizer, FunctionHelper functionHelper)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        _stringLocalizer = stringLocalizer ?? throw new ArgumentNullException(nameof(stringLocalizer));
        _functionHelper = functionHelper ?? throw new ArgumentNullException(nameof(functionHelper));
    }

    public async Task<(List<StockprocessGetViewModel> data, int totals)> PageAsync(
        PageSearch pageSearch, CurrentUser currentUser)
    {
        var where = DapperSearchBuilder.Build(pageSearch.searchObjects, SearchColumns);
        where.Parameters.Add("tenantId", currentUser.tenant_id);
        where.Parameters.Add("offset", Math.Max(0, (pageSearch.pageIndex - 1) * pageSearch.pageSize));
        where.Parameters.Add("pageSize", pageSearch.pageSize);
        var filter = string.IsNullOrWhiteSpace(where.Sql) ? string.Empty : $" AND {where.Sql}";
        await using var connection = await _connectionFactory.OpenConnectionAsync();
        using var grid = await connection.QueryMultipleAsync($"""
            SELECT COUNT(*) FROM `wms_stockprocess` m WHERE m.`tenant_id`=@tenantId{filter};
            SELECT {MasterProjection} FROM `wms_stockprocess` m
            WHERE m.`tenant_id`=@tenantId{filter}
            ORDER BY m.`last_update_time` DESC LIMIT @pageSize OFFSET @offset;
            """, where.Parameters);
        var totals = await grid.ReadSingleAsync<int>();
        return ((await grid.ReadAsync<StockprocessGetViewModel>()).AsList(), totals);
    }

    public async Task<List<StockprocessGetViewModel>> GetAllAsync(CurrentUser currentUser)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync();
        return (await connection.QueryAsync<StockprocessGetViewModel>($"""
            SELECT {MasterProjection} FROM `wms_stockprocess` m WHERE m.`tenant_id`=@tenantId;
            """, new { tenantId = currentUser.tenant_id })).AsList();
    }

    public async Task<StockprocessWithDetailViewModel> GetAsync(int id)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync();
        var master = await connection.QuerySingleOrDefaultAsync<StockprocessWithDetailViewModel>($"""
            SELECT {MasterProjection} FROM `wms_stockprocess` m WHERE m.`id`=@id LIMIT 1;
            """, new { id });
        if (master == null) return null;
        var details = (await connection.QueryAsync<StockprocessdetailViewModel>("""
            SELECT d.`id`,d.`stock_process_id`,d.`sku_id`,d.`goods_owner_id`,d.`goods_location_id`,
                d.`qty`,d.`last_update_time`,d.`tenant_id`,d.`is_source`,d.`is_update_stock`,
                d.`series_number`,d.`expiry_date`,d.`price`,d.`putaway_date`,
                sku.`sku_code`,spu.`spu_code`,spu.`spu_name`,sku.`unit`,COALESCE(gl.`location_name`,'') `location_name`
            FROM `wms_stockprocessdetail` d
            INNER JOIN `wms_sku` sku ON sku.`id`=d.`sku_id`
            INNER JOIN `wms_spu` spu ON spu.`id`=sku.`spu_id`
            LEFT JOIN `wms_goodslocation` gl ON gl.`id`=d.`goods_location_id`
            WHERE d.`stock_process_id`=@id;
            """, new { id })).AsList();
        master.source_detail_list = details.Where(x => x.is_source).ToList();
        master.target_detail_list = details.Where(x => !x.is_source).ToList();
        return master;
    }

    public async Task<(int id, string msg)> AddAsync(StockprocessViewModel viewModel, CurrentUser currentUser)
    {
        var jobCode = await _functionHelper.GetFormNoAsync("Stockprocess");
        await using var connection = await _connectionFactory.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.Serializable);
        try
        {
            var details = viewModel.detailList ?? [];
            var sources = details.Where(x => x.is_source).ToList();
            var stocks = await LoadStocksAsync(connection, transaction, sources, currentUser.tenant_id, true);
            var locked = await LoadLockedAsync(connection, transaction, sources, currentUser.tenant_id);
            foreach (var detail in sources)
            {
                var stock = stocks.FirstOrDefault(x => SameStock(x, detail));
                var lockedQty = locked.Where(x => SameDetail(x, detail)).Sum(x => x.qty);
                if (stock == null || stock.qty - lockedQty < detail.qty)
                    return await Rollback(0, _stringLocalizer["data_changed"], transaction);
                if (stock.is_freeze)
                    return await Rollback(0, _stringLocalizer["stock_frozen"], transaction);
            }

            var now = DateTime.Now;
            var id = await connection.ExecuteScalarAsync<int>("""
                INSERT INTO `wms_stockprocess`
                    (`job_code`,`job_type`,`process_status`,`processor`,`process_time`,`creator`,`create_time`,`last_update_time`,`tenant_id`)
                VALUES (@jobCode,@jobType,@processStatus,@processor,@processTime,@creator,@now,@now,@tenantId);
                SELECT LAST_INSERT_ID();
                """, new { jobCode, jobType = viewModel.job_type, processStatus = viewModel.process_status,
                    viewModel.processor, viewModel.process_time, creator = currentUser.user_name, now,
                    tenantId = currentUser.tenant_id }, transaction);
            foreach (var detail in details)
                await InsertDetailAsync(connection, transaction, id, detail, currentUser.tenant_id, now);
            await transaction.CommitAsync();
            return id > 0 ? (id, _stringLocalizer["save_success"]) : (0, _stringLocalizer["save_failed"]);
        }
        catch { await transaction.RollbackAsync(); throw; }
    }

    public async Task<(bool flag, string msg)> UpdateAsync(StockprocessViewModel viewModel)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted);
        try
        {
            var exists = await connection.ExecuteScalarAsync<bool>(
                "SELECT EXISTS(SELECT 1 FROM `wms_stockprocess` WHERE `id`=@id FOR UPDATE);",
                new { viewModel.id }, transaction);
            if (!exists) return await Rollback(false, _stringLocalizer["not_exists_entity"], transaction);
            var affected = await connection.ExecuteAsync("""
                UPDATE `wms_stockprocess` SET `job_code`=@job_code,`job_type`=@job_type,
                    `process_status`=@process_status,`processor`=@processor,`process_time`=@process_time,
                    `last_update_time`=@now WHERE `id`=@id;
                """, new { viewModel.id, viewModel.job_code, viewModel.job_type, viewModel.process_status,
                    viewModel.processor, viewModel.process_time, now = DateTime.Now }, transaction);
            await transaction.CommitAsync();
            return affected > 0 ? (true, _stringLocalizer["save_success"]) : (false, _stringLocalizer["save_failed"]);
        }
        catch { await transaction.RollbackAsync(); throw; }
    }

    public async Task<(bool flag, string msg)> DeleteAsync(int id)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted);
        try
        {
            var exists = await connection.ExecuteScalarAsync<bool>("""
                SELECT EXISTS(SELECT 1 FROM `wms_stockprocess`
                WHERE `id`=@id AND `process_status`=0 FOR UPDATE);
                """, new { id }, transaction);
            if (!exists) return await Rollback(false, _stringLocalizer["delete_failed"], transaction);
            await connection.ExecuteAsync("DELETE FROM `wms_stockprocessdetail` WHERE `stock_process_id`=@id;", new { id }, transaction);
            var affected = await connection.ExecuteAsync("DELETE FROM `wms_stockprocess` WHERE `id`=@id AND `process_status`=0;", new { id }, transaction);
            await transaction.CommitAsync();
            return affected > 0 ? (true, _stringLocalizer["delete_success"]) : (false, _stringLocalizer["delete_failed"]);
        }
        catch { await transaction.RollbackAsync(); throw; }
    }

    public async Task<(bool flag, string msg)> ConfirmAdjustment(int id, CurrentUser currentUser)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.Serializable);
        try
        {
            var entity = await connection.QuerySingleOrDefaultAsync<StockprocessEntity>("""
                SELECT `id`,`job_code`,`job_type`,`process_status`,`processor`,`process_time`,`creator`,`create_time`,`last_update_time`,`tenant_id`
                FROM `wms_stockprocess` WHERE `id`=@id AND `tenant_id`=@tenantId FOR UPDATE;
                """, new { id, tenantId = currentUser.tenant_id }, transaction);
            if (entity == null) return await Rollback(false, _stringLocalizer["not_exists_entity"], transaction);
            var adjusted = await connection.ExecuteScalarAsync<bool>("""
                SELECT EXISTS(SELECT 1 FROM `wms_stockadjust` a
                INNER JOIN `wms_stockprocessdetail` d ON d.`id`=a.`source_table_id`
                WHERE a.`job_type`=2 AND d.`stock_process_id`=@id);
                """, new { id }, transaction);
            if (entity.process_status && adjusted)
                return await Rollback(false, _stringLocalizer["status_changed"], transaction);

            var details = (await connection.QueryAsync<StockprocessdetailEntity>("""
                SELECT `id`,`stock_process_id`,`sku_id`,`goods_owner_id`,`goods_location_id`,`qty`,
                    `last_update_time`,`tenant_id`,`is_source`,`is_update_stock`,`series_number`,`expiry_date`,`price`,`putaway_date`
                FROM `wms_stockprocessdetail` WHERE `stock_process_id`=@id AND `tenant_id`=@tenantId FOR UPDATE;
                """, new { id, tenantId = currentUser.tenant_id }, transaction)).AsList();
            var adjustmentPutawayDates = details.ToDictionary(x => x.id, x => x.putaway_date);
            var stocks = await LoadStocksAsync(connection, transaction, details, currentUser.tenant_id, true);
            var now = DateTime.Now;
            foreach (var detail in details)
            {
                var stock = stocks.FirstOrDefault(x => SameStock(x, detail));
                if (detail.is_source)
                {
                    if (stock == null || stock.qty < detail.qty)
                        return await Rollback(false, _stringLocalizer["data_changed"], transaction);
                    await connection.ExecuteAsync("UPDATE `wms_stock` SET `qty`=`qty`-@qty,`last_update_time`=@now WHERE `id`=@id;",
                        new { detail.qty, now, stock.id }, transaction);
                    stock.qty -= detail.qty;
                }
                else if (stock == null)
                {
                    detail.putaway_date = DateTime.Today;
                    await connection.ExecuteAsync("""
                        INSERT INTO `wms_stock` (`sku_id`,`goods_location_id`,`qty`,`goods_owner_id`,`is_freeze`,
                            `last_update_time`,`tenant_id`,`series_number`,`expiry_date`,`price`,`putaway_date`)
                        VALUES (@sku_id,@goods_location_id,@qty,@goods_owner_id,0,@now,@tenantId,
                            @series_number,@expiry_date,@price,@putaway_date);
                        """, new { detail.sku_id, detail.goods_location_id, detail.qty, detail.goods_owner_id, now,
                            tenantId = currentUser.tenant_id, detail.series_number, detail.expiry_date, detail.price, detail.putaway_date }, transaction);
                }
                else
                {
                    detail.putaway_date = DateTime.Today;
                    await connection.ExecuteAsync("UPDATE `wms_stock` SET `qty`=`qty`+@qty,`last_update_time`=@now WHERE `id`=@id;",
                        new { detail.qty, now, stock.id }, transaction);
                    stock.qty += detail.qty;
                }
                await connection.ExecuteAsync("""
                    UPDATE `wms_stockprocessdetail` SET `is_update_stock`=1,`last_update_time`=@now,`putaway_date`=@putawayDate
                    WHERE `id`=@id;
                    """, new { detail.id, now, putawayDate = detail.putaway_date }, transaction);
            }

            var adjustCode = await GetNextCodeAsync(connection, transaction, "wms_stockadjust", currentUser.tenant_id);
            foreach (var detail in details)
                await connection.ExecuteAsync("""
                    INSERT INTO `wms_stockadjust` (`job_code`,`sku_id`,`goods_owner_id`,`goods_location_id`,`qty`,`creator`,
                        `create_time`,`last_update_time`,`tenant_id`,`is_update_stock`,`job_type`,`source_table_id`,
                        `series_number`,`expiry_date`,`price`,`putaway_date`)
                    VALUES (@jobCode,@sku_id,@goods_owner_id,@goods_location_id,@qty,@creator,@now,@now,@tenantId,1,2,@id,
                        @series_number,@expiry_date,@price,@putaway_date);
                    """, new { jobCode = adjustCode, detail.sku_id, detail.goods_owner_id, detail.goods_location_id,
                        qty = detail.is_source ? -detail.qty : detail.qty, creator = currentUser.user_name, now,
                        tenantId = currentUser.tenant_id, detail.id, detail.series_number, detail.expiry_date,
                        detail.price, putaway_date = adjustmentPutawayDates[detail.id] }, transaction);
            await connection.ExecuteAsync("UPDATE `wms_stockprocess` SET `last_update_time`=@now WHERE `id`=@id;", new { now, id }, transaction);
            await transaction.CommitAsync();
            return (true, _stringLocalizer["operation_success"]);
        }
        catch { await transaction.RollbackAsync(); throw; }
    }

    public async Task<(bool flag, string msg)> ConfirmProcess(int id, CurrentUser currentUser)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted);
        try
        {
            var status = await connection.QuerySingleOrDefaultAsync<bool?>("""
                SELECT `process_status` FROM `wms_stockprocess`
                WHERE `id`=@id AND `tenant_id`=@tenantId FOR UPDATE;
                """, new { id, tenantId = currentUser.tenant_id }, transaction);
            if (status == null) return await Rollback(false, _stringLocalizer["not_exists_entity"], transaction);
            if (status.Value) return await Rollback(false, _stringLocalizer["status_changed"], transaction);
            var now = DateTime.Now;
            var affected = await connection.ExecuteAsync("""
                UPDATE `wms_stockprocess` SET `process_status`=1,`processor`=@processor,
                    `process_time`=@now,`last_update_time`=@now WHERE `id`=@id AND `tenant_id`=@tenantId;
                """, new { id, tenantId = currentUser.tenant_id, processor = currentUser.user_name, now }, transaction);
            await transaction.CommitAsync();
            return affected > 0 ? (true, _stringLocalizer["operation_success"]) : (false, _stringLocalizer["operation_failed"]);
        }
        catch { await transaction.RollbackAsync(); throw; }
    }

    public async Task<string> GetOrderCode(CurrentUser currentUser)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync();
        return await GetNextCodeAsync(connection, null, "wms_stockprocess", currentUser.tenant_id);
    }

    public async Task<string> GetAdjustOrderCode(CurrentUser currentUser)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync();
        return await GetNextCodeAsync(connection, null, "wms_stockadjust", currentUser.tenant_id);
    }

    private static Task<List<StockEntity>> LoadStocksAsync(MySqlConnection connection, IDbTransaction transaction,
        IEnumerable<StockprocessdetailViewModel> details, long tenantId, bool forUpdate) =>
        LoadStocksByKeysAsync(connection, transaction,
            details.Select(x => new StockLookupKey(x.goods_location_id, x.sku_id)), tenantId, forUpdate);

    private static Task<List<StockEntity>> LoadStocksAsync(MySqlConnection connection, IDbTransaction transaction,
        IEnumerable<StockprocessdetailEntity> details, long tenantId, bool forUpdate) =>
        LoadStocksByKeysAsync(connection, transaction,
            details.Select(x => new StockLookupKey(x.goods_location_id, x.sku_id)), tenantId, forUpdate);

    private static async Task<List<StockEntity>> LoadStocksByKeysAsync(MySqlConnection connection,
        IDbTransaction transaction, IEnumerable<StockLookupKey> details, long tenantId, bool forUpdate)
    {
        var values = details.ToList();
        if (values.Count == 0) return [];
        int[] locationIds = values.Select(x => x.GoodsLocationId).Distinct().ToArray();
        int[] skuIds = values.Select(x => x.SkuId).Distinct().ToArray();
        return (await connection.QueryAsync<StockEntity>($"""
            SELECT `id`,`sku_id`,`goods_location_id`,`qty`,`goods_owner_id`,`is_freeze`,`last_update_time`,
                `tenant_id`,`series_number`,`expiry_date`,`price`,`putaway_date`
            FROM `wms_stock` WHERE `tenant_id`=@tenantId AND `goods_location_id` IN @locationIds AND `sku_id` IN @skuIds
            {(forUpdate ? "FOR UPDATE" : string.Empty)};
            """, new { tenantId, locationIds, skuIds }, transaction)).AsList();
    }

    private static async Task<List<StockprocessdetailEntity>> LoadLockedAsync(MySqlConnection connection,
        IDbTransaction transaction, List<StockprocessdetailViewModel> details, long tenantId)
    {
        if (details.Count == 0) return [];
        var locationIds = details.Select(x => x.goods_location_id).Distinct().ToArray();
        var skuIds = details.Select(x => x.sku_id).Distinct().ToArray();
        return (await connection.QueryAsync<StockprocessdetailEntity>("""
            SELECT `sku_id`,`goods_location_id`,`goods_owner_id`,`series_number`,`expiry_date`,`price`,`putaway_date`,SUM(`qty`) `qty`
            FROM `wms_stockprocessdetail` WHERE `tenant_id`=@tenantId AND `is_update_stock`=0
                AND `goods_location_id` IN @locationIds AND `sku_id` IN @skuIds
            GROUP BY `sku_id`,`goods_location_id`,`goods_owner_id`,`series_number`,`expiry_date`,`price`,`putaway_date`
            FOR UPDATE;
            """, new { tenantId, locationIds, skuIds }, transaction)).AsList();
    }

    private static bool SameStock(StockEntity stock, StockprocessdetailViewModel detail) =>
        stock.sku_id == detail.sku_id && stock.goods_location_id == detail.goods_location_id
        && stock.goods_owner_id == detail.goods_owner_id && stock.series_number == detail.series_number
        && stock.expiry_date == detail.expiry_date && stock.price == detail.price && stock.putaway_date == detail.putaway_date;

    private static bool SameStock(StockEntity stock, StockprocessdetailEntity detail) =>
        stock.sku_id == detail.sku_id && stock.goods_location_id == detail.goods_location_id
        && stock.goods_owner_id == detail.goods_owner_id && stock.series_number == detail.series_number
        && stock.expiry_date == detail.expiry_date && stock.price == detail.price
        && stock.putaway_date == detail.putaway_date;

    private static bool SameDetail(StockprocessdetailEntity locked, StockprocessdetailViewModel detail) =>
        locked.sku_id == detail.sku_id && locked.goods_location_id == detail.goods_location_id
        && locked.goods_owner_id == detail.goods_owner_id && locked.series_number == detail.series_number
        && locked.expiry_date == detail.expiry_date && locked.price == detail.price
        && locked.putaway_date == detail.putaway_date;

    private static Task InsertDetailAsync(MySqlConnection connection, IDbTransaction transaction, int processId,
        StockprocessdetailViewModel detail, long tenantId, DateTime now) => connection.ExecuteAsync("""
            INSERT INTO `wms_stockprocessdetail` (`stock_process_id`,`sku_id`,`goods_owner_id`,`goods_location_id`,`qty`,
                `last_update_time`,`tenant_id`,`is_source`,`is_update_stock`,`series_number`,`expiry_date`,`price`,`putaway_date`)
            VALUES (@processId,@sku_id,@goods_owner_id,@goods_location_id,@qty,@now,@tenantId,@is_source,@is_update_stock,
                @series_number,@expiry_date,@price,@putaway_date);
            """, new { processId, detail.sku_id, detail.goods_owner_id, detail.goods_location_id, detail.qty, now, tenantId,
                detail.is_source, detail.is_update_stock, detail.series_number, detail.expiry_date, detail.price, detail.putaway_date }, transaction);

    private static async Task<string> GetNextCodeAsync(MySqlConnection connection, IDbTransaction transaction,
        string tableName, long tenantId)
    {
        var maxNo = await connection.QuerySingleOrDefaultAsync<string>(
            $"SELECT MAX(`job_code`) FROM `{tableName}` WHERE `tenant_id`=@tenantId;", new { tenantId }, transaction);
        var date = DateTime.Now.ToString("yyyyMMdd");
        if (string.IsNullOrEmpty(maxNo) || maxNo.Length < 13 || maxNo[..8] != date) return date + "-0001";
        int.TryParse(maxNo.Substring(9, 4), out var number);
        return date + "-" + (number + 1).ToString("0000");
    }

    private static async Task<(T value, string msg)> Rollback<T>(T value, string msg, MySqlTransaction transaction)
    {
        await transaction.RollbackAsync();
        return (value, msg);
    }

    private sealed record StockLookupKey(int GoodsLocationId, int SkuId);
}
