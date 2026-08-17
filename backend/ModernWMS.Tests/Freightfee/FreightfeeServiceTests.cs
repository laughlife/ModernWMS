using Dapper;
using Microsoft.Extensions.Localization;
using ModernWMS.Core.Database;
using ModernWMS.Core.DynamicSearch;
using ModernWMS.Core.JWT;
using ModernWMS.Core.Models;
using ModernWMS.WMS.Entities.ViewModels;
using ModernWMS.WMS.Services;
using MySqlConnector;

namespace ModernWMS.Tests.Freightfee;

public sealed class FreightfeeServiceTests
{
    [Fact]
    public void Constructor_uses_Dapper_connection_factory_and_has_no_EF_DbContext_dependency()
    {
        var parameterTypes = typeof(FreightfeeService).GetConstructors()
            .SelectMany(constructor => constructor.GetParameters())
            .Select(parameter => parameter.ParameterType)
            .ToArray();

        Assert.Contains(typeof(IMySqlConnectionFactory), parameterTypes);
        Assert.DoesNotContain(parameterTypes,
            type => type.Name.EndsWith("DbContext", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Page_and_all_queries_pass_the_current_tenant_to_the_data_source()
    {
        var source = new InMemoryFreightfeeDataSource();
        source.Rows.AddRange([
            Freightfee(1, "租户一", 1, new DateTime(2026, 8, 17, 8, 0, 0)),
            Freightfee(2, "租户二", 2, new DateTime(2026, 8, 17, 9, 0, 0))
        ]);
        var service = CreateService(source);
        var page = new PageSearch
        {
            pageIndex = 1,
            pageSize = 10,
            searchObjects = [new SearchObject { Name = "carrier", Operator = Operators.Contains, Text = "租户二" }]
        };

        var (rows, total) = await service.PageAsync(page, CurrentTenant(2));
        var all = await service.GetAllAsync(CurrentTenant(2));

        Assert.Equal(1, total);
        Assert.Equal(2, Assert.Single(rows).id);
        Assert.Equal(2, Assert.Single(all).id);
        Assert.Equal([2L, 2L], source.ReadTenantIds);
    }

    [Fact]
    public async Task GetAsync_preserves_null_when_the_record_does_not_exist()
    {
        var service = CreateService(new InMemoryFreightfeeDataSource());

        var row = await service.GetAsync(404);

        Assert.Null(row);
    }

    [Fact]
    public async Task AddAsync_overrides_identity_audit_fields_and_tenant_without_adding_uniqueness_rules()
    {
        var source = new InMemoryFreightfeeDataSource();
        var service = CreateService(source);
        var input = Input(999, "顺丰");
        input.creator = "伪造创建人";
        input.tenant_id = 999;

        var first = await service.AddAsync(input, CurrentTenant(7, "alice"));
        var second = await service.AddAsync(Input(0, "顺丰"), CurrentTenant(7, "alice"));

        Assert.True(first.id > 0);
        Assert.True(second.id > first.id);
        Assert.Equal("save_success", first.msg);
        Assert.Equal(2, source.Rows.Count);
        Assert.All(source.Rows, row =>
        {
            Assert.Equal(7, row.tenant_id);
            Assert.Equal("alice", row.creator);
            Assert.NotEqual(default, row.create_time);
            Assert.Equal(row.create_time, row.last_update_time);
        });
    }

    [Fact]
    public async Task UpdateAsync_preserves_audit_and_tenant_fields_and_reports_existing_outcomes()
    {
        var source = new InMemoryFreightfeeDataSource();
        var created = Freightfee(1, "旧承运商", 5, new DateTime(2026, 8, 16, 8, 0, 0)) with
        {
            creator = "creator"
        };
        source.Rows.Add(created);
        var service = CreateService(source);

        var missing = await service.UpdateAsync(Input(404, "不存在"));
        var updated = await service.UpdateAsync(Input(1, "新承运商"));

        Assert.Equal((false, "not_exists_entity"), missing);
        Assert.Equal((true, "save_success"), updated);
        var row = Assert.Single(source.Rows);
        Assert.Equal("新承运商", row.carrier);
        Assert.Equal(5, row.tenant_id);
        Assert.Equal("creator", row.creator);
        Assert.Equal(created.create_time, row.create_time);
        Assert.True(row.last_update_time > created.last_update_time);
    }

    [Fact]
    public async Task DeleteAsync_and_ExcelAsync_preserve_messages_and_bulk_audit_fields()
    {
        var source = new InMemoryFreightfeeDataSource();
        source.Rows.Add(Freightfee(1, "待删除", 1, DateTime.Today));
        var service = CreateService(source);

        Assert.Equal((true, "delete_success"), await service.DeleteAsync(1));
        Assert.Equal((false, "delete_failed"), await service.DeleteAsync(1));

        var imported = await service.ExcelAsync([
            Excel("圆通", "深圳", "上海"),
            Excel("圆通", "深圳", "上海")
        ], CurrentTenant(8, "importer"));

        Assert.Equal((true, "save_success"), imported);
        Assert.Equal(2, source.Rows.Count);
        Assert.All(source.Rows, row =>
        {
            Assert.Equal(8, row.tenant_id);
            Assert.Equal("importer", row.creator);
            Assert.True(row.is_valid);
            Assert.Equal(row.create_time, row.last_update_time);
        });
    }

    [Database.DevelopmentMySqlFact]
    public async Task Dapper_freightfee_crud_is_parameterized_tenant_scoped_and_uses_wms_table()
    {
        var sourceConnectionString = Environment.GetEnvironmentVariable("MODERNWMS_TEST_MYSQL")!;
        var source = new MySqlConnectionStringBuilder(sourceConnectionString);
        Assert.Contains(source.Server, ["127.0.0.1", "localhost", "::1"], StringComparer.OrdinalIgnoreCase);
        Assert.Equal("ruoyi-vue-pro", source.Database);

        var databaseName = $"modernwms_freightfee_test_{Guid.NewGuid():N}";
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
                CREATE TABLE `wms_freightfee` (
                    `id` INT NOT NULL AUTO_INCREMENT PRIMARY KEY,
                    `carrier` LONGTEXT NOT NULL,
                    `departure_city` LONGTEXT NOT NULL,
                    `arrival_city` LONGTEXT NOT NULL,
                    `price_per_weight` DECIMAL(18,2) NOT NULL,
                    `price_per_volume` DECIMAL(18,2) NOT NULL,
                    `min_payment` DECIMAL(18,2) NOT NULL,
                    `creator` LONGTEXT NOT NULL,
                    `create_time` DATETIME(6) NOT NULL,
                    `last_update_time` DATETIME(6) NOT NULL,
                    `is_valid` TINYINT(1) NOT NULL,
                    `tenant_id` BIGINT NOT NULL,
                    INDEX `ix_freightfee_tenant_create` (`tenant_id`, `create_time`)
                ) ENGINE=InnoDB;
                INSERT INTO `wms_freightfee`
                    (`carrier`,`departure_city`,`arrival_city`,`price_per_weight`,`price_per_volume`,
                     `min_payment`,`creator`,`create_time`,`last_update_time`,`is_valid`,`tenant_id`)
                VALUES
                    ('其他租户','深圳','广州',1,2,3,'other',NOW(6),NOW(6),1,2),
                    ('旧承运商','深圳','上海',4,5,6,'alice',NOW(6),NOW(6),1,1);
                """);

            await using var factory = new MySqlConnectionFactory(isolated.ConnectionString);
            var service = new FreightfeeService(factory, new EchoLocalizer());
            var tenant = CurrentTenant(1, "alice");

            var (filtered, total) = await service.PageAsync(new PageSearch
            {
                pageIndex = 1,
                pageSize = 10,
                searchObjects =
                [
                    new SearchObject
                    {
                        Name = "carrier",
                        Operator = Operators.Contains,
                        Text = "承运商' OR 1=1 --"
                    }
                ]
            }, tenant);
            Assert.Empty(filtered);
            Assert.Equal(0, total);

            var created = await service.AddAsync(Input(0, "新增承运商"), tenant);
            var duplicateAllowed = await service.AddAsync(Input(0, "新增承运商"), tenant);
            Assert.True(created.id > 0);
            Assert.True(duplicateAllowed.id > created.id);

            Assert.Equal((true, "save_success"), await service.UpdateAsync(Input(created.id, "更新承运商")));
            Assert.Equal("更新承运商", (await service.GetAsync(created.id))!.carrier);

            Assert.Equal((true, "save_success"), await service.ExcelAsync([
                Excel("批量一", "东莞", "杭州"),
                Excel("批量二", "东莞", "南京")
            ], tenant));
            Assert.Equal(5, (await service.GetAllAsync(tenant)).Count);

            Assert.Equal((true, "delete_success"), await service.DeleteAsync(created.id));
            Assert.Equal((false, "delete_failed"), await service.DeleteAsync(created.id));
        }
        finally
        {
            await adminConnection.ExecuteAsync($"DROP DATABASE IF EXISTS `{databaseName}`;");
        }
    }

    private static FreightfeeService CreateService(IFreightfeeDataSource source) =>
        new(source, new EchoLocalizer());

    private static CurrentUser CurrentTenant(long tenantId, string userName = "user") =>
        new() { tenant_id = tenantId, user_name = userName };

    private static FreightfeeData Freightfee(int id, string carrier, long tenantId, DateTime created) => new(
        id, carrier, "深圳", "上海", 1.2m, 2.3m, 3.4m, "creator",
        created, created, true, tenantId);

    private static FreightfeeViewModel Input(int id, string carrier) => new()
    {
        id = id,
        carrier = carrier,
        departure_city = "深圳",
        arrival_city = "上海",
        price_per_weight = 1.2m,
        price_per_volume = 2.3m,
        min_payment = 3.4m,
        is_valid = true
    };

    private static FreightfeeExcelmportViewModel Excel(string carrier, string departure, string arrival) => new()
    {
        carrier = carrier,
        departure_city = departure,
        arrival_city = arrival,
        price_per_weight = 1.2m,
        price_per_volume = 2.3m,
        min_payment = 3.4m
    };

    private sealed class InMemoryFreightfeeDataSource : IFreightfeeDataSource
    {
        public List<FreightfeeData> Rows { get; } = [];
        public List<long> ReadTenantIds { get; } = [];

        public Task<(List<FreightfeeData> Rows, int Total)> PageAsync(
            PageSearch pageSearch,
            long tenantId)
        {
            ReadTenantIds.Add(tenantId);
            IEnumerable<FreightfeeData> query = Rows.Where(row => row.tenant_id == tenantId);
            foreach (var filter in pageSearch.searchObjects)
            {
                if (filter.Name == "carrier" && filter.Operator == Operators.Contains)
                {
                    query = query.Where(row => row.carrier.Contains(filter.Text, StringComparison.Ordinal));
                }
            }

            var total = query.Count();
            var page = query.OrderByDescending(row => row.create_time)
                .Skip((pageSearch.pageIndex - 1) * pageSearch.pageSize)
                .Take(pageSearch.pageSize)
                .ToList();
            return Task.FromResult((page, total));
        }

        public Task<List<FreightfeeData>> GetAllAsync(long tenantId)
        {
            ReadTenantIds.Add(tenantId);
            return Task.FromResult(Rows.Where(row => row.tenant_id == tenantId).ToList());
        }

        public Task<FreightfeeData?> GetAsync(int id) =>
            Task.FromResult(Rows.SingleOrDefault(row => row.id == id));

        public Task<FreightfeeAddResult> AddAsync(FreightfeeData freightfee)
        {
            var id = Rows.Count == 0 ? 1 : Rows.Max(row => row.id) + 1;
            Rows.Add(freightfee with { id = id });
            return Task.FromResult(new FreightfeeAddResult(FreightfeeWriteStatus.Succeeded, id));
        }

        public Task<FreightfeeWriteStatus> UpdateAsync(FreightfeeData freightfee)
        {
            var index = Rows.FindIndex(row => row.id == freightfee.id);
            if (index < 0)
            {
                return Task.FromResult(FreightfeeWriteStatus.NotFound);
            }

            var original = Rows[index];
            Rows[index] = freightfee with
            {
                creator = original.creator,
                create_time = original.create_time,
                tenant_id = original.tenant_id
            };
            return Task.FromResult(FreightfeeWriteStatus.Succeeded);
        }

        public Task<bool> DeleteAsync(int id) =>
            Task.FromResult(Rows.RemoveAll(row => row.id == id) > 0);

        public Task<int> AddRangeAsync(IReadOnlyCollection<FreightfeeData> freightfees)
        {
            foreach (var freightfee in freightfees)
            {
                var id = Rows.Count == 0 ? 1 : Rows.Max(row => row.id) + 1;
                Rows.Add(freightfee with { id = id });
            }

            return Task.FromResult(freightfees.Count);
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
