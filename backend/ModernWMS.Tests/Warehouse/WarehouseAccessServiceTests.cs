using Microsoft.EntityFrameworkCore;
using ModernWMS.Core.DBContext;
using ModernWMS.Core.DBContext.Entities;
using ModernWMS.Core.JWT;
using ModernWMS.Core.Models;
using ModernWMS.WMS.Services;

namespace ModernWMS.Tests.Warehouse;

public class WarehouseAccessServiceTests
{
    [Fact]
    public async Task GetAllowedAsync_admin_returns_all_valid_warehouses_and_prefers_320118()
    {
        await using var wms = CreateWmsDatabase();
        await using var erp = CreateErpDatabase();
        await SeedWarehousesAsync(erp);
        var service = new WarehouseAccessService(wms, erp);

        var result = await service.GetAllowedAsync(new CurrentUser { user_role = " Admin ", tenant_id = 99 });

        Assert.Equal([9L, 10L, 320118L], result.warehouses.Select(t => t.id).ToArray());
        Assert.Equal(320118L, result.default_warehouse_id);
    }

    [Fact]
    public async Task GetAllowedAsync_unbound_ordinary_role_returns_empty_without_default()
    {
        await using var wms = CreateWmsDatabase();
        await using var erp = CreateErpDatabase();
        await SeedWarehousesAsync(erp);
        await wms.Set<UserroleEntity>().AddAsync(new UserroleEntity
        {
            id = 1,
            role_name = "picker",
            is_valid = true,
            tenant_id = 1
        });
        await wms.SaveChangesAsync();
        var service = new WarehouseAccessService(wms, erp);

        var result = await service.GetAllowedAsync(new CurrentUser { user_role = "picker", tenant_id = 1 });

        Assert.Empty(result.warehouses);
        Assert.Null(result.default_warehouse_id);
    }

    [Fact]
    public async Task GetAllowedAsync_unions_same_normalized_role_bindings_without_using_tenant_as_visibility_filter()
    {
        await using var wms = CreateWmsDatabase();
        await using var erp = CreateErpDatabase();
        await SeedWarehousesAsync(erp);
        await wms.Set<UserroleEntity>().AddRangeAsync(
            new UserroleEntity { id = 1, role_name = "Picker", is_valid = true, tenant_id = 1 },
            new UserroleEntity { id = 2, role_name = " picker ", is_valid = true, tenant_id = 2 });
        await wms.Set<RoleWarehouseEntity>().AddRangeAsync(
            new RoleWarehouseEntity { id = 1, role_id = 1, warehouse_id = 9, tenant_id = 1 },
            new RoleWarehouseEntity { id = 2, role_id = 2, warehouse_id = 320118, tenant_id = 2 });
        await wms.SaveChangesAsync();
        var service = new WarehouseAccessService(wms, erp);

        var result = await service.GetAllowedAsync(new CurrentUser { user_role = " PICKER ", tenant_id = 777 });

        Assert.Equal([9L, 320118L], result.warehouses.Select(t => t.id).ToArray());
        Assert.Equal(9L, result.default_warehouse_id);
    }

    [Fact]
    public async Task EnsureAllowedAsync_rejects_a_direct_request_outside_the_role_binding()
    {
        await using var wms = CreateWmsDatabase();
        await using var erp = CreateErpDatabase();
        await SeedWarehousesAsync(erp);
        await wms.Set<UserroleEntity>().AddAsync(new UserroleEntity
        {
            id = 1,
            role_name = "picker",
            is_valid = true,
            tenant_id = 1
        });
        await wms.Set<RoleWarehouseEntity>().AddAsync(new RoleWarehouseEntity
        {
            id = 1,
            role_id = 1,
            warehouse_id = 9,
            tenant_id = 1
        });
        await wms.SaveChangesAsync();
        var service = new WarehouseAccessService(wms, erp);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.EnsureAllowedAsync(320118, new CurrentUser { user_role = "picker", tenant_id = 1 }));
    }

    private static SqlDBContext CreateWmsDatabase()
    {
        return new SqlDBContext(new DbContextOptionsBuilder<SqlDBContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);
    }

    private static RuoyiDbContext CreateErpDatabase()
    {
        return new RuoyiDbContext(new DbContextOptionsBuilder<RuoyiDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);
    }

    private static async Task SeedWarehousesAsync(RuoyiDbContext erp)
    {
        await erp.Warehouses.AddRangeAsync(
            new ErpWarehouseEntity { id = 320118, name = "深圳自建仓", attr = "国内仓库", deleted = false },
            new ErpWarehouseEntity { id = 9, name = "备用仓", attr = "国内仓库", deleted = false },
            new ErpWarehouseEntity { id = 10, name = "海外仓", attr = "海外仓库", deleted = false },
            new ErpWarehouseEntity { id = 11, name = "已删除仓", attr = "国内仓库", deleted = true });
        await erp.SaveChangesAsync();
    }
}
