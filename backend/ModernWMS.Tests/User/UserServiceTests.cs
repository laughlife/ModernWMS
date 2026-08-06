using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using ModernWMS.Core.DBContext;
using ModernWMS.Core.JWT;
using ModernWMS.Core.Models;
using ModernWMS.WMS.Entities.ViewModels;
using ModernWMS.WMS.Services;

namespace ModernWMS.Tests.User;

public class UserServiceTests
{
    [Fact]
    public async Task UpdateAsync_admin_only_changes_login_name_and_stays_active()
    {
        await using var database = CreateDatabase();
        await database.Set<userEntity>().AddAsync(new userEntity
        {
            id = 1,
            tenant_id = 1,
            user_num = "admin",
            user_name = "Admin Employee",
            user_role = "Admin",
            contact_tel = "10086",
            sex = "male",
            is_valid = false
        });
        await database.SaveChangesAsync();
        var service = CreateService(database);

        var (flag, _) = await service.UpdateAsync(new UserViewModel
        {
            id = 1,
            user_num = "new-admin-login",
            user_name = "Changed Employee",
            user_role = "operator",
            contact_tel = "changed",
            sex = "female",
            is_valid = false
        }, TenantOneUser());

        Assert.True(flag);
        var admin = await database.Set<userEntity>().AsNoTracking().SingleAsync(t => t.id == 1);
        Assert.Equal("new-admin-login", admin.user_num);
        Assert.Equal("Admin Employee", admin.user_name);
        Assert.Equal("Admin", admin.user_role);
        Assert.Equal("10086", admin.contact_tel);
        Assert.Equal("male", admin.sex);
        Assert.True(admin.is_valid);
    }

    [Fact]
    public async Task UpdateAsync_non_admin_updates_all_editable_fields()
    {
        await using var database = CreateDatabase();
        await database.Set<userEntity>().AddAsync(new userEntity
        {
            id = 2,
            tenant_id = 1,
            user_num = "employee-1",
            user_name = "Employee One",
            user_role = "operator",
            contact_tel = "10010",
            sex = "male",
            is_valid = true
        });
        await database.SaveChangesAsync();
        var service = CreateService(database);

        var (flag, _) = await service.UpdateAsync(new UserViewModel
        {
            id = 2,
            user_num = "employee-2",
            user_name = "Employee Two",
            user_role = "manager",
            contact_tel = "10000",
            sex = "female",
            is_valid = false
        }, TenantOneUser());

        Assert.True(flag);
        var user = await database.Set<userEntity>().AsNoTracking().SingleAsync(t => t.id == 2);
        Assert.Equal("employee-2", user.user_num);
        Assert.Equal("Employee Two", user.user_name);
        Assert.Equal("manager", user.user_role);
        Assert.Equal("10000", user.contact_tel);
        Assert.Equal("female", user.sex);
        Assert.False(user.is_valid);
    }

    [Fact]
    public async Task UpdateAsync_does_not_modify_cross_tenant_user()
    {
        await using var database = CreateDatabase();
        await database.Set<userEntity>().AddAsync(new userEntity
        {
            id = 3,
            tenant_id = 2,
            user_num = "tenant-two-user",
            user_name = "Tenant Two",
            user_role = "operator",
            is_valid = true
        });
        await database.SaveChangesAsync();
        var service = CreateService(database);

        var (flag, _) = await service.UpdateAsync(new UserViewModel
        {
            id = 3,
            user_num = "changed",
            user_name = "Changed",
            user_role = "manager",
            is_valid = false
        }, TenantOneUser());

        Assert.False(flag);
        var user = await database.Set<userEntity>().AsNoTracking().SingleAsync(t => t.id == 3);
        Assert.Equal("tenant-two-user", user.user_num);
        Assert.Equal("Tenant Two", user.user_name);
        Assert.Equal("operator", user.user_role);
        Assert.True(user.is_valid);
    }

    private static SqlDBContext CreateDatabase()
    {
        var options = new DbContextOptionsBuilder<SqlDBContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new SqlDBContext(options);
    }

    private static UserService CreateService(SqlDBContext database)
    {
        return new UserService(database, new TestStringLocalizer());
    }

    private static CurrentUser TenantOneUser()
    {
        return new CurrentUser { tenant_id = 1 };
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
