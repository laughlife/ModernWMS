using Dapper;
using Microsoft.Extensions.Localization;
using ModernWMS.Core.Database;
using ModernWMS.Core.JWT;
using ModernWMS.Core.Models;
using ModernWMS.WMS.Entities.ViewModels;
using ModernWMS.WMS.IServices;

namespace ModernWMS.WMS.Services;

/// <summary>
/// 过渡期只读展示 ERP 深圳仓；仓库主数据由 ERP 统一维护。
/// </summary>
public class WarehouseService : IWarehouseService
{
    private const string WarehouseScope = "w.`id`=320118 AND w.`deleted`=0 AND w.`attr`='国内仓库'";
    private const string SelectViewSql = """
        SELECT w.`id`, COALESCE(w.`name`,'') AS `warehouse_name`,
               COALESCE(w.`city`,'') AS `city`, COALESCE(w.`address_line`,'') AS `address`,
               COALESCE(w.`email`,'') AS `email`, COALESCE(w.`manager`,'') AS `manager`,
               COALESCE(w.`manager_mobile`,'') AS `contact_tel`, w.`creator`, w.`create_time`,
               w.`update_time` AS `last_update_time`, TRUE AS `is_valid`, TRUE AS `is_system`
        FROM `erp_warehouse` w
        """;
    private static readonly IReadOnlyDictionary<string, string> SearchColumns =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["id"]="w.`id`", ["warehouse_name"]="w.`name`", ["city"]="w.`city`",
            ["address"]="w.`address_line`", ["email"]="w.`email`", ["manager"]="w.`manager`",
            ["contact_tel"]="w.`manager_mobile`", ["creator"]="w.`creator`", ["create_time"]="w.`create_time`",
            ["is_valid"]="(w.`deleted`=0)",
        };
    private readonly IMySqlConnectionFactory _connectionFactory;

    /// <summary>初始化只读仓库服务。</summary>
    public WarehouseService(IMySqlConnectionFactory connectionFactory,
        IStringLocalizer<ModernWMS.Core.MultiLanguage> stringLocalizer)
    {
        _connectionFactory = connectionFactory;
    }

    /// <inheritdoc />
    public async Task<List<FormSelectItem>> GetSelectItemsAsnyc(CurrentUser currentUser)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync();
        return (await connection.QueryAsync<FormSelectItem>($"""
            SELECT 'warehouse_name' AS `code`, w.`name` AS `name`, CAST(w.`id` AS CHAR) AS `value`,
                   'warehouse datas' AS `comments`, TRUE AS `is_default`
            FROM `erp_warehouse` w WHERE {WarehouseScope};
            """)).AsList();
    }

    /// <inheritdoc />
    public async Task<List<ErpWarehouseOptionViewModel>> GetErpWarehouseOptionsAsync()
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync();
        return (await connection.QueryAsync<ErpWarehouseOptionViewModel>($"""
            SELECT w.`id`, COALESCE(w.`name`, '') AS `name` FROM `erp_warehouse` w
            WHERE {WarehouseScope};
            """)).AsList();
    }

    /// <inheritdoc />
    public async Task<(List<WarehouseViewModel> data, int totals)> PageAsync(PageSearch pageSearch, CurrentUser currentUser)
    {
        var filter = DapperSearchBuilder.Build(pageSearch.searchObjects, SearchColumns);
        var where = WarehouseScope + (string.IsNullOrWhiteSpace(filter.Sql) ? "" : " AND " + filter.Sql);
        filter.Parameters.Add("offset", (pageSearch.pageIndex - 1) * pageSearch.pageSize);
        filter.Parameters.Add("pageSize", pageSearch.pageSize);
        await using var connection = await _connectionFactory.OpenConnectionAsync();
        using var result = await connection.QueryMultipleAsync($"""
            SELECT COUNT(*) FROM `erp_warehouse` w WHERE {where};
            {SelectViewSql} WHERE {where} ORDER BY w.`id` LIMIT @pageSize OFFSET @offset;
            """, filter.Parameters);
        var totals = await result.ReadSingleAsync<int>();
        return ((await result.ReadAsync<WarehouseViewModel>()).AsList(), totals);
    }

    /// <inheritdoc />
    public async Task<List<WarehouseViewModel>> GetAllAsync(CurrentUser currentUser)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync();
        return (await connection.QueryAsync<WarehouseViewModel>($"{SelectViewSql} WHERE {WarehouseScope};")).AsList();
    }

    /// <inheritdoc />
    public async Task<WarehouseViewModel?> GetAsync(long id, CurrentUser currentUser)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync();
        return await connection.QuerySingleOrDefaultAsync<WarehouseViewModel>(
            $"{SelectViewSql} WHERE {WarehouseScope} AND w.`id`=@id LIMIT 1;", new { id });
    }

}
