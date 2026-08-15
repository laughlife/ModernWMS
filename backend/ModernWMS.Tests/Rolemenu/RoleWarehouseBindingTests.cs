using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using ModernWMS.Core.DBContext;
using ModernWMS.Core.DBContext.Entities;
using ModernWMS.Core.JWT;
using ModernWMS.Core.Models;
using ModernWMS.WMS.Entities.ViewModels;
using ModernWMS.WMS.Entities.Models;
using ModernWMS.WMS.Services;
using ModernWMS.WMS.Controllers;
using ModernWMS.Core.Utility;
using System.Security.Claims;

namespace ModernWMS.Tests.Rolemenu;

public class RoleWarehouseBindingTests
{
    [Fact]
    public async Task ReplaceWarehousesAsync_validates_all_ids_before_replacing_existing_bindings()
    {
        await using var wms = CreateWmsDatabase();
        await using var erp = CreateErpDatabase();
        await SeedAsync(wms, erp);
        var service = new RolemenuService(wms, new TestStringLocalizer(), erp);

        var (flag, _) = await service.ReplaceWarehousesAsync(new RoleWarehouseBindingViewModel
        {
            userrole_id = 1,
            warehouse_ids = [320118, 999]
        }, CurrentUser());

        Assert.False(flag);
        Assert.Equal([9L], await service.GetWarehouseIdsAsync(1, CurrentUser()));
    }

    [Fact]
    public async Task ReplaceWarehousesAsync_replaces_atomically_and_get_returns_sorted_distinct_ids()
    {
        await using var wms = CreateWmsDatabase();
        await using var erp = CreateErpDatabase();
        await SeedAsync(wms, erp);
        var service = new RolemenuService(wms, new TestStringLocalizer(), erp);

        var (flag, _) = await service.ReplaceWarehousesAsync(new RoleWarehouseBindingViewModel
        {
            userrole_id = 1,
            warehouse_ids = [320118, 10, 9, 320118]
        }, CurrentUser());

        Assert.True(flag);
        Assert.Equal([9L, 10L, 320118L], await service.GetWarehouseIdsAsync(1, CurrentUser()));
    }

    [Fact]
    public async Task ReplaceWarehousesAsync_rejects_role_outside_current_tenant()
    {
        await using var wms = CreateWmsDatabase();
        await using var erp = CreateErpDatabase();
        await SeedAsync(wms, erp);
        var service = new RolemenuService(wms, new TestStringLocalizer(), erp);

        var (flag, message) = await service.ReplaceWarehousesAsync(new RoleWarehouseBindingViewModel
        {
            userrole_id = 2,
            warehouse_ids = [9]
        }, CurrentUser());

        Assert.False(flag);
        Assert.Equal("not_exists_entity", message);
    }

    [Fact]
    public async Task Warehouse_management_rejects_an_ordinary_role_even_for_its_own_binding()
    {
        await using var wms = CreateWmsDatabase();
        await using var erp = CreateErpDatabase();
        await SeedAsync(wms, erp);
        var service = new RolemenuService(wms, new TestStringLocalizer(), erp);
        var ordinaryUser = new CurrentUser { user_id = 9, tenant_id = 1, user_role = "picker" };

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.GetWarehouseIdsAsync(1, ordinaryUser));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.ReplaceWarehousesAsync(new RoleWarehouseBindingViewModel
            {
                userrole_id = 1,
                warehouse_ids = [320118]
            }, ordinaryUser));
    }

    [Fact]
    public async Task Warehouse_management_rejects_a_non_admin_even_with_existing_roleMenu_permission()
    {
        await using var wms = CreateWmsDatabase();
        await using var erp = CreateErpDatabase();
        await SeedAsync(wms, erp);
        await wms.Set<UserroleEntity>().AddAsync(new UserroleEntity
        {
            id = 4,
            role_name = "role-manager",
            is_valid = true,
            tenant_id = 1
        });
        await wms.Set<MenuEntity>().AddAsync(new MenuEntity
        {
            id = 9,
            menu_name = "roleMenu",
            tenant_id = 1
        });
        await wms.Set<RolemenuEntity>().AddAsync(new RolemenuEntity
        {
            id = 9,
            userrole_id = 4,
            menu_id = 9,
            authority = 1,
            tenant_id = 1
        });
        await wms.SaveChangesAsync();
        var service = new RolemenuService(wms, new TestStringLocalizer(), erp);
        var manager = new CurrentUser { user_id = 10, tenant_id = 1, user_role = " role-manager " };

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.GetWarehouseIdsAsync(1, manager));
    }

    [Fact]
    public async Task Warehouse_management_controller_returns_forbid_for_an_ordinary_role()
    {
        await using var wms = CreateWmsDatabase();
        await using var erp = CreateErpDatabase();
        await SeedAsync(wms, erp);
        var service = new RolemenuService(wms, new TestStringLocalizer(), erp);
        var controller = CreateController(service,
            new CurrentUser { user_id = 9, tenant_id = 1, user_role = "picker" });

        var getResult = await controller.GetWarehousesAsync(1);
        var putResult = await controller.ReplaceWarehousesAsync(new RoleWarehouseBindingViewModel
        {
            userrole_id = 1,
            warehouse_ids = [320118]
        });

        Assert.IsType<ForbidResult>(getResult.Result);
        Assert.IsType<ForbidResult>(putResult.Result);
    }

    [Fact]
    public async Task Warehouse_management_controller_allows_a_real_admin_role()
    {
        await using var wms = CreateWmsDatabase();
        await using var erp = CreateErpDatabase();
        await SeedAsync(wms, erp);
        var service = new RolemenuService(wms, new TestStringLocalizer(), erp);
        var controller = CreateController(service, CurrentUser());

        var result = await controller.GetWarehousesAsync(1);

        Assert.Null(result.Result);
        Assert.Equal([9L], result.Value!.Data);
    }

    private static SqlDBContext CreateWmsDatabase() => new(new DbContextOptionsBuilder<SqlDBContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static RuoyiDbContext CreateErpDatabase() => new(new DbContextOptionsBuilder<RuoyiDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static CurrentUser CurrentUser() => new() { user_id = 8, tenant_id = 1, user_role = "admin" };

    private static RolemenuController CreateController(RolemenuService service, CurrentUser currentUser)
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ModernWMS.Core.JWT.ClaimValueTypes.Json, JsonHelper.SerializeObject(currentUser))
        ], "test"));
        return new RolemenuController(service, new TestStringLocalizer())
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = principal }
            }
        };
    }

    private static async Task SeedAsync(SqlDBContext wms, RuoyiDbContext erp)
    {
        await wms.Set<UserroleEntity>().AddRangeAsync(
            new UserroleEntity { id = 1, role_name = "picker", is_valid = true, tenant_id = 1 },
            new UserroleEntity { id = 2, role_name = "other", is_valid = true, tenant_id = 2 },
            new UserroleEntity { id = 3, role_name = "admin", is_valid = true, tenant_id = 1 });
        await wms.Set<RoleWarehouseEntity>().AddAsync(new RoleWarehouseEntity
        {
            id = 1,
            role_id = 1,
            warehouse_id = 9,
            tenant_id = 1
        });
        await wms.SaveChangesAsync();
        await erp.Warehouses.AddRangeAsync(
            new ErpWarehouseEntity { id = 9, name = "备用仓", attr = "国内仓库", deleted = false },
            new ErpWarehouseEntity { id = 10, name = "海外仓", attr = "海外仓库", deleted = false },
            new ErpWarehouseEntity { id = 320118, name = "深圳自建仓", attr = "国内仓库", deleted = false });
        await erp.SaveChangesAsync();
    }

    private sealed class TestStringLocalizer : IStringLocalizer<ModernWMS.Core.MultiLanguage>
    {
        public LocalizedString this[string name] => new(name, name);
        public LocalizedString this[string name, params object[] arguments] => new(name, string.Format(name, arguments));
        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => [];
    }
}
