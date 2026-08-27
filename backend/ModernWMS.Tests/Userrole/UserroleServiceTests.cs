using Microsoft.Extensions.Localization;
using ModernWMS.Core.Database;
using ModernWMS.Core.JWT;
using ModernWMS.WMS.Entities.ViewModels;
using ModernWMS.WMS.Services;
using MySqlConnector;

namespace ModernWMS.Tests.Userrole;

public class UserroleServiceTests
{
    [Fact]
    public async Task AddAsync_rejects_admin_case_variant_before_database_access()
    {
        var service = CreateService();

        var (id, message) = await service.AddAsync(new UserroleViewModel
        {
            role_name = " Admin ",
            is_valid = true
        }, new CurrentUser());

        Assert.Equal(0, id);
        Assert.Equal("admin_role_reserved", message);
    }

    [Fact]
    public async Task BulkSaveAsync_rejects_new_admin_case_variant_before_database_access()
    {
        var service = CreateService();

        var (succeeded, message) = await service.BulkSaveAsync([
            new UserroleViewModel { id = 0, role_name = "ADMIN", is_valid = true }
        ], new CurrentUser());

        Assert.False(succeeded);
        Assert.Equal("admin_role_reserved", message);
    }

    private static UserroleService CreateService() =>
        new(new ForbiddenConnectionFactory(), new TestStringLocalizer());

    private sealed class ForbiddenConnectionFactory : IMySqlConnectionFactory
    {
        public MySqlConnection CreateConnection() =>
            throw new InvalidOperationException("This contract test must not access a database.");

        public ValueTask<MySqlConnection> OpenConnectionAsync(CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("This contract test must not access a database.");
    }

    private sealed class TestStringLocalizer : IStringLocalizer<ModernWMS.Core.MultiLanguage>
    {
        public LocalizedString this[string name] => new(name, name);
        public LocalizedString this[string name, params object[] arguments] =>
            new(name, string.Format(name, arguments));
        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => [];
    }
}
