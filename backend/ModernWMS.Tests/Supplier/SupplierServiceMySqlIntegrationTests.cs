using Dapper;
using ModernWMS.Core.Database;
using ModernWMS.Core.DynamicSearch;
using ModernWMS.Core.JWT;
using ModernWMS.Core.Models;
using ModernWMS.WMS.Services;
using MySqlConnector;

namespace ModernWMS.Tests.Supplier;

public sealed class SupplierServiceMySqlIntegrationTests
{
    [Database.DevelopmentMySqlFact]
    public async Task Supplier_queries_use_the_shared_development_mysql_connection()
    {
        var sourceConnectionString = Environment.GetEnvironmentVariable("MODERNWMS_TEST_MYSQL")!;
        var source = new MySqlConnectionStringBuilder(sourceConnectionString);
        Assert.Contains(source.Server, ["127.0.0.1", "localhost", "::1"], StringComparer.OrdinalIgnoreCase);
        Assert.Equal("ruoyi-vue-pro", source.Database);

        var databaseName = $"modernwms_supplier_test_{Guid.NewGuid():N}";
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
                CREATE TABLE `erp_supplier` (
                    `id` BIGINT NOT NULL PRIMARY KEY,
                    `name` VARCHAR(128) NULL,
                    `linkman` VARCHAR(64) NULL,
                    `telephone_num` VARCHAR(32) NULL,
                    `qq` VARCHAR(20) NULL,
                    `email` VARCHAR(254) NULL,
                    `province_name` VARCHAR(80) NULL,
                    `city_name` VARCHAR(50) NULL,
                    `address_line` VARCHAR(255) NULL,
                    `remark` VARCHAR(512) NULL,
                    `deleted` BIT NOT NULL
                ) ENGINE=InnoDB;
                INSERT INTO `erp_supplier`
                    (`id`,`name`,`linkman`,`telephone_num`,`qq`,`email`,`province_name`,`city_name`,`address_line`,`remark`,`deleted`)
                VALUES
                    (1,'Alpha供应商','A','100','1','a@example.com','广东','深圳','A路','有效',0),
                    (2,'Beta供应商',NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,0),
                    (3,'Alpha已删除',NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,1);
                """);

            await using var factory = new MySqlConnectionFactory(isolated.ConnectionString);
            var service = new SupplierService(factory);

            var (page, total) = await service.PageAsync(new PageSearch
            {
                pageIndex = 1,
                pageSize = 10,
                searchObjects =
                [
                    new SearchObject { Name = "supplier_name", Text = "Alpha" }
                ]
            }, new CurrentUser());

            Assert.Equal(1, total);
            Assert.Single(page);
            Assert.Equal("Alpha供应商", page[0].supplier_name);
            Assert.Equal("Alpha供应商", page[0].name);

            var all = await service.GetAllAsync();
            Assert.Equal(["Alpha供应商", "Beta供应商"], all.Select(item => item.name));
            Assert.Equal(string.Empty, all[1].linkman);

            Assert.Equal("Beta供应商", (await service.GetAsync(2))?.name);
            Assert.Null(await service.GetAsync(3));
        }
        finally
        {
            await adminConnection.ExecuteAsync($"DROP DATABASE IF EXISTS `{databaseName}`;");
        }
    }
}
