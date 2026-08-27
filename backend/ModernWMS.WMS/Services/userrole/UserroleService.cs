using System.Data;
using System.Text;
using Dapper;
using Microsoft.Extensions.Localization;
using ModernWMS.Core.Database;
using ModernWMS.Core.JWT;
using ModernWMS.Core.Models;
using ModernWMS.Core.Services;
using ModernWMS.WMS.Entities.ViewModels;
using ModernWMS.WMS.IServices;

namespace ModernWMS.WMS.Services;

/// <summary>
/// 提供用户角色维护及系统保留角色保护。
/// </summary>
public class UserroleService : BaseService<UserroleEntity>, IUserroleService
{
    private const string AdminRoleName = "admin";
    private const string AdminRoleReservedMessageKey = "admin_role_reserved";
    private readonly IMySqlConnectionFactory _connectionFactory;
    private readonly IStringLocalizer<Core.MultiLanguage> _stringLocalizer;

    /// <summary>
    /// 初始化用户角色服务。
    /// </summary>
    /// <param name="connectionFactory">MySQL 连接工厂。</param>
    /// <param name="stringLocalizer">多语言文本提供器。</param>
    public UserroleService(IMySqlConnectionFactory connectionFactory,
        IStringLocalizer<Core.MultiLanguage> stringLocalizer)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        _stringLocalizer = stringLocalizer ?? throw new ArgumentNullException(nameof(stringLocalizer));
    }

    /// <inheritdoc />
    public async Task<(bool flag, string msg)> BulkSaveAsync(List<UserroleViewModel> viewModels, CurrentUser currentUser)
    {
        var messages = new StringBuilder();
        viewModels ??= [];
        viewModels.ForEach(x => x.role_name = NormalizeRoleName(x.role_name));
        var upserts = viewModels.Where(x => x.id >= 0).ToList();
        if (upserts.Any(x => x.id == 0 && IsAdminRole(x.role_name)))
            return (false, _stringLocalizer[AdminRoleReservedMessageKey]);
        var deleteIds = viewModels.Where(x => x.id < 0).Select(x => -x.id).ToList();
        var requestedIds = upserts.Where(x => x.id > 0).Select(x => x.id).Concat(deleteIds).Distinct().ToArray();

        await using var connection = await _connectionFactory.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.Serializable);
        try
        {
            var existing = requestedIds.Length == 0 ? [] : (await connection.QueryAsync<UserroleEntity>("""
                SELECT `id`, `role_name`, `is_valid`, `create_time`, `last_update_time`
                FROM `wms_userrole`
                WHERE `id` IN @requestedIds
                FOR UPDATE;
                """, new { requestedIds }, transaction)).AsList();
            var existingById = existing.ToDictionary(x => x.id);
            if (requestedIds.Any(id => !existingById.ContainsKey(id)))
                return await RollbackResult(false, _stringLocalizer["not_exists_entity"], transaction);

            var workItems = new List<UserroleViewModel>();
            foreach (var item in upserts)
            {
                if (item.id == 0 && IsAdminRole(item.role_name))
                    return await RollbackResult(false, _stringLocalizer[AdminRoleReservedMessageKey], transaction);
                if (item.id > 0 && IsAdminRole(existingById[item.id].role_name))
                {
                    if (!IsSameRole(item, existingById[item.id]))
                        return await RollbackResult(false, _stringLocalizer[AdminRoleReservedMessageKey], transaction);
                    continue;
                }
                if (IsAdminRole(item.role_name))
                    return await RollbackResult(false, _stringLocalizer[AdminRoleReservedMessageKey], transaction);
                workItems.Add(item);
            }
            if (deleteIds.Any(id => IsAdminRole(existingById[id].role_name)))
                return await RollbackResult(false, _stringLocalizer[AdminRoleReservedMessageKey], transaction);

            var inputDuplicates = workItems.GroupBy(x => x.role_name, StringComparer.OrdinalIgnoreCase)
                .Where(x => x.Count() > 1).Select(x => x.Key).ToList();
            foreach (var name in inputDuplicates) messages.AppendLine(DuplicateMessage(name));
            if (inputDuplicates.Count > 0)
                return await RollbackResult(false, messages.ToString(), transaction);

            var roleNames = workItems.Select(x => x.role_name).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            var updateIds = workItems.Where(x => x.id > 0).Select(x => x.id).ToArray();
            var databaseDuplicates = roleNames.Length == 0 ? [] : (await connection.QueryAsync<string>("""
                SELECT `role_name` FROM `wms_userrole`
                WHERE `role_name` IN @roleNames
                  AND (`id` NOT IN @deleteIds OR @hasDeletes = 0)
                  AND (`id` NOT IN @updateIds OR @hasUpdates = 0)
                FOR UPDATE;
                """, new
                {
                    roleNames,
                    deleteIds = deleteIds.Count == 0 ? [-1] : deleteIds,
                    hasDeletes = deleteIds.Count > 0 ? 1 : 0,
                    updateIds = updateIds.Length == 0 ? [-1] : updateIds,
                    hasUpdates = updateIds.Length > 0 ? 1 : 0
                }, transaction)).AsList();
            foreach (var name in databaseDuplicates) messages.AppendLine(DuplicateMessage(name));
            if (databaseDuplicates.Count > 0)
                return await RollbackResult(false, messages.ToString(), transaction);

            var now = DateTime.Now;
            foreach (var item in workItems.Where(x => x.id == 0))
                await connection.ExecuteAsync("""
                    INSERT INTO `wms_userrole` (`role_name`,`is_valid`,`create_time`,`last_update_time`)
                    VALUES (@roleName,@isValid,@now,@now);
                    """, new { roleName = item.role_name, isValid = item.is_valid, now}, transaction);
            foreach (var item in workItems.Where(x => x.id > 0))
                await connection.ExecuteAsync("""
                    UPDATE `wms_userrole` SET `role_name`=@roleName,`is_valid`=@isValid,`last_update_time`=@now
                    WHERE `id`=@id ;
                    """, new { item.id, roleName = item.role_name, isValid = item.is_valid, now}, transaction);
            if (deleteIds.Count > 0)
                await connection.ExecuteAsync("DELETE FROM `wms_userrole` WHERE `id` IN @deleteIds;",
                    new { deleteIds }, transaction);

            await transaction.CommitAsync();
            return (true, _stringLocalizer["save_success"]);
        }
        catch { await transaction.RollbackAsync(); throw; }
    }

    /// <inheritdoc />
    public async Task<List<UserroleViewModel>> GetAllAsync(CurrentUser currentUser)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync();
        return (await connection.QueryAsync<UserroleViewModel>("""
            SELECT `id`,`role_name`,`is_valid`,`create_time`,`last_update_time`
            FROM `wms_userrole`;
            """)).AsList();
    }

    /// <inheritdoc />
    public async Task<UserroleViewModel?> GetAsync(int id)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync();
        return await connection.QuerySingleOrDefaultAsync<UserroleViewModel>("""
            SELECT `id`,`role_name`,`is_valid`,`create_time`,`last_update_time`
            FROM `wms_userrole` WHERE `id`=@id LIMIT 1;
            """, new { id });
    }

    /// <inheritdoc />
    public async Task<(int id, string msg)> AddAsync(UserroleViewModel viewModel, CurrentUser currentUser)
    {
        viewModel.role_name = NormalizeRoleName(viewModel.role_name);
        if (IsAdminRole(viewModel.role_name)) return (0, _stringLocalizer[AdminRoleReservedMessageKey]);
        await using var connection = await _connectionFactory.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.Serializable);
        try
        {
            if (await RoleNameExistsAsync(connection, transaction, viewModel.role_name))
                return await RollbackResult(0, DuplicateMessage(viewModel.role_name), transaction);
            var now = DateTime.Now;
            var id = await connection.ExecuteScalarAsync<int>("""
                INSERT INTO `wms_userrole` (`role_name`,`is_valid`,`create_time`,`last_update_time`)
                VALUES (@roleName,@isValid,@now,@now); SELECT LAST_INSERT_ID();
                """, new { roleName = viewModel.role_name, isValid = viewModel.is_valid, now}, transaction);
            await transaction.CommitAsync();
            return id > 0 ? (id, _stringLocalizer["save_success"]) : (0, _stringLocalizer["save_failed"]);
        }
        catch { await transaction.RollbackAsync(); throw; }
    }

    /// <inheritdoc />
    public async Task<(bool flag, string msg)> UpdateAsync(UserroleViewModel viewModel, CurrentUser currentUser)
    {
        viewModel.role_name = NormalizeRoleName(viewModel.role_name);
        if (IsAdminRole(viewModel.role_name)) return (false, _stringLocalizer[AdminRoleReservedMessageKey]);
        await using var connection = await _connectionFactory.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.Serializable);
        try
        {
            var entity = await connection.QuerySingleOrDefaultAsync<UserroleEntity>("""
                SELECT `id`,`role_name`,`is_valid`,`create_time`,`last_update_time`
                FROM `wms_userrole` WHERE `id`=@id FOR UPDATE;
                """, new { viewModel.id}, transaction);
            if (entity == null) return await RollbackResult(false, _stringLocalizer["not_exists_entity"], transaction);
            if (IsAdminRole(entity.role_name)) return await RollbackResult(false, _stringLocalizer[AdminRoleReservedMessageKey], transaction);
            if (await RoleNameExistsAsync(connection, transaction, viewModel.role_name, viewModel.id))
                return await RollbackResult(false, DuplicateMessage(viewModel.role_name), transaction);

            await connection.ExecuteAsync("""
                UPDATE `wms_user` SET `user_role`=@newName
                WHERE `user_role`=@oldName;
                UPDATE `wms_userrole` SET `role_name`=@newName,`is_valid`=@isValid,`last_update_time`=@now
                WHERE `id`=@id ;
                """, new { id = viewModel.id,
                    newName = viewModel.role_name, isValid = viewModel.is_valid, now = DateTime.Now }, transaction);
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
            var roleName = await connection.QuerySingleOrDefaultAsync<string>("""
                SELECT `role_name` FROM `wms_userrole`
                WHERE `id`=@id FOR UPDATE;
                """, new { id}, transaction);
            if (roleName == null) return await RollbackResult(false, _stringLocalizer["not_exists_entity"], transaction);
            if (IsAdminRole(roleName)) return await RollbackResult(false, _stringLocalizer[AdminRoleReservedMessageKey], transaction);
            var affected = await connection.ExecuteAsync(
                "DELETE FROM `wms_userrole` WHERE `id`=@id ;",
                new { id}, transaction);
            await transaction.CommitAsync();
            return affected > 0 ? (true, _stringLocalizer["delete_success"]) : (false, _stringLocalizer["delete_failed"]);
        }
        catch { await transaction.RollbackAsync(); throw; }
    }

    private static async Task<(T value, string msg)> RollbackResult<T>(T value, string msg, IDbTransaction transaction)
    {
        if (transaction is System.Data.Common.DbTransaction dbTransaction) await dbTransaction.RollbackAsync();
        else transaction.Rollback();
        return (value, msg);
    }

    private static Task<bool> RoleNameExistsAsync(IDbConnection connection, IDbTransaction transaction,
        string roleName, int? excludedId = null) => connection.ExecuteScalarAsync<bool>("""
            SELECT EXISTS(SELECT 1 FROM `wms_userrole`
            WHERE `role_name`=@roleName
              AND (@excludedId IS NULL OR `id`<>@excludedId));
            """, new { roleName, excludedId }, transaction);

    private string DuplicateMessage(string roleName) => string.Format(
        _stringLocalizer["exists_entity"], _stringLocalizer["role_name"], roleName);
    private static bool IsAdminRole(string roleName) => string.Equals(
        NormalizeRoleName(roleName), AdminRoleName, StringComparison.OrdinalIgnoreCase);
    private static string NormalizeRoleName(string roleName) => roleName?.Trim() ?? string.Empty;
    private static bool IsSameRole(UserroleViewModel viewModel, UserroleEntity entity) =>
        string.Equals(NormalizeRoleName(viewModel.role_name), NormalizeRoleName(entity.role_name), StringComparison.Ordinal)
        && viewModel.is_valid == entity.is_valid;
}
