using Dapper;
using ModernWMS.Core.Database;
using ModernWMS.WMS.Services;
using MySqlConnector;

namespace ModernWMS.Tests.OperatorGroup;

public sealed class OperatorGroupServiceMySqlIntegrationTests
{
    [Database.DevelopmentMySqlFact]
    public async Task Operator_groups_are_ordered_and_join_active_leaders()
    {
        var sourceConnectionString = Environment.GetEnvironmentVariable("MODERNWMS_TEST_MYSQL")!;
        var source = new MySqlConnectionStringBuilder(sourceConnectionString);
        Assert.Contains(source.Server, ["127.0.0.1", "localhost", "::1"], StringComparer.OrdinalIgnoreCase);
        Assert.Equal("ruoyi-vue-pro", source.Database);

        var databaseName = $"modernwms_operator_test_{Guid.NewGuid():N}";
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
                CREATE TABLE `system_dept` (
                    `id` BIGINT NOT NULL PRIMARY KEY,
                    `name` VARCHAR(128) NULL,
                    `dept` VARCHAR(64) NULL,
                    `sort` INT NOT NULL,
                    `leader_user_id` BIGINT NULL,
                    `deleted` BIT NOT NULL
                ) ENGINE=InnoDB;
                CREATE TABLE `system_users` (
                    `id` BIGINT NOT NULL PRIMARY KEY,
                    `nickname` VARCHAR(128) NULL,
                    `mobile` VARCHAR(32) NULL,
                    `deleted` BIT NOT NULL
                ) ENGINE=InnoDB;
                INSERT INTO `system_users` (`id`,`nickname`,`mobile`,`deleted`) VALUES
                    (10,'负责人甲','13800000000',0),
                    (20,'已删除负责人','13900000000',1);
                INSERT INTO `system_dept` (`id`,`name`,`dept`,`sort`,`leader_user_id`,`deleted`) VALUES
                    (2,'操作组乙','operator',20,NULL,0),
                    (1,'操作组甲','operator',10,10,0),
                    (3,'其他部门','sales',1,10,0),
                    (4,'已删除操作组','operator',1,10,1),
                    (5,'操作组丙','operator',30,20,0);
                """);

            await using var factory = new MySqlConnectionFactory(isolated.ConnectionString);
            var service = new OperatorGroupService(factory);

            var rows = await service.GetAllAsync();

            Assert.Equal([1, 2, 3], rows.Select(row => row.sequence));
            Assert.Equal(["操作组甲", "操作组乙", "操作组丙"], rows.Select(row => row.group_name));
            Assert.Equal("负责人甲", rows[0].leader_name);
            Assert.Equal("13800000000", rows[0].phone);
            Assert.Equal(string.Empty, rows[1].leader_name);
            Assert.Equal(string.Empty, rows[2].leader_name);
        }
        finally
        {
            await adminConnection.ExecuteAsync($"DROP DATABASE IF EXISTS `{databaseName}`;");
        }
    }
}
