using Dapper;
using Microsoft.Extensions.Localization;
using ModernWMS.Core.Database;
using ModernWMS.Core.JWT;
using ModernWMS.Core.Models;
using ModernWMS.Core.Services;
using ModernWMS.Core.Utility;
using MySqlConnector;

namespace ModernWMS.Tests.Security;

public sealed class AccountServiceMySqlIntegrationTests
{
    [Database.DevelopmentMySqlFact]
    public async Task Login_uses_parameterized_shared_mysql_query_and_tenant_role_boundary()
    {
        var sourceConnectionString = Environment.GetEnvironmentVariable("MODERNWMS_TEST_MYSQL")!;
        var source = new MySqlConnectionStringBuilder(sourceConnectionString);
        Assert.Contains(source.Server, ["127.0.0.1", "localhost", "::1"], StringComparer.OrdinalIgnoreCase);
        Assert.Equal("ruoyi-vue-pro", source.Database);

        var databaseName = $"modernwms_account_test_{Guid.NewGuid():N}";
        var admin = new MySqlConnectionStringBuilder(sourceConnectionString)
        {
            Database = string.Empty,
            SslMode = MySqlSslMode.Disabled
        };
        var isolated = new MySqlConnectionStringBuilder(sourceConnectionString)
        {
            Database = databaseName,
            SslMode = MySqlSslMode.Disabled
        };

        await using var adminConnection = new MySqlConnection(admin.ConnectionString);
        await adminConnection.OpenAsync();
        await adminConnection.ExecuteAsync($"CREATE DATABASE `{databaseName}` CHARACTER SET utf8mb4;");

        try
        {
            await using var setup = new MySqlConnection(isolated.ConnectionString);
            await setup.OpenAsync();
            await setup.ExecuteAsync("""
                CREATE TABLE `wms_userrole` (
                    `id` INT NOT NULL PRIMARY KEY,
                    `role_name` VARCHAR(128) NOT NULL,
                    `tenant_id` BIGINT NOT NULL
                ) ENGINE=InnoDB;
                CREATE TABLE `wms_user` (
                    `id` INT NOT NULL PRIMARY KEY,
                    `user_num` VARCHAR(128) NOT NULL,
                    `user_name` VARCHAR(128) NOT NULL,
                    `user_role` VARCHAR(128) NOT NULL,
                    `auth_string` VARCHAR(128) NOT NULL,
                    `tenant_id` BIGINT NOT NULL
                ) ENGINE=InnoDB;
                """);
            await setup.ExecuteAsync("""
                INSERT INTO `wms_userrole` (`id`,`role_name`,`tenant_id`) VALUES
                    (7,'picker',1),
                    (8,'picker',2);
                INSERT INTO `wms_user` (`id`,`user_num`,`user_name`,`user_role`,`auth_string`,`tenant_id`) VALUES
                    (11,'U001','alice','picker',@password,1),
                    (12,'U002','orphan','missing-role',@password,1);
                """, new { password = Md5Helper.Md5Encrypt32("secret") });

            await using var factory = new MySqlConnectionFactory(isolated.ConnectionString);
            var service = new AccountService(factory, new EchoLocalizer());

            var byName = await service.Login(
                new LoginInputViewModel { user_name = "alice", password = "secret" },
                new CurrentUser());
            var byNumber = await service.Login(
                new LoginInputViewModel { user_name = "U001", password = "secret" },
                new CurrentUser());
            var invalid = await service.Login(
                new LoginInputViewModel { user_name = "alice' OR 1=1 --", password = "secret" },
                new CurrentUser());

            Assert.Equal(11, byName.user_id);
            Assert.Equal(7, byName.userrole_id);
            Assert.Equal(1, byName.tenant_id);
            Assert.Equal(byName.user_id, byNumber.user_id);
            Assert.Null(invalid);
        }
        finally
        {
            await adminConnection.ExecuteAsync($"DROP DATABASE IF EXISTS `{databaseName}`;");
        }
    }

    private sealed class EchoLocalizer : IStringLocalizer<ModernWMS.Core.MultiLanguage>
    {
        public LocalizedString this[string name] => new(name, name);

        public LocalizedString this[string name, params object[] arguments] =>
            new(name, string.Format(name, arguments));

        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => [];
    }
}
