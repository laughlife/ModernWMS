using System.Data;
using System.Text;
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
/// 提供 WMS 仓库配置、ERP 仓库映射及仓库有效性维护。
/// </summary>
public class WarehouseService : BaseService<WarehouseEntity>, IWarehouseService
{
    private const int CurrentWarehouseId = 1;
    private const long CurrentErpWarehouseId = 320118;
    private const string Projection = """
        w.`id`, w.`warehouse_name`, w.`erp_warehouse_id`, w.`city`, w.`address`,
        w.`email`, w.`manager`, w.`contact_tel`, w.`creator`, w.`create_time`,
        w.`last_update_time`, w.`is_valid`
        """;
    private static readonly IReadOnlyDictionary<string, string> SearchColumns =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["id"]="w.`id`", ["warehouse_name"]="w.`warehouse_name`", ["erp_warehouse_id"]="w.`erp_warehouse_id`",
            ["city"]="w.`city`", ["address"]="w.`address`", ["email"]="w.`email`", ["manager"]="w.`manager`",
            ["contact_tel"]="w.`contact_tel`", ["creator"]="w.`creator`", ["create_time"]="w.`create_time`",
        };

    private readonly IMySqlConnectionFactory _connectionFactory;
    private readonly IStringLocalizer<ModernWMS.Core.MultiLanguage> _stringLocalizer;

    /// <summary>
    /// 初始化仓库服务。
    /// </summary>
    /// <param name="connectionFactory">MySQL 连接工厂。</param>
    /// <param name="stringLocalizer">多语言文本提供器。</param>
    public WarehouseService(IMySqlConnectionFactory connectionFactory,
        IStringLocalizer<ModernWMS.Core.MultiLanguage> stringLocalizer)
    {
        _connectionFactory = connectionFactory;
        _stringLocalizer = stringLocalizer;
    }

    /// <inheritdoc />
    public async Task<List<FormSelectItem>> GetSelectItemsAsnyc(CurrentUser currentUser)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync();
        return (await connection.QueryAsync<FormSelectItem>("""
            SELECT 'warehouse_name' AS `code`, w.`warehouse_name` AS `name`, CAST(w.`id` AS CHAR) AS `value`,
                   'warehouse datas' AS `comments`,
                   (w.`id`=@currentId OR w.`erp_warehouse_id`=@erpId) AS `is_default`
            FROM `wms_warehouse` w WHERE w.`is_valid`=1 ;
            """, new { currentId=CurrentWarehouseId, erpId=CurrentErpWarehouseId })).AsList();
    }

    /// <inheritdoc />
    public async Task<List<ErpWarehouseOptionViewModel>> GetErpWarehouseOptionsAsync()
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync();
        return (await connection.QueryAsync<ErpWarehouseOptionViewModel>("""
            SELECT `id`, COALESCE(`name`, '') AS `name` FROM `erp_warehouse`
            WHERE `deleted`=0 AND `attr`='国内仓库' ORDER BY `name`, `id`;
            """)).AsList();
    }

    /// <inheritdoc />
    public async Task<(List<WarehouseViewModel> data, int totals)> PageAsync(PageSearch pageSearch, CurrentUser currentUser)
    {
        var filter = DapperSearchBuilder.Build(pageSearch.searchObjects, SearchColumns);
        var clauses = new List<string>();
        if (!string.IsNullOrWhiteSpace(filter.Sql)) clauses.Add(filter.Sql);
        if (pageSearch.sqlTitle == "select") clauses.Add("w.`is_valid`=1");
        filter.Parameters.Add("offset", (pageSearch.pageIndex - 1) * pageSearch.pageSize);
        filter.Parameters.Add("pageSize", pageSearch.pageSize);
        var where = clauses.Count == 0 ? "1=1" : string.Join(" AND ", clauses);
        await using var connection = await _connectionFactory.OpenConnectionAsync();
        using var result = await connection.QueryMultipleAsync($"""
            SELECT COUNT(*) FROM `wms_warehouse` w WHERE {where};
            {SelectViewSql} WHERE {where} ORDER BY w.`create_time` DESC LIMIT @pageSize OFFSET @offset;
            """, filter.Parameters);
        var totals = await result.ReadSingleAsync<int>();
        var rows = (await result.ReadAsync<WarehouseViewModel>()).AsList();
        return (rows, totals);
    }

    /// <inheritdoc />
    public async Task<List<WarehouseViewModel>> GetAllAsync(CurrentUser currentUser)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync();
        return (await connection.QueryAsync<WarehouseViewModel>($"""
            {SelectViewSql};
            """)).AsList();
    }

    /// <inheritdoc />
    public async Task<WarehouseViewModel?> GetAsync(int id, CurrentUser currentUser)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync();
        return await connection.QuerySingleOrDefaultAsync<WarehouseViewModel>($"""
            {SelectViewSql} WHERE w.`id`=@id  LIMIT 1;
            """, new { id});
    }

    /// <inheritdoc />
    public async Task<(int id, string msg)> AddAsync(WarehouseViewModel viewModel, CurrentUser currentUser)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.Serializable);
        try
        {
            if (!await IsValidErpWarehouseAsync(connection, transaction, viewModel.erp_warehouse_id))
            { await transaction.RollbackAsync(); return (0, _stringLocalizer["invalid_erp_warehouse"]); }
            if (await connection.ExecuteScalarAsync<bool>("""
                SELECT EXISTS(SELECT 1 FROM `wms_warehouse` WHERE `warehouse_name`=@warehouse_name);
                """, new { viewModel.warehouse_name}, transaction))
            { await transaction.RollbackAsync(); return (0, DuplicateMessage(viewModel.warehouse_name)); }
            var now = DateTime.Now;
            var id = await connection.ExecuteScalarAsync<int>("""
                INSERT INTO `wms_warehouse` (`warehouse_name`,`erp_warehouse_id`,`city`,`address`,`email`,`manager`,
                    `contact_tel`,`creator`,`create_time`,`last_update_time`,`is_valid`)
                VALUES (@warehouse_name,@erp_warehouse_id,@city,@address,@email,@manager,@contact_tel,@creator,
                    @create_time,@last_update_time,@is_valid); SELECT LAST_INSERT_ID();
                """, new { viewModel.warehouse_name, viewModel.erp_warehouse_id, viewModel.city, viewModel.address,
                    viewModel.email, viewModel.manager, viewModel.contact_tel, creator=currentUser.user_name,
                    create_time=now, last_update_time=now, viewModel.is_valid}, transaction);
            await transaction.CommitAsync();
            return id > 0 ? (id, _stringLocalizer["save_success"]) : (0, _stringLocalizer["save_failed"]);
        }
        catch { await transaction.RollbackAsync(); throw; }
    }

    /// <inheritdoc />
    public async Task<(bool flag, string msg)> UpdateAsync(WarehouseViewModel viewModel, CurrentUser currentUser)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.Serializable);
        try
        {
            if (!await IsValidErpWarehouseAsync(connection, transaction, viewModel.erp_warehouse_id))
            { await transaction.RollbackAsync(); return (false, _stringLocalizer["invalid_erp_warehouse"]); }
            if (await connection.ExecuteScalarAsync<bool>("""
                SELECT EXISTS(SELECT 1 FROM `wms_warehouse` WHERE `id`<>@id AND `warehouse_name`=@warehouse_name);
                """, new { viewModel.id, viewModel.warehouse_name}, transaction))
            { await transaction.RollbackAsync(); return (false, DuplicateMessage(viewModel.warehouse_name)); }
            var entity = await connection.QuerySingleOrDefaultAsync<WarehouseEntity>($"""
                SELECT {Projection} FROM `wms_warehouse` w WHERE w.`id`=@id FOR UPDATE;
                """, new { viewModel.id}, transaction);
            if (entity == null) { await transaction.RollbackAsync(); return (false, _stringLocalizer["not_exists_entity"]); }
            if (IsCurrentWarehouse(entity) && !string.Equals(viewModel.warehouse_name, entity.warehouse_name, StringComparison.Ordinal))
            { await transaction.RollbackAsync(); return (false, _stringLocalizer["default_warehouse_name_locked"]); }
            var args = new { viewModel.id, viewModel.warehouse_name, viewModel.erp_warehouse_id, viewModel.city,
                viewModel.address, viewModel.email, viewModel.manager, viewModel.contact_tel, viewModel.is_valid,
                last_update_time=DateTime.Now};
            await connection.ExecuteAsync("""
                UPDATE `wms_warehouse` SET `warehouse_name`=@warehouse_name,`erp_warehouse_id`=@erp_warehouse_id,
                    `city`=@city,`address`=@address,`email`=@email,`manager`=@manager,`contact_tel`=@contact_tel,
                    `is_valid`=@is_valid,`last_update_time`=@last_update_time WHERE `id`=@id ;
                UPDATE `wms_warehousearea` SET `is_valid`=@is_valid WHERE `warehouse_id`=@id ;
                UPDATE `wms_goodslocation` SET `warehouse_name`=@warehouse_name,`is_valid`=@is_valid
                    WHERE `warehouse_id`=@id ;
                """, args, transaction);
            await transaction.CommitAsync();
            return (true, _stringLocalizer["save_success"]);
        }
        catch { await transaction.RollbackAsync(); throw; }
    }

    /// <inheritdoc />
    public async Task<(bool flag, string msg)> DeleteAsync(int id, CurrentUser currentUser)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.Serializable);
        try
        {
            var args = new { id, erpId=CurrentErpWarehouseId };
            if (await connection.ExecuteScalarAsync<bool>("""
                SELECT EXISTS(SELECT 1 FROM `wms_warehouse` WHERE `id`=@id
                    AND (`id`=@currentId OR `erp_warehouse_id`=@erpId));
                """, args, transaction))
            { await transaction.RollbackAsync(); return (false, _stringLocalizer["default_warehouse_not_delete"]); }
            var occupied = await connection.ExecuteScalarAsync<bool>("""
                SELECT EXISTS(SELECT 1 FROM `wms_warehousearea` WHERE `warehouse_id`=@id)
                    OR EXISTS(SELECT 1 FROM `wms_goodslocation` WHERE `warehouse_id`=@id);
                """, args, transaction);
            if (occupied) { await transaction.RollbackAsync(); return (false, _stringLocalizer["exist_warehousearea_not_delete"]); }
            var qty = await connection.ExecuteAsync("DELETE FROM `wms_warehouse` WHERE `id`=@id ;", args, transaction);
            await transaction.CommitAsync();
            return qty > 0 ? (true, _stringLocalizer["delete_success"]) : (false, _stringLocalizer["delete_failed"]);
        }
        catch { await transaction.RollbackAsync(); throw; }
    }

    /// <inheritdoc />
    public async Task<(bool flag, string msg)> ExcelAsync(List<WarehouseExcelImportViewModel> datas, CurrentUser currentUser)
    {
        var sb = new StringBuilder();
        var repeats = datas.GroupBy(t => t.warehouse_name).Where(t => t.Count() > 1).Select(t => t.Key).ToList();
        repeats.ForEach(name => sb.AppendLine(DuplicateMessage(name)));
        if (repeats.Count > 0) return (false, sb.ToString());
        if (datas.Count == 0) return (false, _stringLocalizer["save_failed"]);
        await using var connection = await _connectionFactory.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.Serializable);
        try
        {
            var names = datas.Select(t => t.warehouse_name).ToList();
            var existing = (await connection.QueryAsync<string>("""
                SELECT `warehouse_name` FROM `wms_warehouse` WHERE `warehouse_name` IN @names;
                """, new { names }, transaction)).AsList();
            existing.ForEach(name => sb.AppendLine(DuplicateMessage(name)));
            if (existing.Count > 0) { await transaction.RollbackAsync(); return (false, sb.ToString()); }
            var now = DateTime.Now;
            var rows = datas.Select(t => new { t.warehouse_name, t.city, t.address, t.email, t.manager, t.contact_tel,
                creator=currentUser.user_name, create_time=now, last_update_time=now, is_valid=true}).ToList();
            var qty = await connection.ExecuteAsync("""
                INSERT INTO `wms_warehouse` (`warehouse_name`,`city`,`address`,`email`,`manager`,`contact_tel`,
                    `creator`,`create_time`,`last_update_time`,`is_valid`)
                VALUES (@warehouse_name,@city,@address,@email,@manager,@contact_tel,@creator,@create_time,@last_update_time,@is_valid);
                """, rows, transaction);
            await transaction.CommitAsync();
            return qty > 0 ? (true, _stringLocalizer["save_success"]) : (false, _stringLocalizer["save_failed"]);
        }
        catch { await transaction.RollbackAsync(); throw; }
    }

    private const string SelectViewSql = """
        SELECT w.`id`, w.`warehouse_name`, w.`erp_warehouse_id`, w.`city`, w.`address`, w.`email`,
               w.`manager`, w.`contact_tel`, w.`creator`, w.`create_time`, w.`last_update_time`, w.`is_valid`,
               COALESCE(erp.`name`, '') AS `erp_warehouse_name`,
               (w.`id`=1 OR w.`erp_warehouse_id`=320118) AS `is_system`
        FROM `wms_warehouse` w LEFT JOIN `erp_warehouse` erp ON erp.`id`=w.`erp_warehouse_id` AND erp.`deleted`=0
        """;

    private static async Task<bool> IsValidErpWarehouseAsync(System.Data.Common.DbConnection connection,
        System.Data.Common.DbTransaction transaction, long? erpWarehouseId)
    {
        if (!erpWarehouseId.HasValue) return true;
        return await connection.ExecuteScalarAsync<bool>("""
            SELECT EXISTS(SELECT 1 FROM `erp_warehouse` WHERE `id`=@erpWarehouseId AND `deleted`=0 AND `attr`='国内仓库');
            """, new { erpWarehouseId }, transaction);
    }

    private string DuplicateMessage(string name) => string.Format(
        _stringLocalizer["exists_entity"], _stringLocalizer["warehouse_name"], name);
    private static bool IsCurrentWarehouse(WarehouseEntity warehouse) =>
        warehouse.id == CurrentWarehouseId || warehouse.erp_warehouse_id == CurrentErpWarehouseId;
}
