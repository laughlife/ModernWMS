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
using MySqlConnector;

namespace ModernWMS.WMS.Services;

/// <summary>
/// 提供仓库库区、操作小组绑定及有效性维护。
/// </summary>
public class WarehouseareaService : BaseService<WarehouseareaEntity>, IWarehouseareaService
{
    private const string Projection = """
        wa.`id`, wa.`warehouse_id`, w.`warehouse_name`, wa.`area_name`, wa.`parent_id`,
        wa.`create_time`, wa.`last_update_time`, wa.`is_valid`,
        wa.`area_property`, wa.`sort`
        """;
    private static readonly IReadOnlyDictionary<string, string> SearchColumns =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["id"]="wa.`id`", ["warehouse_id"]="wa.`warehouse_id`", ["warehouse_name"]="w.`warehouse_name`",
            ["area_name"]="wa.`area_name`", ["parent_id"]="wa.`parent_id`", ["create_time"]="wa.`create_time`",
            ["last_update_time"]="wa.`last_update_time`", ["is_valid"]="wa.`is_valid`",
        };
    private readonly IMySqlConnectionFactory _connectionFactory;
    private readonly IStringLocalizer<ModernWMS.Core.MultiLanguage> _stringLocalizer;

    /// <summary>
    /// 初始化仓库库区服务。
    /// </summary>
    /// <param name="connectionFactory">MySQL 连接工厂。</param>
    /// <param name="stringLocalizer">多语言文本提供器。</param>
    public WarehouseareaService(IMySqlConnectionFactory connectionFactory,
        IStringLocalizer<ModernWMS.Core.MultiLanguage> stringLocalizer)
    {
        _connectionFactory = connectionFactory;
        _stringLocalizer = stringLocalizer;
    }

    /// <inheritdoc />
    public async Task<List<OperatorGroupOptionViewModel>> GetOperatorGroupOptionsAsync()
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync();
        return (await connection.QueryAsync<OperatorGroupOptionViewModel>("""
            SELECT `id`, COALESCE(`name`, '') AS `name`, `sort` FROM `system_dept`
            WHERE `deleted`=0 AND `status`=0 AND `dept`='operator' ORDER BY `sort`, `id`;
            """)).AsList();
    }

    /// <inheritdoc />
    public async Task<List<OperatorGroupMemberOptionViewModel>> GetOperatorGroupMemberOptionsAsync(string? keyword)
    {
        var normalized = (keyword ?? string.Empty).Trim();
        var hasKeyword = normalized.Length > 0;
        await using var connection = await _connectionFactory.OpenConnectionAsync();
        return (await connection.QueryAsync<OperatorGroupMemberOptionViewModel>($"""
            WITH RECURSIVE dept_tree AS (
                SELECT d.`id`, d.`id` AS group_id, COALESCE(d.`name`,'') AS group_name, 0 AS depth
                FROM `system_dept` d
                WHERE d.`deleted`=0 AND d.`status`=0 AND d.`dept`='operator'
                UNION ALL
                SELECT c.`id`, t.group_id, t.group_name, t.depth + 1
                FROM `system_dept` c
                JOIN dept_tree t ON c.`parent_id` = t.`id`
                WHERE c.`deleted`=0 AND c.`status`=0 AND t.depth < 20
            )
            SELECT DISTINCT u.`id` AS user_id, COALESCE(u.`nickname`,'') AS member_name,
                   t.group_id, t.group_name
            FROM `system_users` u
            JOIN dept_tree t ON u.`dept_id` = t.`id`
            WHERE u.`deleted`=0 AND u.`status`=0
              {(hasKeyword ? "AND (t.group_name LIKE @like OR u.`nickname` LIKE @like)" : string.Empty)}
            ORDER BY group_name, member_name
            LIMIT 200;
            """, new { like = $"%{normalized}%" })).AsList();
    }

    /// <inheritdoc />
    public async Task<(List<WarehouseareaViewModel> data, int totals)> PageAsync(PageSearch pageSearch, CurrentUser currentUser)
    {
        var filter = DapperSearchBuilder.Build(pageSearch.searchObjects, SearchColumns);
        var clauses = new List<string>();
        if (pageSearch.sqlTitle == "select") clauses.Add("wa.`is_valid`=1");
        if (!string.IsNullOrWhiteSpace(filter.Sql)) clauses.Add(filter.Sql);
        filter.Parameters.Add("offset", (pageSearch.pageIndex - 1) * pageSearch.pageSize);
        filter.Parameters.Add("page_size", pageSearch.pageSize);
        var where = clauses.Count == 0 ? "1=1" : string.Join(" AND ", clauses);
        await using var connection = await _connectionFactory.OpenConnectionAsync();
        using var result = await connection.QueryMultipleAsync($"""
            SELECT COUNT(*) FROM `wms_warehousearea` wa JOIN `wms_warehouse` w ON w.`id`=wa.`warehouse_id` WHERE {where};
            SELECT {Projection} FROM `wms_warehousearea` wa JOIN `wms_warehouse` w ON w.`id`=wa.`warehouse_id`
            WHERE {where} ORDER BY wa.`sort`, wa.`id` LIMIT @page_size OFFSET @offset;
            """, filter.Parameters);
        var totals = await result.ReadSingleAsync<int>();
        var list = (await result.ReadAsync<WarehouseareaViewModel>()).AsList();
        await PopulateBindingsAsync(connection, list);
        return (list, totals);
    }

    /// <inheritdoc />
    public async Task<List<FormSelectItem>> GetWarehouseareaByWarehouse_id(int warehouse_id, CurrentUser currentUser)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync();
        return (await connection.QueryAsync<FormSelectItem>("""
            SELECT 'warehousearea' `code`, 'warehouseareas of the warehouse' `comments`,
                   `area_name` `name`, CAST(`id` AS CHAR) `value`
            FROM `wms_warehousearea` WHERE `is_valid`=1
              AND `warehouse_id`=@warehouse_id ORDER BY `sort`, `id`;
            """, new { warehouse_id })).AsList();
    }

    /// <inheritdoc />
    public async Task<List<WarehouseareaViewModel>> GetAllAsync(int warehouse_id, CurrentUser currentUser)
    {
        var byWarehouse = warehouse_id > 0 ? "AND wa.`warehouse_id`=@warehouse_id" : string.Empty;
        await using var connection = await _connectionFactory.OpenConnectionAsync();
        var list = (await connection.QueryAsync<WarehouseareaViewModel>($"""
            SELECT {Projection} FROM `wms_warehousearea` wa JOIN `wms_warehouse` w ON w.`id`=wa.`warehouse_id`
            WHERE wa.`is_valid`=1 {byWarehouse} ORDER BY wa.`sort`, wa.`id`;
            """, new { warehouse_id })).AsList();
        await PopulateBindingsAsync(connection, list);
        return list;
    }

    /// <inheritdoc />
    public async Task<WarehouseareaViewModel> GetAsync(int id, CurrentUser currentUser)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync();
        var item = await connection.QuerySingleOrDefaultAsync<WarehouseareaViewModel>($"""
            SELECT {Projection} FROM `wms_warehousearea` wa JOIN `wms_warehouse` w ON w.`id`=wa.`warehouse_id`
            WHERE wa.`id`=@id  LIMIT 1;
            """, new { id });
        if (item == null) return null!;
        await PopulateBindingsAsync(connection, [item]);
        return item;
    }

    /// <inheritdoc />
    public async Task<(int id, string msg)> AddAsync(WarehouseareaViewModel viewModel, CurrentUser currentUser)
    {
        var groupIds = Normalize(viewModel.operator_group_ids);
        await using var connection = await _connectionFactory.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.Serializable);
        try
        {
            if (!await ValidGroupsAsync(connection, transaction, groupIds)) return await Rollback(transaction, (0, _stringLocalizer["invalid_operator_group"].Value));
            if (await BindingConflictAsync(connection, transaction, groupIds, null)) return await Rollback(transaction, (0, _stringLocalizer["operator_group_already_bound"].Value));
            if (!await WarehouseExistsAsync(connection, transaction, viewModel.warehouse_id)) return await Rollback(transaction, (0, _stringLocalizer["not_exists_entity"].Value));
            if (await AreaExistsAsync(connection, transaction, viewModel.warehouse_id, viewModel.area_name, null)) return await Rollback(transaction, (0, Duplicate(viewModel.area_name)));
            var now = DateTime.Now;
            var id = await connection.ExecuteScalarAsync<int>("""
                INSERT INTO `wms_warehousearea` (`warehouse_id`,`area_name`,`parent_id`,`create_time`,`last_update_time`,`is_valid`,`area_property`,`sort`)
                VALUES (@warehouse_id,@area_name,@parent_id,@now,@now,@is_valid,@area_property,@sort); SELECT LAST_INSERT_ID();
                """, new { viewModel.warehouse_id, viewModel.area_name, viewModel.parent_id, now, viewModel.is_valid, viewModel.sort }, transaction);
            await AddBindingsAsync(connection, transaction, id, groupIds, currentUser.user_name, now);
            await transaction.CommitAsync();
            return id > 0 ? (id, _stringLocalizer["save_success"]) : (0, _stringLocalizer["save_failed"]);
        }
        catch { await transaction.RollbackAsync(); throw; }
    }

    /// <inheritdoc />
    public async Task<(bool flag, string msg)> UpdateAsync(WarehouseareaViewModel viewModel, CurrentUser currentUser)
    {
        var groupIds = Normalize(viewModel.operator_group_ids);
        await using var connection = await _connectionFactory.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.Serializable);
        try
        {
            if (!await ValidGroupsAsync(connection, transaction, groupIds)) return await Rollback(transaction, (false, _stringLocalizer["invalid_operator_group"].Value));
            if (await BindingConflictAsync(connection, transaction, groupIds, viewModel.id)) return await Rollback(transaction, (false, _stringLocalizer["operator_group_already_bound"].Value));
            if (!await WarehouseExistsAsync(connection, transaction, viewModel.warehouse_id)) return await Rollback(transaction, (false, _stringLocalizer["not_exists_entity"].Value));
            var exists = await connection.ExecuteScalarAsync<bool>("SELECT EXISTS(SELECT 1 FROM `wms_warehousearea` WHERE `id`=@id FOR UPDATE);", new { viewModel.id }, transaction);
            if (await AreaExistsAsync(connection, transaction, viewModel.warehouse_id, viewModel.area_name, viewModel.id)) return await Rollback(transaction, (false, Duplicate(viewModel.area_name)));
            if (!exists) return await Rollback(transaction, (false, _stringLocalizer["not_exists_entity"].Value));
            var now = DateTime.Now;
            await connection.ExecuteAsync("""
                UPDATE `wms_warehousearea` SET `warehouse_id`=@warehouse_id,`area_name`=@area_name,`parent_id`=@parent_id,
                    `is_valid`=@is_valid,`area_property`=@area_property,`sort`=@sort,`last_update_time`=@now
                WHERE `id`=@id ;
                UPDATE `wms_goodslocation` SET `warehouse_area_name`=@area_name,`warehouse_area_property`=@area_property,`is_valid`=@is_valid
                WHERE `warehouse_area_id`=@id ;
                """, new { viewModel.id, viewModel.warehouse_id, viewModel.area_name, viewModel.parent_id, viewModel.is_valid, viewModel.area_property, viewModel.sort}, transaction);
            var oldIds = (await connection.QueryAsync<long>("SELECT `dept_id` FROM `wms_warehousearea_operator_group` WHERE `warehouse_area_id`=@id ;", new { viewModel.id }, transaction)).AsList();
            if (groupIds.Count == 0)
                await connection.ExecuteAsync("DELETE FROM `wms_warehousearea_operator_group` WHERE `warehouse_area_id`=@id ;", new { viewModel.id }, transaction);
            else
                await connection.ExecuteAsync("DELETE FROM `wms_warehousearea_operator_group` WHERE `warehouse_area_id`=@id  AND `dept_id` NOT IN @groupIds;", new { viewModel.id, groupIds }, transaction);
            await AddBindingsAsync(connection, transaction, viewModel.id, groupIds.Except(oldIds).ToList(), currentUser.user_name, now);
            await transaction.CommitAsync();
            return (true, _stringLocalizer["save_success"]);
        }
        catch { await transaction.RollbackAsync(); throw; }
    }

    /// <inheritdoc />
    public async Task<(bool flag, string msg)> DeleteAsync(int id, CurrentUser currentUser)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted);
        try
        {
            var occupied = await connection.ExecuteScalarAsync<bool>("SELECT EXISTS(SELECT 1 FROM `wms_goodslocation` WHERE `warehouse_area_id`=@id);", new { id }, transaction);
            if (occupied) return await Rollback(transaction, (false, _stringLocalizer["exist_location_not_delete"].Value));
            await connection.ExecuteAsync("DELETE FROM `wms_warehousearea_operator_group` WHERE `warehouse_area_id`=@id ;", new { id }, transaction);
            var affected = await connection.ExecuteAsync("DELETE FROM `wms_warehousearea` WHERE `id`=@id ;", new { id }, transaction);
            await transaction.CommitAsync();
            return affected > 0 ? (true, _stringLocalizer["delete_success"]) : (false, _stringLocalizer["delete_failed"]);
        }
        catch { await transaction.RollbackAsync(); throw; }
    }

    private static async Task<bool> ValidGroupsAsync(MySqlConnection c, IDbTransaction tx, IReadOnlyCollection<long> ids)
    {
        if (ids.Count == 0) return true;
        var count = await c.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM `system_dept` WHERE `id` IN @ids AND `deleted`=0 AND `status`=0 AND `dept`='operator';", new { ids }, tx);
        return count == ids.Count;
    }
    private static async Task<bool> BindingConflictAsync(MySqlConnection c, IDbTransaction tx, IReadOnlyCollection<long> ids, int? areaId)
    {
        if (ids.Count == 0) return false;
        return await c.ExecuteScalarAsync<bool>("SELECT EXISTS(SELECT 1 FROM `wms_warehousearea_operator_group` WHERE `dept_id` IN @ids AND (@areaId IS NULL OR `warehouse_area_id`<>@areaId));", new { ids, areaId }, tx);
    }
    private static Task<bool> WarehouseExistsAsync(MySqlConnection c, IDbTransaction tx, int warehouseId) =>
        c.ExecuteScalarAsync<bool>("SELECT EXISTS(SELECT 1 FROM `wms_warehouse` WHERE `id`=@warehouseId);", new { warehouseId }, tx);
    private static Task<bool> AreaExistsAsync(MySqlConnection c, IDbTransaction tx, int warehouseId, string areaName, int? areaId) =>
        c.ExecuteScalarAsync<bool>("SELECT EXISTS(SELECT 1 FROM `wms_warehousearea` WHERE `warehouse_id`=@warehouseId AND `area_name`=@areaName  AND (@areaId IS NULL OR `id`<>@areaId));", new { warehouseId, areaName, areaId }, tx);
    private static async Task AddBindingsAsync(MySqlConnection c, IDbTransaction tx, int areaId, IReadOnlyCollection<long> ids, string creator, DateTime now)
    {
        if (ids.Count == 0) return;
        await c.ExecuteAsync("INSERT INTO `wms_warehousearea_operator_group` (`warehouse_area_id`,`dept_id`,`creator`,`create_time`) VALUES (@areaId,@deptId,@creator,@now);",
            ids.Select(deptId => new { areaId, deptId, creator, now }), tx);
    }
    private static async Task PopulateBindingsAsync(MySqlConnection c, IEnumerable<WarehouseareaViewModel> areas)
    {
        var list = areas.ToList();
        if (list.Count == 0) return;
        var rows = (await c.QueryAsync<BindingRow>("""
            SELECT b.`warehouse_area_id`,b.`dept_id`,COALESCE(d.`name`,'') `name`
            FROM `wms_warehousearea_operator_group` b JOIN `system_dept` d ON d.`id`=b.`dept_id` AND d.`deleted`=0
            WHERE b.`warehouse_area_id` IN @areaIds ORDER BY d.`sort`,b.`dept_id`;
            """, new { areaIds=list.Select(x=>x.id).Distinct() })).AsList();
        foreach (var area in list)
        {
            var bindings = rows.Where(x => x.warehouse_area_id == area.id).ToList();
            area.operator_group_ids = bindings.Select(x => x.dept_id).ToList();
            area.operator_group_names = bindings.Select(x => x.name).ToList();
        }
    }
    private string Duplicate(string name) => string.Format(_stringLocalizer["exists_entity"], _stringLocalizer["area_name"], name);
    private static List<long> Normalize(IEnumerable<long>? ids) => ids?.Where(x => x > 0).Distinct().ToList() ?? [];
    private static async Task<T> Rollback<T>(MySqlTransaction transaction, T result) { await transaction.RollbackAsync(); return result; }
    private sealed class BindingRow { public int warehouse_area_id { get; set; } public long dept_id { get; set; } public string name { get; set; } = string.Empty; }
}
