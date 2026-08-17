using Microsoft.EntityFrameworkCore;
using ModernWMS.Core.DBContext;
using ModernWMS.Core.JWT;
using ModernWMS.Core.Models;
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

    private readonly SqlDBContext _dbContext;
    private readonly RuoyiDbContext _ruoyiDbContext;

    public WarehouseAccessService(SqlDBContext dbContext, RuoyiDbContext ruoyiDbContext)
    {
        _dbContext = dbContext;
        _ruoyiDbContext = ruoyiDbContext;
    }

    public async Task<WarehouseAccessViewModel> GetAllowedAsync(CurrentUser currentUser)
    {
        // 发货/收货等 ERP 协同流程只作用于国内仓，海外仓不出现在仓库选择中。
        var validWarehouses = await _ruoyiDbContext.Warehouses
            .AsNoTracking()
            .Where(t => !t.deleted && t.attr == "国内仓库")
            .OrderBy(t => t.id)
            .Select(t => new ErpWarehouseOptionViewModel
            {
                id = t.id,
                name = t.name ?? string.Empty
            })
            .ToListAsync();

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

        // Role names are the identity carried by CurrentUser. Resolve all exact normalized matches;
        // tenant_id deliberately does not participate in warehouse visibility.
        var roles = await _dbContext.GetDbSet<UserroleEntity>()
            .AsNoTracking()
            .Where(t => t.is_valid)
            .Select(t => new { t.id, t.role_name })
            .ToListAsync();
        var roleIds = roles
            .Where(t => NormalizeRoleName(t.role_name) == normalizedRoleName)
            .Select(t => t.id)
            .Distinct()
            .ToList();
        if (roleIds.Count == 0)
        {
            return new WarehouseAccessViewModel();
        }

        var allowedIds = await _dbContext.GetDbSet<RoleWarehouseEntity>()
            .AsNoTracking()
            .Where(t => roleIds.Contains(t.role_id))
            .Select(t => t.warehouse_id)
            .Distinct()
            .ToListAsync();
        var allowedSet = allowedIds.ToHashSet();
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
}
