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

/// <summary>
/// 表示 GoodslocationService 类型。
/// </summary>
public class GoodslocationService : BaseService<GoodslocationEntity>, IGoodslocationService
{
    private const string Projection = """
        gl.`id`, gl.`warehouse_id`, gl.`warehouse_name`, gl.`warehouse_area_name`,
        gl.`warehouse_area_property`, gl.`location_name`, gl.`location_length`, gl.`location_width`,
        gl.`location_heigth`, gl.`location_volume`, gl.`location_load`, gl.`roadway_number`,
        gl.`shelf_number`, gl.`layer_number`, gl.`tag_number`, gl.`create_time`,
        gl.`last_update_time`, gl.`is_valid`, gl.gl.`warehouse_area_id`
        """;

    private static readonly IReadOnlyDictionary<string, string> SearchColumns =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["id"] = "gl.`id`", ["warehouse_id"] = "gl.`warehouse_id`",
            ["warehouse_name"] = "gl.`warehouse_name`", ["warehouse_area_name"] = "gl.`warehouse_area_name`",
            ["warehouse_area_property"] = "gl.`warehouse_area_property`", ["location_name"] = "gl.`location_name`",
            ["location_length"] = "gl.`location_length`", ["location_width"] = "gl.`location_width`",
            ["location_heigth"] = "gl.`location_heigth`", ["location_volume"] = "gl.`location_volume`",
            ["location_load"] = "gl.`location_load`", ["roadway_number"] = "gl.`roadway_number`",
            ["shelf_number"] = "gl.`shelf_number`", ["layer_number"] = "gl.`layer_number`",
            ["tag_number"] = "gl.`tag_number`", ["create_time"] = "gl.`create_time`",
            ["last_update_time"] = "gl.`last_update_time`", ["is_valid"] = "gl.`is_valid`",
        };

    private readonly IMySqlConnectionFactory _connectionFactory;
    private readonly IStringLocalizer<ModernWMS.Core.MultiLanguage> _stringLocalizer;

    /// <summary>
    /// 初始化 GoodslocationService 的新实例。
    /// </summary>
    public GoodslocationService(
        IMySqlConnectionFactory connectionFactory,
        IStringLocalizer<ModernWMS.Core.MultiLanguage> stringLocalizer)
    {
        _connectionFactory = connectionFactory;
        _stringLocalizer = stringLocalizer;
    }

    /// <summary>
    /// 执行 GetGoodslocationByWarehouse_area_id 操作。
    /// </summary>
    public async Task<List<FormSelectItem>> GetGoodslocationByWarehouse_area_id(
        int warehouse_area_id, CurrentUser currentUser)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync();
        var rows = await connection.QueryAsync<FormSelectItem>("""
            SELECT 'goodslocation' AS `code`,
                   'goodslocations of the warehousearea' AS `comments`,
                   gl.`location_name` AS `name`, CAST(gl.`id` AS CHAR) AS `value`
            FROM `wms_goodslocation` AS gl
            WHERE gl.`is_valid` = 1
              AND gl.`warehouse_area_id` = @warehouse_area_id;
            """, new { warehouse_area_id });
        return rows.AsList();
    }

    /// <summary>
    /// 执行 PageAsync 操作。
    /// </summary>
    public async Task<(List<GoodslocationViewModel> data, int totals)> PageAsync(
        PageSearch pageSearch, CurrentUser currentUser)
    {
        var filter = DapperSearchBuilder.Build(pageSearch.searchObjects, SearchColumns);
        var clauses = new List<string>();
        if (!string.IsNullOrWhiteSpace(filter.Sql)) clauses.Add(filter.Sql);
        if (pageSearch.sqlTitle == "select") clauses.Add("gl.`is_valid` = 1");
        filter.Parameters.Add("offset", (pageSearch.pageIndex - 1) * pageSearch.pageSize);
        filter.Parameters.Add("page_size", pageSearch.pageSize);
        var where = clauses.Count == 0 ? "1=1" : string.Join(" AND ", clauses);

        await using var connection = await _connectionFactory.OpenConnectionAsync();
        using var result = await connection.QueryMultipleAsync($"""
            SELECT COUNT(*) FROM `wms_goodslocation` AS gl WHERE {where};
            SELECT {Projection}
            FROM `wms_goodslocation` AS gl
            WHERE {where}
            ORDER BY gl.`create_time` DESC
            LIMIT @page_size OFFSET @offset;
            """, filter.Parameters);
        var totals = await result.ReadSingleAsync<int>();
        var list = (await result.ReadAsync<GoodslocationViewModel>()).AsList();
        return (list, totals);
    }

    /// <summary>
    /// 执行 GetAllAsync 操作。
    /// </summary>
    public async Task<List<GoodslocationViewModel>> GetAllAsync(CurrentUser currentUser)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync();
        var rows = await connection.QueryAsync<GoodslocationViewModel>($"""
            SELECT {Projection} FROM `wms_goodslocation` AS gl;
            """);
        return rows.AsList();
    }

    /// <summary>
    /// 执行 GetAsync 操作。
    /// </summary>
    public async Task<GoodslocationViewModel?> GetAsync(int id)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync();
        return await connection.QuerySingleOrDefaultAsync<GoodslocationViewModel>($"""
            SELECT {Projection} FROM `wms_goodslocation` AS gl WHERE gl.`id` = @id LIMIT 1;
            """, new { id });
    }

    /// <summary>
    /// 执行 AddAsync 操作。
    /// </summary>
    public async Task<(int id, string msg)> AddAsync(GoodslocationViewModel viewModel, CurrentUser currentUser)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        var exists = await connection.ExecuteScalarAsync<bool>("""
            SELECT EXISTS(SELECT 1 FROM `wms_goodslocation`
                WHERE `location_name` = @location_name);
            """, new { viewModel.location_name }, transaction);
        if (exists)
        {
            await transaction.RollbackAsync();
            return (0, string.Format(_stringLocalizer["exists_entity"], _stringLocalizer["location_name"], viewModel.location_name));
        }

        var now = DateTime.Now;
        var id = await connection.ExecuteScalarAsync<int>("""
            INSERT INTO `wms_goodslocation`
                (`warehouse_id`, `warehouse_name`, `warehouse_area_name`, `warehouse_area_property`,
                 `location_name`, `location_length`, `location_width`, `location_heigth`, `location_volume`,
                 `location_load`, `roadway_number`, `shelf_number`, `layer_number`, `tag_number`,
                 `create_time`, `last_update_time`, `is_valid`, `warehouse_area_id`)
            VALUES
                (@warehouse_id, @warehouse_name, @warehouse_area_name, @warehouse_area_property,
                 @location_name, @location_length, @location_width, @location_heigth, @location_volume,
                 @location_load, @roadway_number, @shelf_number, @layer_number, @tag_number,
                 @create_time, @last_update_time, @is_valid, @warehouse_area_id);
            SELECT LAST_INSERT_ID();
            """, new
        {
            viewModel.warehouse_id, viewModel.warehouse_name, viewModel.warehouse_area_name,
            viewModel.warehouse_area_property, viewModel.location_name, viewModel.location_length,
            viewModel.location_width, viewModel.location_heigth, viewModel.location_volume,
            viewModel.location_load, viewModel.roadway_number, viewModel.shelf_number,
            viewModel.layer_number, viewModel.tag_number, create_time = now, last_update_time = now,
            viewModel.is_valid, viewModel.warehouse_area_id
        }, transaction);
        await transaction.CommitAsync();
        return id > 0 ? (id, _stringLocalizer["save_success"]) : (0, _stringLocalizer["save_failed"]);
    }

    /// <summary>
    /// 执行 UpdateAsync 操作。
    /// </summary>
    public async Task<(bool flag, string msg)> UpdateAsync(GoodslocationViewModel viewModel, CurrentUser currentUser)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        var duplicate = await connection.ExecuteScalarAsync<bool>("""
            SELECT EXISTS(SELECT 1 FROM `wms_goodslocation`
                WHERE `id` <> @id AND `warehouse_id` = @warehouse_id
                  AND `location_name` = @location_name);
            """, new { viewModel.id, viewModel.warehouse_id, viewModel.location_name }, transaction);
        if (duplicate)
        {
            await transaction.RollbackAsync();
            return (false, string.Format(_stringLocalizer["exists_entity"], _stringLocalizer["location_name"], viewModel.location_name));
        }

        var exists = await connection.ExecuteScalarAsync<bool>(
            "SELECT EXISTS(SELECT 1 FROM `wms_goodslocation` WHERE `id` = @id);",
            new { viewModel.id }, transaction);
        if (!exists)
        {
            await transaction.RollbackAsync();
            return (false, _stringLocalizer["not_exists_entity"]);
        }

        var qty = await connection.ExecuteAsync("""
            UPDATE `wms_goodslocation`
            SET `warehouse_id`=@warehouse_id, `warehouse_name`=@warehouse_name,
                `warehouse_area_name`=@warehouse_area_name, `warehouse_area_property`=@warehouse_area_property,
                `location_name`=@location_name, `location_length`=@location_length,
                `location_width`=@location_width, `location_heigth`=@location_heigth,
                `location_volume`=@location_volume, `location_load`=@location_load,
                `roadway_number`=@roadway_number, `shelf_number`=@shelf_number,
                `layer_number`=@layer_number, `tag_number`=@tag_number, `is_valid`=@is_valid,
                `warehouse_area_id`=@warehouse_area_id, `last_update_time`=@last_update_time
            WHERE `id`=@id;
            """, new
        {
            viewModel.id, viewModel.warehouse_id, viewModel.warehouse_name, viewModel.warehouse_area_name,
            viewModel.warehouse_area_property, viewModel.location_name, viewModel.location_length,
            viewModel.location_width, viewModel.location_heigth, viewModel.location_volume,
            viewModel.location_load, viewModel.roadway_number, viewModel.shelf_number,
            viewModel.layer_number, viewModel.tag_number, viewModel.is_valid,
            viewModel.warehouse_area_id, last_update_time = DateTime.Now
        }, transaction);
        await transaction.CommitAsync();
        return qty > 0 ? (true, _stringLocalizer["save_success"]) : (false, _stringLocalizer["save_failed"]);
    }

    /// <summary>
    /// 执行 DeleteAsync 操作。
    /// </summary>
    public async Task<(bool flag, string msg)> DeleteAsync(int id)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        var existStock = await connection.ExecuteScalarAsync<bool>("""
            SELECT
              EXISTS(SELECT 1 FROM `wms_stock` WHERE `qty` > 0 AND `goods_location_id` = @id)
              OR EXISTS(
                SELECT 1 FROM `wms_erp_stock_allocation`
                 WHERE `goods_location_id` = @id AND `location_state` = 'ACTIVE'
              );
            """, new { id }, transaction);
        if (existStock)
        {
            await transaction.RollbackAsync();
            return (false, _stringLocalizer["location_exist_stock_not_delete"]);
        }

        var qty = await connection.ExecuteAsync(
            "DELETE FROM `wms_goodslocation` WHERE `id` = @id;", new { id }, transaction);
        await transaction.CommitAsync();
        return qty > 0 ? (true, _stringLocalizer["delete_success"]) : (false, _stringLocalizer["delete_failed"]);
    }
}
