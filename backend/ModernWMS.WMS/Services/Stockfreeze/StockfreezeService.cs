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

namespace ModernWMS.WMS.Services;

public class StockfreezeService : BaseService<StockfreezeEntity>, IStockfreezeService
{
    private const string ViewSql = """
        SELECT f.`id`,f.`job_code`,f.`job_type`,f.`sku_id`,f.`goods_owner_id`,f.`goods_location_id`,
               f.`handler`,f.`handle_time`,f.`last_update_time`,f.`tenant_id`,f.`series_number`,
               k.`sku_code`,p.`spu_code`,p.`spu_name`,l.`location_name`,l.`warehouse_name`
          FROM `wms_stockfreeze` f
          INNER JOIN `wms_sku` k ON k.`id`=f.`sku_id`
          INNER JOIN `wms_spu` p ON p.`id`=k.`spu_id`
          INNER JOIN `wms_goodslocation` l ON l.`id`=f.`goods_location_id`
        """;

    private static readonly IReadOnlyDictionary<string, string> SearchColumns =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["id"] = "f.`id`", ["job_code"] = "f.`job_code`", ["job_type"] = "f.`job_type`",
            ["sku_id"] = "f.`sku_id`", ["goods_owner_id"] = "f.`goods_owner_id`",
            ["goods_location_id"] = "f.`goods_location_id`", ["handler"] = "f.`handler`",
            ["handle_time"] = "f.`handle_time`", ["last_update_time"] = "f.`last_update_time`",
            ["tenant_id"] = "f.`tenant_id`", ["series_number"] = "f.`series_number`",
            ["sku_code"] = "k.`sku_code`", ["spu_code"] = "p.`spu_code`", ["spu_name"] = "p.`spu_name`",
            ["location_name"] = "l.`location_name`", ["warehouse_name"] = "l.`warehouse_name`"
        };

    private readonly IMySqlConnectionFactory _connectionFactory;
    private readonly IStringLocalizer<ModernWMS.Core.MultiLanguage> _stringLocalizer;
    private readonly FunctionHelper _functionHelper;

    public StockfreezeService(
        IMySqlConnectionFactory connectionFactory,
        IStringLocalizer<ModernWMS.Core.MultiLanguage> stringLocalizer,
        FunctionHelper functionHelper)
    {
        _connectionFactory = connectionFactory;
        _stringLocalizer = stringLocalizer;
        _functionHelper = functionHelper;
    }

    public async Task<(List<StockfreezeViewModel> data, int totals)> PageAsync(
        PageSearch pageSearch,
        CurrentUser currentUser)
    {
        var filter = DapperSearchBuilder.Build(pageSearch.searchObjects, SearchColumns);
        var where = "f.`tenant_id`=@tenantId" +
                    (string.IsNullOrWhiteSpace(filter.Sql) ? string.Empty : $" AND {filter.Sql}");
        filter.Parameters.Add("tenantId", currentUser.tenant_id);
        filter.Parameters.Add("offset", (pageSearch.pageIndex - 1) * pageSearch.pageSize);
        filter.Parameters.Add("pageSize", pageSearch.pageSize);

        await using var connection = await _connectionFactory.OpenConnectionAsync();
        using var result = await connection.QueryMultipleAsync($"""
            SELECT COUNT(*) FROM `wms_stockfreeze` f
            INNER JOIN `wms_sku` k ON k.`id`=f.`sku_id`
            INNER JOIN `wms_spu` p ON p.`id`=k.`spu_id`
            INNER JOIN `wms_goodslocation` l ON l.`id`=f.`goods_location_id`
            WHERE {where};
            {ViewSql} WHERE {where} ORDER BY f.`last_update_time` DESC LIMIT @pageSize OFFSET @offset;
            """, filter.Parameters);
        var totals = await result.ReadSingleAsync<int>();
        var rows = (await result.ReadAsync<StockfreezeViewModel>()).AsList();
        return (rows, totals);
    }

    public async Task<List<StockfreezeViewModel>> GetAllAsync(CurrentUser currentUser)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync();
        return (await connection.QueryAsync<StockfreezeViewModel>("""
            SELECT `id`,`job_code`,`job_type`,`sku_id`,`goods_owner_id`,`goods_location_id`,`handler`,
                   `handle_time`,`last_update_time`,`tenant_id`,`series_number`
              FROM `wms_stockfreeze` WHERE `tenant_id`=@tenantId;
            """, new { tenantId = currentUser.tenant_id })).AsList();
    }

    public async Task<StockfreezeViewModel> GetAsync(int id)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync();
        return await connection.QuerySingleOrDefaultAsync<StockfreezeViewModel>($"""
            {ViewSql} WHERE f.`id`=@id LIMIT 1;
            """, new { id });
    }

    public async Task<(int id, string msg)> AddAsync(StockfreezeViewModel viewModel, CurrentUser currentUser)
    {
        var jobCode = await _functionHelper.GetFormNoAsync("Stockfreeze");
        await using var connection = await _connectionFactory.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.Serializable);
        try
        {
            var parameters = new
            {
                viewModel.goods_location_id,
                viewModel.goods_owner_id,
                viewModel.sku_id,
                viewModel.series_number,
                tenantId = currentUser.tenant_id
            };
            if (await connection.ExecuteScalarAsync<bool>("""
                SELECT EXISTS(SELECT 1 FROM `wms_stockprocessdetail`
                    WHERE `goods_location_id`=@goods_location_id AND `goods_owner_id`=@goods_owner_id
                      AND `sku_id`=@sku_id AND `is_update_stock`=0 AND `tenant_id`=@tenantId);
                """, parameters, transaction))
            {
                await transaction.RollbackAsync();
                return (0, _stringLocalizer["process_not_comfirm"]);
            }
            if (await connection.ExecuteScalarAsync<bool>("""
                SELECT EXISTS(SELECT 1 FROM `wms_dispatchpicklist`
                    WHERE `goods_location_id`=@goods_location_id AND `sku_id`=@sku_id
                      AND `is_update_stock`=0);
                """, parameters, transaction))
            {
                await transaction.RollbackAsync();
                return (0, _stringLocalizer["dispatch_not_comfirm"]);
            }
            if (await connection.ExecuteScalarAsync<bool>("""
                SELECT EXISTS(SELECT 1 FROM `wms_stockmove`
                    WHERE (`orig_goods_location_id`=@goods_location_id OR `dest_googs_location_id`=@goods_location_id)
                      AND `sku_id`=@sku_id AND `move_status`=0 AND `tenant_id`=@tenantId);
                """, parameters, transaction))
            {
                await transaction.RollbackAsync();
                return (0, _stringLocalizer["move_not_comfirm"]);
            }

            var now = DateTime.Now;
            await connection.ExecuteAsync("""
                UPDATE `wms_stock` SET `is_freeze`=@isFreeze
                 WHERE `goods_location_id`=@goods_location_id AND `goods_owner_id`=@goods_owner_id
                   AND `sku_id`=@sku_id AND `series_number`=@series_number AND `tenant_id`=@tenantId;
                """, new
                {
                    isFreeze = viewModel.job_type,
                    viewModel.goods_location_id,
                    viewModel.goods_owner_id,
                    viewModel.sku_id,
                    viewModel.series_number,
                    tenantId = currentUser.tenant_id
                }, transaction);
            var id = await connection.ExecuteScalarAsync<int>("""
                INSERT INTO `wms_stockfreeze` (`job_code`,`job_type`,`sku_id`,`goods_owner_id`,`goods_location_id`,
                    `handler`,`handle_time`,`last_update_time`,`tenant_id`,`series_number`)
                VALUES (@jobCode,@jobType,@skuId,@goodsOwnerId,@goodsLocationId,@handler,@handleTime,@lastUpdate,
                    @tenantId,@seriesNumber); SELECT LAST_INSERT_ID();
                """, new
                {
                    jobCode,
                    jobType = viewModel.job_type,
                    skuId = viewModel.sku_id,
                    goodsOwnerId = viewModel.goods_owner_id,
                    goodsLocationId = viewModel.goods_location_id,
                    handler = currentUser.user_name,
                    handleTime = now,
                    lastUpdate = now,
                    tenantId = currentUser.tenant_id,
                    seriesNumber = viewModel.series_number
                }, transaction);
            await transaction.CommitAsync();
            return id > 0 ? (id, _stringLocalizer["save_success"]) : (0, _stringLocalizer["save_failed"]);
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    public async Task<(bool flag, string msg)> UpdateAsync(StockfreezeViewModel viewModel)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.Serializable);
        try
        {
            var existingId = await connection.QuerySingleOrDefaultAsync<int?>(
                "SELECT `id` FROM `wms_stockfreeze` WHERE `id`=@id FOR UPDATE;",
                new { viewModel.id }, transaction);
            if (!existingId.HasValue)
            {
                await transaction.RollbackAsync();
                return (false, _stringLocalizer["not_exists_entity"]);
            }
            var qty = await connection.ExecuteAsync("""
                UPDATE `wms_stockfreeze` SET `job_code`=@job_code,`job_type`=@job_type,`sku_id`=@sku_id,
                    `goods_owner_id`=@goods_owner_id,`goods_location_id`=@goods_location_id,`handler`=@handler,
                    `handle_time`=@handle_time,`last_update_time`=@lastUpdate,`series_number`=@series_number
                 WHERE `id`=@id;
                """, new
                {
                    viewModel.id, viewModel.job_code, viewModel.job_type, viewModel.sku_id, viewModel.goods_owner_id,
                    viewModel.goods_location_id, viewModel.handler, viewModel.handle_time,
                    lastUpdate = DateTime.Now, viewModel.series_number
                }, transaction);
            await transaction.CommitAsync();
            return qty > 0
                ? (true, _stringLocalizer["save_success"])
                : (false, _stringLocalizer["save_failed"]);
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    public async Task<(bool flag, string msg)> DeleteAsync(int id)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.Serializable);
        try
        {
            var qty = await connection.ExecuteAsync("DELETE FROM `wms_stockfreeze` WHERE `id`=@id;", new { id }, transaction);
            await transaction.CommitAsync();
            return qty > 0
                ? (true, _stringLocalizer["delete_success"])
                : (false, _stringLocalizer["delete_failed"]);
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    public async Task<string> GetOrderCode(CurrentUser currentUser)
    {
        var date = DateTime.Now.ToString("yyyyMMdd");
        await using var connection = await _connectionFactory.OpenConnectionAsync();
        var maxNo = await connection.QuerySingleAsync<string?>("""
            SELECT MAX(`job_code`) FROM `wms_stockfreeze` WHERE `tenant_id`=@tenantId;
            """, new { tenantId = currentUser.tenant_id });
        if (maxNo == null) return date + "-0001";
        var maxDate = maxNo.Substring(0, 8);
        var maxDateNo = maxNo.Substring(9, 4);
        if (date != maxDate) return date + "-0001";
        int.TryParse(maxDateNo, out var number);
        return date + "-" + (number + 1).ToString("0000");
    }
}
