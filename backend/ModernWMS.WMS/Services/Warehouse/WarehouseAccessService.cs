using Dapper;
using ModernWMS.Core.Database;
using ModernWMS.Core.JWT;
using ModernWMS.WMS.Entities.ViewModels;
using ModernWMS.WMS.IServices;

namespace ModernWMS.WMS.Services;

/// <summary>
/// Resolves access from explicit role bindings. Tenant identifiers are compatibility metadata,
/// not a warehouse-visibility boundary.
/// </summary>
public class WarehouseAccessService : IWarehouseAccessService
{
    private const long PreferredWarehouseId = 320118;
    private const string AdminRoleName = "admin";

    private readonly IWarehouseAccessDataSource _dataSource;

    public WarehouseAccessService(IMySqlConnectionFactory connectionFactory)
        : this(new DapperWarehouseAccessDataSource(connectionFactory))
    {
    }

    internal WarehouseAccessService(IWarehouseAccessDataSource dataSource)
    {
        _dataSource = dataSource;
    }

    public async Task<WarehouseAccessViewModel> GetAllowedAsync(CurrentUser currentUser)
    {
        // 发货/收货等 ERP 协同流程只作用于国内仓，海外仓不出现在仓库选择中。
        var validWarehouses = await _dataSource.GetDomesticWarehousesAsync();

        if (IsAdmin(currentUser.user_role))
        {
            return new WarehouseAccessViewModel
            {
                warehouses = validWarehouses,
                default_warehouse_id = validWarehouses.Any(t => t.id == PreferredWarehouseId)
                    ? PreferredWarehouseId
                    : validWarehouses.Select(t => (long?)t.id).FirstOrDefault()
            };
        }

        var normalizedRoleName = NormalizeRoleName(currentUser.user_role);
        if (normalizedRoleName.Length == 0)
        {
            return new WarehouseAccessViewModel();
        }

        // Role names are the identity carried by CurrentUser. Normalize in .NET to preserve the
        // previous exact matching rules; tenant_id deliberately does not participate in visibility.
        var roleBindings = await _dataSource.GetValidRoleBindingsAsync();
        var allowedSet = roleBindings
            .Where(t => NormalizeRoleName(t.role_name) == normalizedRoleName && t.warehouse_id.HasValue)
            .Select(t => t.warehouse_id!.Value)
            .ToHashSet();
        var allowedWarehouses = validWarehouses
            .Where(t => allowedSet.Contains(t.id))
            .OrderBy(t => t.id)
            .ToList();

        return new WarehouseAccessViewModel
        {
            warehouses = allowedWarehouses,
            default_warehouse_id = allowedWarehouses.Select(t => (long?)t.id).FirstOrDefault()
        };
    }

    public async Task EnsureAllowedAsync(long warehouseId, CurrentUser currentUser)
    {
        var access = await GetAllowedAsync(currentUser);
        if (!access.warehouses.Any(t => t.id == warehouseId))
        {
            throw new UnauthorizedAccessException($"warehouse access denied: {warehouseId}");
        }
    }

    private static bool IsAdmin(string? roleName) =>
        string.Equals(roleName?.Trim(), AdminRoleName, StringComparison.OrdinalIgnoreCase);

    private static string NormalizeRoleName(string? roleName) =>
        roleName?.Trim().ToUpperInvariant() ?? string.Empty;

    internal interface IWarehouseAccessDataSource
    {
        Task<List<ErpWarehouseOptionViewModel>> GetDomesticWarehousesAsync();
        Task<List<RoleWarehouseBinding>> GetValidRoleBindingsAsync();
    }

    internal sealed class RoleWarehouseBinding
    {
        public string? role_name { get; init; }
        public long? warehouse_id { get; init; }
    }

    private sealed class DapperWarehouseAccessDataSource(IMySqlConnectionFactory connectionFactory)
        : IWarehouseAccessDataSource
    {
        public async Task<List<ErpWarehouseOptionViewModel>> GetDomesticWarehousesAsync()
        {
            await using var connection = await connectionFactory.OpenConnectionAsync();
            return (await connection.QueryAsync<ErpWarehouseOptionViewModel>("""
                SELECT
                    `id`,
                    COALESCE(`name`, '') AS `name`
                FROM `erp_warehouse`
                WHERE `deleted` = 0
                    AND `attr` = @domesticWarehouseAttribute
                ORDER BY `id`;
                """, new { domesticWarehouseAttribute = "国内仓库" })).AsList();
        }

        public async Task<List<RoleWarehouseBinding>> GetValidRoleBindingsAsync()
        {
            await using var connection = await connectionFactory.OpenConnectionAsync();
            return (await connection.QueryAsync<RoleWarehouseBinding>("""
                SELECT
                    role.`role_name`,
                    binding.`warehouse_id`
                FROM `wms_userrole` AS role
                LEFT JOIN `wms_role_warehouse` AS binding ON binding.`role_id` = role.`id`
                WHERE role.`is_valid` = 1;
                """)).AsList();
        }
    }
}
