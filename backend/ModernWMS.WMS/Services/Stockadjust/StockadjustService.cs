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

/// <summary>Stock adjustment service.</summary>
public class StockadjustService : BaseService<StockadjustEntity>, IStockadjustService
{
    private const string EntityColumns = """
        a.`id`,a.`job_code`,a.`sku_id`,a.`goods_owner_id`,a.`goods_location_id`,a.`qty`,a.`creator`,
        a.`create_time`,a.`last_update_time`,a.`tenant_id`,a.`is_update_stock`,a.`job_type`,a.`source_table_id`,
        a.`series_number`,a.`expiry_date`,a.`price`,a.`putaway_date`
        """;

    private const string PageSelect = """
        SELECT a.`id`,a.`job_code`,a.`is_update_stock`,a.`job_type`,a.`qty`,a.`source_table_id`,a.`tenant_id`,
               sku.`id` sku_id,sku.`sku_code`,sku.`sku_name`,spu.`spu_code`,spu.`spu_name`,
               a.`goods_location_id`,gl.`warehouse_name`,gl.`location_name`,a.`goods_owner_id`,
               COALESCE(go.`goods_owner_name`,'') goods_owner_name,a.`creator`,a.`create_time`,a.`last_update_time`,
               a.`series_number`,a.`expiry_date`,a.`price`,a.`putaway_date`
        FROM `wms_stockadjust` a
        JOIN `wms_sku` sku ON sku.`id`=a.`sku_id`
        JOIN `wms_spu` spu ON spu.`id`=sku.`spu_id`
        JOIN `wms_goodslocation` gl ON gl.`id`=a.`goods_location_id`
        LEFT JOIN `wms_goodsowner` go ON go.`id`=a.`goods_owner_id`
        WHERE a.`tenant_id`=@tenantId
        """;

    private static readonly IReadOnlyDictionary<string, string> SearchColumns = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["id"]="q.`id`", ["job_code"]="q.`job_code`", ["is_update_stock"]="q.`is_update_stock`",
        ["job_type"]="q.`job_type`", ["qty"]="q.`qty`", ["source_table_id"]="q.`source_table_id`",
        ["tenant_id"]="q.`tenant_id`", ["sku_id"]="q.`sku_id`", ["sku_code"]="q.`sku_code`",
        ["sku_name"]="q.`sku_name`", ["spu_code"]="q.`spu_code`", ["spu_name"]="q.`spu_name`",
        ["goods_location_id"]="q.`goods_location_id`", ["warehouse_name"]="q.`warehouse_name`",
        ["location_name"]="q.`location_name`", ["goods_owner_id"]="q.`goods_owner_id`",
        ["goods_owner_name"]="q.`goods_owner_name`", ["creator"]="q.`creator`", ["create_time"]="q.`create_time`",
        ["last_update_time"]="q.`last_update_time`", ["series_number"]="q.`series_number`",
        ["expiry_date"]="q.`expiry_date`", ["price"]="q.`price`", ["putaway_date"]="q.`putaway_date`"
    };

    private readonly IMySqlConnectionFactory _connectionFactory;
    private readonly IStringLocalizer<ModernWMS.Core.MultiLanguage> _stringLocalizer;

    public StockadjustService(IMySqlConnectionFactory connectionFactory,
        IStringLocalizer<ModernWMS.Core.MultiLanguage> stringLocalizer)
    {
        _connectionFactory = connectionFactory;
        _stringLocalizer = stringLocalizer;
    }

    public async Task<(List<StockadjustViewModel> data, int totals)> PageAsync(PageSearch pageSearch, CurrentUser currentUser)
    {
        var filter = DapperSearchBuilder.Build(pageSearch.searchObjects, SearchColumns);
        filter.Parameters.Add("tenantId", currentUser.tenant_id);
        filter.Parameters.Add("offset", (pageSearch.pageIndex - 1) * pageSearch.pageSize);
        filter.Parameters.Add("pageSize", pageSearch.pageSize);
        var where = filter.Sql.Length == 0 ? "1=1" : filter.Sql;

        await using var connection = await _connectionFactory.OpenConnectionAsync();
        using var result = await connection.QueryMultipleAsync($"""
            SELECT COUNT(*) FROM ({PageSelect}) q WHERE {where};
            SELECT q.* FROM ({PageSelect}) q WHERE {where}
            ORDER BY q.`create_time` DESC LIMIT @pageSize OFFSET @offset;
            """, filter.Parameters);
        var totals = await result.ReadSingleAsync<int>();
        return ((await result.ReadAsync<StockadjustViewModel>()).AsList(), totals);
    }

    public async Task<List<StockadjustViewModel>> GetAllAsync(CurrentUser currentUser)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync();
        return (await connection.QueryAsync<StockadjustViewModel>($"""
            SELECT {EntityColumns} FROM `wms_stockadjust` a WHERE a.`tenant_id`=@tenantId;
            """, new { tenantId = currentUser.tenant_id })).AsList();
    }

    public async Task<StockadjustViewModel> GetAsync(int id)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync();
        return await connection.QuerySingleOrDefaultAsync<StockadjustViewModel>($"""
            SELECT {EntityColumns} FROM `wms_stockadjust` a WHERE a.`id`=@id LIMIT 1;
            """, new { id });
    }

    public async Task<(int id, string msg)> AddAsync(StockadjustViewModel viewModel, CurrentUser currentUser)
    {
        var now = DateTime.Now;
        await using var connection = await _connectionFactory.OpenConnectionAsync();
        var id = await connection.ExecuteScalarAsync<int>("""
            INSERT INTO `wms_stockadjust`
              (`job_code`,`sku_id`,`goods_owner_id`,`goods_location_id`,`qty`,`creator`,`create_time`,`last_update_time`,
               `tenant_id`,`is_update_stock`,`job_type`,`source_table_id`,`series_number`,`expiry_date`,`price`,`putaway_date`)
            VALUES
              (@job_code,@sku_id,@goods_owner_id,@goods_location_id,@qty,@creator,@now,@now,
               @tenantId,@is_update_stock,@job_type,@source_table_id,@series_number,@expiry_date,@price,@putaway_date);
            SELECT LAST_INSERT_ID();
            """, new
        {
            viewModel.job_code, viewModel.sku_id, viewModel.goods_owner_id, viewModel.goods_location_id,
            viewModel.qty, creator = currentUser.user_name, now, tenantId = currentUser.tenant_id,
            viewModel.is_update_stock, viewModel.job_type, viewModel.source_table_id, viewModel.series_number,
            viewModel.expiry_date, viewModel.price, viewModel.putaway_date
        });
        return id > 0 ? (id, _stringLocalizer["save_success"]) : (0, _stringLocalizer["save_failed"]);
    }

    public async Task<(bool flag, string msg)> UpdateAsync(StockadjustViewModel viewModel)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync();
        var affected = await connection.ExecuteAsync("""
            UPDATE `wms_stockadjust` SET
              `job_code`=@job_code,`sku_id`=@sku_id,`goods_owner_id`=@goods_owner_id,
              `goods_location_id`=@goods_location_id,`qty`=@qty,`is_update_stock`=@is_update_stock,
              `job_type`=@job_type,`source_table_id`=@source_table_id,`last_update_time`=@now,
              `series_number`=@series_number,`expiry_date`=@expiry_date,`price`=@price,`putaway_date`=@putaway_date
            WHERE `id`=@id;
            """, new
        {
            viewModel.id, viewModel.job_code, viewModel.sku_id, viewModel.goods_owner_id,
            viewModel.goods_location_id, viewModel.qty, viewModel.is_update_stock, viewModel.job_type,
            viewModel.source_table_id, now = DateTime.Now, viewModel.series_number, viewModel.expiry_date,
            viewModel.price, viewModel.putaway_date
        });
        return affected > 0
            ? (true, _stringLocalizer["save_success"])
            : (false, _stringLocalizer["not_exists_entity"]);
    }

    public async Task<(bool flag, string msg)> DeleteAsync(int id)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync();
        var affected = await connection.ExecuteAsync("DELETE FROM `wms_stockadjust` WHERE `id`=@id;", new { id });
        return affected > 0
            ? (true, _stringLocalizer["delete_success"])
            : (false, _stringLocalizer["not_exists_entity"]);
    }

    public async Task<(bool flag, string msg)> ConfirmAdjustment(int id)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted);
        try
        {
            var adjustment = await connection.QuerySingleOrDefaultAsync<StockadjustEntity>("""
                SELECT * FROM `wms_stockadjust` WHERE `id`=@id FOR UPDATE;
                """, new { id }, transaction);
            if (adjustment == null)
            {
                await transaction.RollbackAsync();
                return (false, _stringLocalizer["not_exists_entity"]);
            }

            var now = DateTime.Now;
            var affected = 0;
            if (adjustment.job_type == 2)
            {
                affected += await connection.ExecuteAsync("""
                    UPDATE `wms_stockprocessdetail` SET `last_update_time`=@now,`is_update_stock`=1
                    WHERE `id`=@sourceId;
                    """, new { now, sourceId = adjustment.source_table_id }, transaction);
            }

            // The previous tracked implementation only updated an existing stock row; it did not attach a newly
            // constructed row. Keep that persisted behavior during this data-access-only migration.
            affected += await connection.ExecuteAsync("""
                UPDATE `wms_stock` SET `qty`=`qty`+@qty,`goods_owner_id`=@goods_owner_id,`last_update_time`=@now
                WHERE `goods_owner_id`=@goods_owner_id AND `series_number`=@series_number
                  AND `goods_location_id`=@goods_location_id AND `sku_id`=@sku_id
                  AND `expiry_date`=@expiry_date AND `price`=@price AND `putaway_date`=@putaway_date
                LIMIT 1;
                """, new
            {
                adjustment.qty, adjustment.goods_owner_id, adjustment.series_number,
                adjustment.goods_location_id, adjustment.sku_id, adjustment.expiry_date,
                adjustment.price, adjustment.putaway_date, now
            }, transaction);

            affected += await connection.ExecuteAsync("""
                UPDATE `wms_stockadjust` SET `is_update_stock`=1,`last_update_time`=@now WHERE `id`=@id;
                """, new { id, now }, transaction);
            await transaction.CommitAsync();
            return affected > 0
                ? (true, _stringLocalizer["operation_success"])
                : (false, _stringLocalizer["operation_failed"]);
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }
}
