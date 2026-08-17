using Dapper;
using Microsoft.Extensions.Localization;
using ModernWMS.Core.Database;
using ModernWMS.Core.DynamicSearch;
using ModernWMS.Core.JWT;
using ModernWMS.Core.Models;
using ModernWMS.WMS.Services;
using MySqlConnector;

namespace ModernWMS.Tests.ActionLog;

public sealed class ActionLogServiceMySqlIntegrationTests
{
    [Database.DevelopmentMySqlFact]
    public async Task Add_and_page_are_tenant_scoped_and_parameterized()
    {
        var sourceConnectionString = Environment.GetEnvironmentVariable("MODERNWMS_TEST_MYSQL")!;
        var source = new MySqlConnectionStringBuilder(sourceConnectionString);
        Assert.Contains(source.Server, ["127.0.0.1", "localhost", "::1"], StringComparer.OrdinalIgnoreCase);
        Assert.Equal("ruoyi-vue-pro", source.Database);

        var databaseName = $"modernwms_action_log_test_{Guid.NewGuid():N}";
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
                CREATE TABLE `wms_action_log` (
                    `id` INT NOT NULL AUTO_INCREMENT PRIMARY KEY,
                    `vue_path` VARCHAR(64) NOT NULL,
                    `user_name` VARCHAR(128) NOT NULL,
                    `action_content` VARCHAR(2000) NOT NULL,
                    `action_time` DATETIME(6) NOT NULL,
                    `tenant_id` BIGINT NOT NULL
                ) ENGINE=InnoDB;
                INSERT INTO `wms_action_log`
                    (`vue_path`,`user_name`,`action_content`,`action_time`,`tenant_id`)
                VALUES ('stock','other','其他租户',UTC_TIMESTAMP(6),2);
                """);

            await using var factory = new MySqlConnectionFactory(isolated.ConnectionString);
            var service = new ActionLogService(factory, new EchoLocalizer());
            var currentUser = new CurrentUser { tenant_id = 1, user_name = "alice" };

            Assert.True(await service.AddLogAsync("stock", "新增库存", currentUser));
            Assert.True(await service.AddLogAsync("dispatch", "生成拣货单", currentUser));

            var (rows, total) = await service.PageAsync(new PageSearch
            {
                pageIndex = 1,
                pageSize = 10,
                searchObjects =
                [
                    new SearchObject { Name = "vue_path", Operator = Operators.Equal, Text = "stock" },
                    new SearchObject { Name = "user_name", Operator = Operators.Contains, Text = "ali" }
                ]
            }, currentUser);

            Assert.Equal(1, total);
            var row = Assert.Single(rows);
            Assert.Equal("alice", row.user_name);
            Assert.Equal("新增库存", row.action_content);
        }
        finally
        {
            await adminConnection.ExecuteAsync($"DROP DATABASE IF EXISTS `{databaseName}`;");
        }
    }

    private sealed class EchoLocalizer : IStringLocalizer<ModernWMS.Core.MultiLanguage>
    {
        public LocalizedString this[string name] => new(name, name);
        public LocalizedString this[string name, params object[] arguments] => new(name, string.Format(name, arguments));
        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => [];
    }
}
