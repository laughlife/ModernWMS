using Microsoft.Extensions.Localization;
using Dapper;
using ModernWMS.Core.Database;
using ModernWMS.Core.JWT;
using ModernWMS.WMS.Entities.ViewModels;
using ModernWMS.WMS.Services;
using MySqlConnector;

namespace ModernWMS.Tests.Company;

public sealed class CompanyServiceTests
{
    [Fact]
    public void Constructor_uses_Dapper_connection_factory_and_has_no_EF_DbContext_dependency()
    {
        var parameterTypes = typeof(CompanyService).GetConstructors()
            .SelectMany(constructor => constructor.GetParameters())
            .Select(parameter => parameter.ParameterType)
            .ToArray();

        Assert.Contains(typeof(IMySqlConnectionFactory), parameterTypes);
        Assert.DoesNotContain(parameterTypes,
            type => type.Name.EndsWith("DbContext", StringComparison.Ordinal));
    }

    [Fact]
    public async Task GetAllAsync_reads_only_the_current_tenant()
    {
        var source = new InMemoryCompanyDataSource();
        source.Companies.AddRange([
            Company(1, "租户一", 1),
            Company(2, "租户二", 2)
        ]);
        var service = CreateService(source);

        var rows = await service.GetAllAsync(CurrentTenant(2));

        Assert.Equal(2, Assert.Single(rows).id);
        Assert.Equal([2L], source.ReadTenantIds);
    }

    [Fact]
    public async Task GetAsync_returns_an_empty_view_model_when_company_does_not_exist()
    {
        var service = CreateService(new InMemoryCompanyDataSource());

        var row = await service.GetAsync(404);

        Assert.Equal(0, row.id);
        Assert.Equal(string.Empty, row.company_name);
    }

    [Fact]
    public async Task AddAsync_preserves_tenant_and_duplicate_name_messages()
    {
        var source = new InMemoryCompanyDataSource();
        source.Companies.Add(Company(1, "同名公司", 7));
        var service = CreateService(source);

        var duplicate = await service.AddAsync(Input(0, "同名公司"), CurrentTenant(7));
        var created = await service.AddAsync(Input(999, "新公司"), CurrentTenant(7));

        Assert.Equal((0, "exists_entity"), duplicate);
        Assert.True(created.id > 0);
        Assert.Equal("save_success", created.msg);
        var inserted = Assert.Single(source.Companies, company => company.company_name == "新公司");
        Assert.Equal(7, inserted.tenant_id);
        Assert.NotEqual(default, inserted.create_time);
        Assert.Equal(inserted.create_time, inserted.last_update_time);
    }

    [Fact]
    public async Task UpdateAsync_keeps_the_original_tenant_and_reports_all_outcomes()
    {
        var source = new InMemoryCompanyDataSource();
        source.Companies.AddRange([
            Company(1, "甲", 5),
            Company(2, "乙", 5),
            Company(3, "乙", 6)
        ]);
        var service = CreateService(source);

        var missing = await service.UpdateAsync(Input(404, "不存在"));
        var duplicate = await service.UpdateAsync(Input(1, "乙"));
        var updated = await service.UpdateAsync(Input(1, "甲-新"));

        Assert.Equal((false, "not_exists_entity"), missing);
        Assert.Equal((false, "exists_entity"), duplicate);
        Assert.Equal((true, "save_success"), updated);
        var row = Assert.Single(source.Companies, company => company.id == 1);
        Assert.Equal(5, row.tenant_id);
        Assert.Equal("甲-新", row.company_name);
    }

    [Fact]
    public async Task DeleteAsync_preserves_success_and_failure_messages()
    {
        var source = new InMemoryCompanyDataSource();
        source.Companies.Add(Company(1, "待删除", 1));
        var service = CreateService(source);

        var deleted = await service.DeleteAsync(1);
        var missing = await service.DeleteAsync(1);

        Assert.Equal((true, "delete_success"), deleted);
        Assert.Equal((false, "delete_failed"), missing);
    }

    [Database.DevelopmentMySqlFact]
    public async Task Dapper_company_crud_is_tenant_scoped_and_uses_wms_company()
    {
        var sourceConnectionString = Environment.GetEnvironmentVariable("MODERNWMS_TEST_MYSQL")!;
        var source = new MySqlConnectionStringBuilder(sourceConnectionString);
        Assert.Contains(source.Server, ["127.0.0.1", "localhost", "::1"],
            StringComparer.OrdinalIgnoreCase);
        Assert.Equal("ruoyi-vue-pro", source.Database);

        var databaseName = $"modernwms_company_test_{Guid.NewGuid():N}";
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
        await adminConnection.ExecuteAsync(
            $"CREATE DATABASE `{databaseName}` CHARACTER SET utf8mb4;");

        try
        {
            await using var setup = new MySqlConnection(isolated.ConnectionString);
            await setup.OpenAsync();
            await setup.ExecuteAsync("""
                CREATE TABLE `wms_company` (
                    `id` INT NOT NULL AUTO_INCREMENT PRIMARY KEY,
                    `company_name` VARCHAR(256) NOT NULL,
                    `city` VARCHAR(128) NOT NULL,
                    `address` VARCHAR(256) NOT NULL,
                    `manager` VARCHAR(64) NOT NULL,
                    `contact_tel` VARCHAR(64) NOT NULL,
                    `create_time` DATETIME(6) NOT NULL,
                    `last_update_time` DATETIME(6) NOT NULL,
                    `tenant_id` BIGINT NOT NULL,
                    INDEX `ix_company_tenant_name` (`tenant_id`, `company_name`)
                ) ENGINE=InnoDB;
                INSERT INTO `wms_company`
                    (`company_name`,`city`,`address`,`manager`,`contact_tel`,
                     `create_time`,`last_update_time`,`tenant_id`)
                VALUES
                    ('旧公司','深圳','旧地址','','',NOW(6),NOW(6),1),
                    ('同名公司','上海','地址','','',NOW(6),NOW(6),1),
                    ('同名公司','北京','地址','','',NOW(6),NOW(6),2);
                """);

            await using var factory = new MySqlConnectionFactory(isolated.ConnectionString);
            var service = new CompanyService(factory, new EchoLocalizer());

            var tenantRows = await service.GetAllAsync(CurrentTenant(2));
            Assert.Equal("同名公司", Assert.Single(tenantRows).company_name);

            var created = await service.AddAsync(Input(0, "新公司"), CurrentTenant(1));
            Assert.True(created.id > 0);
            Assert.Equal("save_success", created.msg);
            Assert.Equal((0, "exists_entity"),
                await service.AddAsync(Input(0, "同名公司"), CurrentTenant(1)));

            Assert.Equal((false, "exists_entity"),
                await service.UpdateAsync(Input(1, "同名公司")));
            Assert.Equal((true, "save_success"),
                await service.UpdateAsync(Input(1, "已更新公司")));
            var updated = await service.GetAsync(1);
            Assert.Equal("已更新公司", updated.company_name);

            Assert.Equal((true, "delete_success"), await service.DeleteAsync(created.id));
            Assert.Equal((false, "delete_failed"), await service.DeleteAsync(created.id));
        }
        finally
        {
            await adminConnection.ExecuteAsync($"DROP DATABASE IF EXISTS `{databaseName}`;");
        }
    }

    private static CompanyService CreateService(ICompanyDataSource source) =>
        new(source, new EchoLocalizer());

    private static CurrentUser CurrentTenant(long tenantId) => new() { tenant_id = tenantId };

    private static CompanyData Company(int id, string name, long tenantId) => new(
        id, name, "深圳", "地址", "负责人", "13800000000",
        new DateTime(2026, 8, 17, 8, 0, 0),
        new DateTime(2026, 8, 17, 8, 0, 0), tenantId);

    private static CompanyViewModel Input(int id, string name) => new()
    {
        id = id,
        company_name = name,
        city = "深圳",
        address = "地址",
        manager = "负责人",
        contact_tel = "13800000000"
    };

    private sealed class InMemoryCompanyDataSource : ICompanyDataSource
    {
        public List<CompanyData> Companies { get; } = [];
        public List<long> ReadTenantIds { get; } = [];

        public Task<List<CompanyData>> GetAllAsync(long tenantId)
        {
            ReadTenantIds.Add(tenantId);
            return Task.FromResult(Companies.Where(company => company.tenant_id == tenantId)
                .OrderByDescending(company => company.create_time).ToList());
        }

        public Task<CompanyData?> GetAsync(int id) =>
            Task.FromResult(Companies.SingleOrDefault(company => company.id == id));

        public Task<CompanyAddResult> AddAsync(CompanyData company)
        {
            if (Companies.Any(existing => existing.tenant_id == company.tenant_id
                && existing.company_name == company.company_name))
            {
                return Task.FromResult(new CompanyAddResult(CompanyWriteStatus.Duplicate, 0));
            }

            var id = Companies.Count == 0 ? 1 : Companies.Max(existing => existing.id) + 1;
            Companies.Add(company with { id = id });
            return Task.FromResult(new CompanyAddResult(CompanyWriteStatus.Succeeded, id));
        }

        public Task<CompanyWriteStatus> UpdateAsync(CompanyData company)
        {
            var index = Companies.FindIndex(existing => existing.id == company.id);
            if (index < 0)
            {
                return Task.FromResult(CompanyWriteStatus.NotFound);
            }

            var original = Companies[index];
            if (Companies.Any(existing => existing.id != company.id
                && existing.tenant_id == original.tenant_id
                && existing.company_name == company.company_name))
            {
                return Task.FromResult(CompanyWriteStatus.Duplicate);
            }

            Companies[index] = company with { tenant_id = original.tenant_id };
            return Task.FromResult(CompanyWriteStatus.Succeeded);
        }

        public Task<bool> DeleteAsync(int id) =>
            Task.FromResult(Companies.RemoveAll(company => company.id == id) > 0);
    }

    private sealed class EchoLocalizer : IStringLocalizer<ModernWMS.Core.MultiLanguage>
    {
        public LocalizedString this[string name] => new(name, name);
        public LocalizedString this[string name, params object[] arguments] =>
            new(name, string.Format(name, arguments));
        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => [];
    }
}
