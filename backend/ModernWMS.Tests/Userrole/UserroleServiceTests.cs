using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using ModernWMS.Core.DBContext;
using ModernWMS.Core.JWT;
using ModernWMS.Core.Models;
using ModernWMS.WMS.Entities.Models;
using ModernWMS.WMS.Entities.ViewModels;
using ModernWMS.WMS.Services;

namespace ModernWMS.Tests.Userrole;

public class UserroleServiceTests
{
    private const string AdminRoleReservedMessage = "admin_role_reserved";

    [Fact]
    public async Task AddAsync_rejects_admin_case_variant()
    {
        await using var database = CreateDatabase();
        await SeedUserrolesAsync(database);
        var service = CreateService(database);

        var (id, msg) = await service.AddAsync(new UserroleViewModel
        {
            role_name = " Admin ",
            is_valid = true
        }, TenantOneUser());

        Assert.Equal(0, id);
        Assert.Equal(AdminRoleReservedMessage, msg);
    }

    [Fact]
    public async Task UpdateAsync_rejects_existing_admin_role()
    {
        await using var database = CreateDatabase();
        await SeedUserrolesAsync(database);
        var service = CreateService(database);

        var (flag, msg) = await service.UpdateAsync(new UserroleViewModel
        {
            id = 3,
            role_name = "admin-renamed",
            is_valid = true
        }, TenantOneUser());

        Assert.False(flag);
        Assert.Equal(AdminRoleReservedMessage, msg);
        Assert.Equal("Admin", (await database.Set<UserroleEntity>().AsNoTracking().SingleAsync(t => t.id == 3)).role_name);
    }

    [Fact]
    public async Task UpdateAsync_does_not_modify_cross_tenant_role()
    {
        await using var database = CreateDatabase();
        await SeedUserrolesAsync(database);
        var service = CreateService(database);

        var (flag, _) = await service.UpdateAsync(new UserroleViewModel
        {
            id = 2,
            role_name = "tenant-two-renamed",
            is_valid = false
        }, TenantOneUser());

        Assert.False(flag);
        var role = await database.Set<UserroleEntity>().AsNoTracking().SingleAsync(t => t.id == 2);
        Assert.Equal("tenant-two-role", role.role_name);
        Assert.True(role.is_valid);
    }

    [Fact]
    public async Task DeleteAsync_rejects_existing_admin_role()
    {
        await using var database = CreateDatabase();
        await SeedUserrolesAsync(database);
        var service = CreateService(database);

        var (flag, msg) = await service.DeleteAsync(3, TenantOneUser());

        Assert.False(flag);
        Assert.Equal(AdminRoleReservedMessage, msg);
        Assert.True(await database.Set<UserroleEntity>().AnyAsync(t => t.id == 3));
    }

    [Fact]
    public async Task DeleteAsync_does_not_delete_cross_tenant_role()
    {
        await using var database = CreateDatabase();
        await SeedUserrolesAsync(database);
        var service = CreateService(database);

        var (flag, _) = await service.DeleteAsync(2, TenantOneUser());

        Assert.False(flag);
        Assert.True(await database.Set<UserroleEntity>().AnyAsync(t => t.id == 2 && t.tenant_id == 2));
    }

    [Fact]
    public async Task BulkSaveAsync_rejects_new_admin_case_variant()
    {
        await using var database = CreateDatabase();
        await SeedUserrolesAsync(database);
        var service = CreateService(database);

        var (flag, msg) = await service.BulkSaveAsync([
            new UserroleViewModel { id = 0, role_name = "ADMIN", is_valid = true }
        ], TenantOneUser());

        Assert.False(flag);
        Assert.Equal(AdminRoleReservedMessage, msg);
    }

    [Fact]
    public async Task BulkSaveAsync_ignores_unchanged_admin_and_updates_other_roles()
    {
        await using var database = CreateDatabase();
        await SeedUserrolesAsync(database);
        var service = CreateService(database);

        var (flag, _) = await service.BulkSaveAsync([
            new UserroleViewModel { id = 3, role_name = "Admin", is_valid = true },
            new UserroleViewModel { id = 1, role_name = "tenant-one-renamed", is_valid = false }
        ], TenantOneUser());

        Assert.True(flag);
        var roles = await database.Set<UserroleEntity>().AsNoTracking().ToListAsync();
        Assert.Equal("Admin", roles.Single(t => t.id == 3).role_name);
        Assert.True(roles.Single(t => t.id == 3).is_valid);
        Assert.Equal("tenant-one-renamed", roles.Single(t => t.id == 1).role_name);
        Assert.False(roles.Single(t => t.id == 1).is_valid);
    }

    [Fact]
    public async Task BulkSaveAsync_rejects_changed_admin_role()
    {
        await using var database = CreateDatabase();
        await SeedUserrolesAsync(database);
        var service = CreateService(database);

        var (flag, msg) = await service.BulkSaveAsync([
            new UserroleViewModel { id = 3, role_name = "Admin", is_valid = false }
        ], TenantOneUser());

        Assert.False(flag);
        Assert.Equal(AdminRoleReservedMessage, msg);
    }

    [Fact]
    public async Task BulkSaveAsync_rejects_admin_delete()
    {
        await using var database = CreateDatabase();
        await SeedUserrolesAsync(database);
        var service = CreateService(database);

        var (flag, msg) = await service.BulkSaveAsync([
            new UserroleViewModel { id = -3, role_name = "Admin", is_valid = true }
        ], TenantOneUser());

        Assert.False(flag);
        Assert.Equal(AdminRoleReservedMessage, msg);
        Assert.True(await database.Set<UserroleEntity>().AnyAsync(t => t.id == 3));
    }

    [Fact]
    public async Task BulkSaveAsync_rejects_cross_tenant_update()
    {
        await using var database = CreateDatabase();
        await SeedUserrolesAsync(database);
        var service = CreateService(database);

        var (flag, msg) = await service.BulkSaveAsync([
            new UserroleViewModel { id = 2, role_name = "tenant-two-renamed", is_valid = false }
        ], TenantOneUser());

        Assert.False(flag);
        Assert.Equal("not_exists_entity", msg);
        Assert.Equal("tenant-two-role", (await database.Set<UserroleEntity>().AsNoTracking().SingleAsync(t => t.id == 2)).role_name);
    }

    private static SqlDBContext CreateDatabase()
    {
        var options = new DbContextOptionsBuilder<SqlDBContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new SqlDBContext(options);
    }

    private static UserroleService CreateService(SqlDBContext database)
    {
        return new UserroleService(database, new TestStringLocalizer());
    }

    private static CurrentUser TenantOneUser()
    {
        return new CurrentUser { tenant_id = 1 };
    }

    private static async Task SeedUserrolesAsync(SqlDBContext database)
    {
        await database.Set<UserroleEntity>().AddRangeAsync(
            new UserroleEntity { id = 1, role_name = "tenant-one-role", is_valid = true, tenant_id = 1 },
            new UserroleEntity { id = 2, role_name = "tenant-two-role", is_valid = true, tenant_id = 2 },
            new UserroleEntity { id = 3, role_name = "Admin", is_valid = true, tenant_id = 1 });
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
