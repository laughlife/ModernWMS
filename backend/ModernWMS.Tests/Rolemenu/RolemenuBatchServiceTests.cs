using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using ModernWMS.Core.DBContext;
using ModernWMS.Core.JWT;
using ModernWMS.Core.Models;
using ModernWMS.Core.Utility;
using ModernWMS.WMS.Entities.Models;
using ModernWMS.WMS.Entities.ViewModels;
using ModernWMS.WMS.Services;

namespace ModernWMS.Tests.Rolemenu;

public class RolemenuBatchServiceTests
{
    private const string AdminRolePermissionMessage = "admin_role_permission_readonly";

    [Fact]
    public async Task BatchUpdateAsync_replaces_current_role_permission_tree()
    {
        await using var database = CreateDatabase();
        await SeedRoleMenusAsync(database);
        var service = CreateService(database);

        var (flag, _) = await service.BatchUpdateAsync(new RolemenuBatchViewModel
        {
            userrole_id = 1,
            detailList =
            [
                new RolemenuBatchDetailViewModel { menu_id = 1, menu_actions_authority = ["保存"] },
                new RolemenuBatchDetailViewModel { menu_id = 3, menu_actions_authority = ["导出"] }
            ]
        }, TenantOneUser());

        Assert.True(flag);
        var saved = await database.Set<RolemenuEntity>()
            .AsNoTracking()
            .Where(t => t.tenant_id == 1 && t.userrole_id == 1)
            .OrderBy(t => t.menu_id)
            .ToListAsync();
        Assert.Equal([1, 3], saved.Select(t => t.menu_id).ToArray());
        Assert.Equal(["保存"], JsonHelper.DeserializeObject<List<string>>(saved[0].menu_actions_authority));
        Assert.Equal(["导出"], JsonHelper.DeserializeObject<List<string>>(saved[1].menu_actions_authority));
    }

    [Fact]
    public async Task BatchUpdateAsync_returns_success_when_payload_matches_existing_permissions()
    {
        await using var database = CreateDatabase();
        await SeedRoleMenusAsync(database);
        var service = CreateService(database);

        var (flag, _) = await service.BatchUpdateAsync(new RolemenuBatchViewModel
        {
            userrole_id = 1,
            detailList =
            [
                new RolemenuBatchDetailViewModel { menu_id = 1, menu_actions_authority = ["查询"] },
                new RolemenuBatchDetailViewModel { menu_id = 2, menu_actions_authority = [] }
            ]
        }, TenantOneUser());

        Assert.True(flag);
    }

    [Fact]
    public async Task BatchUpdateAsync_empty_detail_list_removes_all_permissions_for_current_role()
    {
        await using var database = CreateDatabase();
        await SeedRoleMenusAsync(database);
        var service = CreateService(database);

        var (flag, _) = await service.BatchUpdateAsync(new RolemenuBatchViewModel
        {
            userrole_id = 1,
            detailList = []
        }, TenantOneUser());

        Assert.True(flag);
        Assert.Empty(await database.Set<RolemenuEntity>()
            .AsNoTracking()
            .Where(t => t.tenant_id == 1 && t.userrole_id == 1)
            .ToListAsync());
    }

    [Fact]
    public async Task BatchUpdateAsync_rejects_missing_detail_list()
    {
        await using var database = CreateDatabase();
        await SeedRoleMenusAsync(database);
        var service = CreateService(database);

        var (flag, msg) = await service.BatchUpdateAsync(new RolemenuBatchViewModel
        {
            userrole_id = 1
        }, TenantOneUser());

        Assert.False(flag);
        Assert.Equal("detailList is required", msg);
    }

    [Fact]
    public async Task BatchUpdateAsync_normalizes_action_authority_before_saving()
    {
        await using var database = CreateDatabase();
        await SeedRoleMenusAsync(database);
        var service = CreateService(database);

        var (flag, _) = await service.BatchUpdateAsync(new RolemenuBatchViewModel
        {
            userrole_id = 1,
            detailList =
            [
                new RolemenuBatchDetailViewModel { menu_id = 1, menu_actions_authority = [" 保存", "查询", "保存", "", "  "] },
                new RolemenuBatchDetailViewModel { menu_id = 2, menu_actions_authority = ["导出", " 保存 "] }
            ]
        }, TenantOneUser());

        Assert.True(flag);
        var saved = await database.Set<RolemenuEntity>()
            .AsNoTracking()
            .Where(t => t.tenant_id == 1 && t.userrole_id == 1)
            .OrderBy(t => t.menu_id)
            .ToListAsync();
        Assert.Equal(["保存", "查询"], JsonHelper.DeserializeObject<List<string>>(saved[0].menu_actions_authority));
        Assert.Equal(["保存", "导出"], JsonHelper.DeserializeObject<List<string>>(saved[1].menu_actions_authority));
    }

    [Fact]
    public async Task BatchUpdateAsync_rejects_action_outside_non_empty_menu_action_white_list()
    {
        await using var database = CreateDatabase();
        await SeedRoleMenusAsync(database);
        var service = CreateService(database);

        var (flag, msg) = await service.BatchUpdateAsync(new RolemenuBatchViewModel
        {
            userrole_id = 1,
            detailList =
            [
                new RolemenuBatchDetailViewModel { menu_id = 1, menu_actions_authority = ["删除"] }
            ]
        }, TenantOneUser());

        Assert.False(flag);
        Assert.Contains("invalid menu_actions_authority", msg);
    }

    [Fact]
    public async Task BatchUpdateAsync_accepts_action_inside_non_empty_menu_action_white_list()
    {
        await using var database = CreateDatabase();
        await SeedRoleMenusAsync(database);
        var service = CreateService(database);

        var (flag, _) = await service.BatchUpdateAsync(new RolemenuBatchViewModel
        {
            userrole_id = 1,
            detailList =
            [
                new RolemenuBatchDetailViewModel { menu_id = 1, menu_actions_authority = [" 保存 ", "查询", "保存"] }
            ]
        }, TenantOneUser());

        Assert.True(flag);
        var saved = await database.Set<RolemenuEntity>()
            .AsNoTracking()
            .SingleAsync(t => t.tenant_id == 1 && t.userrole_id == 1 && t.menu_id == 1);
        Assert.Equal(["保存", "查询"], JsonHelper.DeserializeObject<List<string>>(saved.menu_actions_authority));
    }

    [Fact]
    public async Task BatchUpdateAsync_rejects_too_long_action_authority()
    {
        await using var database = CreateDatabase();
        await SeedRoleMenusAsync(database);
        var service = CreateService(database);

        var (flag, msg) = await service.BatchUpdateAsync(new RolemenuBatchViewModel
        {
            userrole_id = 1,
            detailList =
            [
                new RolemenuBatchDetailViewModel { menu_id = 1, menu_actions_authority = [new string('A', 65)] }
            ]
        }, TenantOneUser());

        Assert.False(flag);
        Assert.Contains("menu_actions_authority length", msg);
    }

    [Fact]
    public async Task BatchUpdateAsync_rejects_duplicate_menu_ids()
    {
        await using var database = CreateDatabase();
        await SeedRoleMenusAsync(database);
        var service = CreateService(database);

        var (flag, msg) = await service.BatchUpdateAsync(new RolemenuBatchViewModel
        {
            userrole_id = 1,
            detailList =
            [
                new RolemenuBatchDetailViewModel { menu_id = 1, menu_actions_authority = [] },
                new RolemenuBatchDetailViewModel { menu_id = 1, menu_actions_authority = ["查询"] }
            ]
        }, TenantOneUser());

        Assert.False(flag);
        Assert.Contains("duplicate menu_id", msg);
    }

    [Fact]
    public async Task BatchUpdateAsync_rejects_menu_outside_current_tenant()
    {
        await using var database = CreateDatabase();
        await SeedRoleMenusAsync(database);
        var service = CreateService(database);

        var (flag, msg) = await service.BatchUpdateAsync(new RolemenuBatchViewModel
        {
            userrole_id = 1,
            detailList =
            [
                new RolemenuBatchDetailViewModel { menu_id = 4, menu_actions_authority = [] }
            ]
        }, TenantOneUser());

        Assert.False(flag);
        Assert.Contains("invalid menu_id", msg);
    }

    [Fact]
    public async Task BatchUpdateAsync_rejects_role_outside_current_tenant()
    {
        await using var database = CreateDatabase();
        await SeedRoleMenusAsync(database);
        var service = CreateService(database);

        var (flag, msg) = await service.BatchUpdateAsync(new RolemenuBatchViewModel
        {
            userrole_id = 2,
            detailList =
            [
                new RolemenuBatchDetailViewModel { menu_id = 1, menu_actions_authority = [] }
            ]
        }, TenantOneUser());

        Assert.False(flag);
        Assert.Equal("not_exists_entity", msg);
    }

    [Fact]
    public async Task BatchUpdateAsync_rejects_admin_role_permission_assignment()
    {
        await using var database = CreateDatabase();
        await SeedRoleMenusAsync(database);
        var service = CreateService(database);

        var (flag, msg) = await service.BatchUpdateAsync(new RolemenuBatchViewModel
        {
            userrole_id = 3,
            detailList =
            [
                new RolemenuBatchDetailViewModel { menu_id = 1, menu_actions_authority = ["查询"] }
            ]
        }, TenantOneUser());

        Assert.False(flag);
        Assert.Equal(AdminRolePermissionMessage, msg);
    }

    [Fact]
    public async Task GetMenusByRoleId_returns_all_current_tenant_menus_and_actions_for_admin()
    {
        await using var database = CreateDatabase();
        await SeedRoleMenusAsync(database);
        var service = CreateService(database);

        var menus = await service.GetMenusByRoleId(3, TenantOneUser());

        Assert.Equal([1, 2, 3], menus.Select(t => t.id).ToArray());
        Assert.Equal(["保存", "查询"], menus.Single(t => t.id == 1).menu_actions);
        Assert.Empty(menus.Single(t => t.id == 2).menu_actions);
        Assert.Equal(["导出"], menus.Single(t => t.id == 3).menu_actions);
    }

    [Fact]
    public async Task GetMenusByRoleId_does_not_return_cross_tenant_menus_for_non_admin()
    {
        await using var database = CreateDatabase();
        await SeedRoleMenusAsync(database);
        var service = CreateService(database);

        var menus = await service.GetMenusByRoleId(1, TenantOneUser());

        Assert.Equal([1, 2], menus.Select(t => t.id).ToArray());
        Assert.DoesNotContain(menus, t => t.id == 4);
    }

    [Fact]
    public async Task AddAsync_rejects_admin_role_permission_assignment()
    {
        await using var database = CreateDatabase();
        await SeedRoleMenusAsync(database);
        var service = CreateService(database);

        var (id, msg) = await service.AddAsync(new RolemenuBothViewModel
        {
            userrole_id = 3,
            detailList =
            [
                new RolemenuViewModel { menu_id = 1, menu_actions_authority = ["查询"] }
            ]
        }, TenantOneUser());

        Assert.Equal(0, id);
        Assert.Equal(AdminRolePermissionMessage, msg);
    }

    [Fact]
    public async Task UpdateAsync_rejects_admin_role_permission_assignment()
    {
        await using var database = CreateDatabase();
        await SeedRoleMenusAsync(database);
        var service = CreateService(database);

        var (flag, msg) = await service.UpdateAsync(new RolemenuBothViewModel
        {
            userrole_id = 3,
            detailList =
            [
                new RolemenuViewModel { id = 0, menu_id = 1, menu_actions_authority = ["查询"] }
            ]
        }, TenantOneUser());

        Assert.False(flag);
        Assert.Equal(AdminRolePermissionMessage, msg);
    }

    [Fact]
    public async Task DeleteAsync_rejects_admin_role_permission_assignment()
    {
        await using var database = CreateDatabase();
        await SeedRoleMenusAsync(database);
        var service = CreateService(database);

        var (flag, msg) = await service.DeleteAsync(3, TenantOneUser());

        Assert.False(flag);
        Assert.Equal(AdminRolePermissionMessage, msg);
    }

    private static SqlDBContext CreateDatabase()
    {
        var options = new DbContextOptionsBuilder<SqlDBContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new SqlDBContext(options);
    }

    private static RolemenuService CreateService(SqlDBContext database)
    {
        return new RolemenuService(database, new TestStringLocalizer());
    }

    private static CurrentUser TenantOneUser()
    {
        return new CurrentUser { tenant_id = 1 };
    }

    private static async Task SeedRoleMenusAsync(SqlDBContext database)
    {
        await database.Set<UserroleEntity>().AddRangeAsync(
            new UserroleEntity { id = 1, role_name = "tenant-one-role", is_valid = true, tenant_id = 1 },
            new UserroleEntity { id = 2, role_name = "tenant-two-role", is_valid = true, tenant_id = 2 },
            new UserroleEntity { id = 3, role_name = "Admin", is_valid = true, tenant_id = 1 });
        await database.Set<MenuEntity>().AddRangeAsync(
            new MenuEntity { id = 1, menu_name = "menu-1", tenant_id = 1, menu_actions = "[\"查询\",\"保存\"]" },
            new MenuEntity { id = 2, menu_name = "menu-2", tenant_id = 1, menu_actions = "[]" },
            new MenuEntity { id = 3, menu_name = "menu-3", tenant_id = 1, menu_actions = "[\"导出\"]" },
            new MenuEntity { id = 4, menu_name = "menu-4", tenant_id = 2, menu_actions = "[]" });
        await database.Set<RolemenuEntity>().AddRangeAsync(
            new RolemenuEntity
            {
                id = 1,
                userrole_id = 1,
                menu_id = 1,
                authority = 1,
                tenant_id = 1,
                menu_actions_authority = "[\"查询\"]"
            },
            new RolemenuEntity
            {
                id = 2,
                userrole_id = 1,
                menu_id = 2,
                authority = 1,
                tenant_id = 1,
                menu_actions_authority = "[]"
            });
        await database.SaveChangesAsync();
    }

    private sealed class TestStringLocalizer : IStringLocalizer<ModernWMS.Core.MultiLanguage>
    {
        public LocalizedString this[string name] => new(name, name);

        public LocalizedString this[string name, params object[] arguments] => new(name, string.Format(name, arguments));

        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures)
        {
            return [];
        }
    }
}
