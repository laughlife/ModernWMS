using Dapper;
using Microsoft.Extensions.Localization;
using ModernWMS.Core.Database;
using ModernWMS.Core.DynamicSearch;
using ModernWMS.Core.JWT;
using ModernWMS.Core.Models;
using ModernWMS.WMS.Entities.ViewModels;
using ModernWMS.WMS.Services;
using MySqlConnector;

namespace ModernWMS.Tests.Print;

public sealed class PrintSolutionServiceTests
{
    [Fact]
    public void Constructor_uses_Dapper_connection_factory_and_has_no_EF_DbContext_dependency()
    {
        var parameterTypes = typeof(PrintSolutionService).GetConstructors()
            .SelectMany(constructor => constructor.GetParameters())
            .Select(parameter => parameter.ParameterType)
            .ToArray();

        Assert.Contains(typeof(IMySqlConnectionFactory), parameterTypes);
        Assert.DoesNotContain(parameterTypes,
            type => type.Name.EndsWith("DbContext", StringComparison.Ordinal));
    }

    [Fact]
    public async Task PageAsync_forwards_tenant_search_and_paging_and_preserves_template_content()
    {
        var source = new InMemoryPrintSolutionDataSource();
        source.Rows.AddRange([
            Solution(1, "shipment", "main", "旧模板", "{\"old\":true}", 7),
            Solution(3, "shipment", "main", "目标模板", "{\"text\":\"标签内容\"}", 7),
            Solution(4, "shipment", "main", "其他租户目标模板", "{}", 8)
        ]);
        var service = CreateService(source);
        var search = new PageSearch
        {
            pageIndex = 1,
            pageSize = 1,
            searchObjects =
            [
                new SearchObject
                {
                    Name = "solution_name",
                    Operator = Operators.Contains,
                    Text = "目标"
                }
            ]
        };

        var (rows, total) = await service.PageAsync(search, CurrentTenant(7));

        Assert.Equal(1, total);
        var row = Assert.Single(rows);
        Assert.Equal(3, row.id);
        Assert.Equal("{\"text\":\"标签内容\"}", row.config_json);
        Assert.Equal([(7L, 1, 1)], source.PageRequests);
    }

    [Fact]
    public async Task Tenant_reads_and_path_lookup_do_not_leak_other_tenants()
    {
        var source = new InMemoryPrintSolutionDataSource();
        source.Rows.AddRange([
            Solution(1, "shipment", "main", "租户一", "{}", 1),
            Solution(2, "shipment", "main", "租户二", "{}", 2),
            Solution(3, "stock", "main", "另一页面", "{}", 2)
        ]);
        var service = CreateService(source);

        var all = await service.GetAllAsync(CurrentTenant(2));
        var byPath = await service.GetByPathAsync(
            new PrintSolutionGetByPathInputViewModel { vue_path = "shipment", tab_page = "main" },
            CurrentTenant(2));

        Assert.Equal([2, 3], all.Select(row => row.id));
        Assert.Equal(2, Assert.Single(byPath).id);
    }

    [Fact]
    public async Task GetAsync_preserves_found_and_missing_results()
    {
        var source = new InMemoryPrintSolutionDataSource();
        source.Rows.Add(Solution(5, "shipment", "main", "模板", "{}", 1));
        var service = CreateService(source);

        Assert.Equal(5, (await service.GetAsync(5)).id);
        Assert.Null(await service.GetAsync(404));
    }

    [Fact]
    public async Task AddAsync_uses_current_tenant_and_ignores_input_id()
    {
        var source = new InMemoryPrintSolutionDataSource();
        var service = CreateService(source);

        var result = await service.AddAsync(Input(999, "新模板", "{\"value\":1}"), CurrentTenant(12));

        Assert.True(result.id > 0);
        Assert.Equal("save_success", result.msg);
        var inserted = Assert.Single(source.Rows);
        Assert.Equal(12, inserted.tenant_id);
        Assert.NotEqual(999, inserted.id);
        Assert.NotEqual(default, inserted.last_update_time);
        Assert.Equal("{\"value\":1}", inserted.config_json);
    }

    [Fact]
    public async Task UpdateAsync_preserves_original_tenant_and_reports_not_found_and_success()
    {
        var source = new InMemoryPrintSolutionDataSource();
        source.Rows.Add(Solution(1, "old", "old-tab", "旧模板", "{\"old\":true}", 6));
        var service = CreateService(source);

        var missing = await service.UpdateAsync(Input(404, "不存在", "{}"));
        var updatedInput = Input(1, "新模板", "{\"new\":true}");
        updatedInput.vue_path = "new";
        updatedInput.tab_page = "new-tab";
        updatedInput.report_length = 88.5m;
        updatedInput.report_width = 66.5m;
        updatedInput.report_direction = "A4";
        updatedInput.tenant_id = 999;
        var updated = await service.UpdateAsync(updatedInput);

        Assert.Equal((false, "not_exists_entity"), missing);
        Assert.Equal((true, "save_success"), updated);
        var row = Assert.Single(source.Rows);
        Assert.Equal(6, row.tenant_id);
        Assert.Equal("{\"new\":true}", row.config_json);
        Assert.Equal(88.5m, row.report_length);
        Assert.Equal("A4", row.report_direction);
    }

    [Fact]
    public async Task DeleteAsync_preserves_success_and_failure_messages()
    {
        var source = new InMemoryPrintSolutionDataSource();
        source.Rows.Add(Solution(1, "path", "tab", "模板", "{}", 1));
        var service = CreateService(source);

        Assert.Equal((true, "delete_success"), await service.DeleteAsync(1));
        Assert.Equal((false, "delete_failed"), await service.DeleteAsync(1));
    }

    [Database.DevelopmentMySqlFact]
    public async Task Dapper_print_solution_queries_and_crud_use_real_prefixed_table_and_transactions()
    {
        var sourceConnectionString = Environment.GetEnvironmentVariable("MODERNWMS_TEST_MYSQL")!;
        var source = new MySqlConnectionStringBuilder(sourceConnectionString);
        Assert.Contains(source.Server, ["127.0.0.1", "localhost", "::1"],
            StringComparer.OrdinalIgnoreCase);
        Assert.Equal("ruoyi-vue-pro", source.Database);

        var databaseName = $"modernwms_print_solution_test_{Guid.NewGuid():N}";
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
                CREATE TABLE `wms_user_defined_print_solution` (
                    `id` INT NOT NULL AUTO_INCREMENT PRIMARY KEY,
                    `vue_path` LONGTEXT NOT NULL,
                    `tab_page` LONGTEXT NOT NULL,
                    `solution_name` LONGTEXT NOT NULL,
                    `config_json` LONGTEXT NOT NULL,
                    `report_length` DECIMAL(18,2) NOT NULL,
                    `report_width` DECIMAL(18,2) NOT NULL,
                    `report_direction` LONGTEXT NOT NULL,
                    `last_update_time` DATETIME(6) NOT NULL,
                    `tenant_id` BIGINT NOT NULL
                ) ENGINE=InnoDB;
                INSERT INTO `wms_user_defined_print_solution`
                    (`vue_path`,`tab_page`,`solution_name`,`config_json`,`report_length`,
                     `report_width`,`report_direction`,`last_update_time`,`tenant_id`)
                VALUES
                    ('shipment','main','租户一旧模板','{"tenant":1}',100,80,'A4','2026-08-17 18:00:00',1),
                    ('shipment','main','租户二模板','{"tenant":2}',90,70,'st','2026-08-17 18:00:00',2);
                """);

            await using var factory = new MySqlConnectionFactory(isolated.ConnectionString);
            var service = new PrintSolutionService(factory, new EchoLocalizer());

            var tenantRows = await service.GetAllAsync(CurrentTenant(2));
            Assert.Equal("租户二模板", Assert.Single(tenantRows).solution_name);
            var pathRows = await service.GetByPathAsync(
                new PrintSolutionGetByPathInputViewModel { vue_path = "shipment", tab_page = "main" },
                CurrentTenant(1));
            Assert.Equal("租户一旧模板", Assert.Single(pathRows).solution_name);

            var page = await service.PageAsync(new PageSearch
            {
                pageIndex = 1,
                pageSize = 10,
                searchObjects =
                [
                    new SearchObject
                    {
                        Name = "solution_name",
                        Operator = Operators.Contains,
                        Text = "旧"
                    }
                ]
            }, CurrentTenant(1));
            Assert.Equal(1, page.totals);
            Assert.Equal(1, Assert.Single(page.data).id);

            var endOfDayPage = await service.PageAsync(new PageSearch
            {
                pageIndex = 1,
                pageSize = 10,
                searchObjects =
                [
                    new SearchObject
                    {
                        Name = "last_update_time",
                        Type = "DATETIMEPICKER",
                        Operator = Operators.LessThanOrEqual,
                        Text = "2026-08-17"
                    }
                ]
            }, CurrentTenant(1));
            Assert.Equal(1, endOfDayPage.totals);

            var created = await service.AddAsync(Input(123, "新增模板", "{\"saved\":true}"), CurrentTenant(1));
            Assert.True(created.id > 0);
            var createdRow = await service.GetAsync(created.id);
            Assert.Equal("{\"saved\":true}", createdRow.config_json);
            Assert.Equal(1, createdRow.tenant_id);

            var update = Input(created.id, "已更新模板", "{\"updated\":true}");
            update.tenant_id = 999;
            Assert.Equal((true, "save_success"), await service.UpdateAsync(update));
            var updatedRow = await service.GetAsync(created.id);
            Assert.Equal("已更新模板", updatedRow.solution_name);
            Assert.Equal(1, updatedRow.tenant_id);

            Assert.Equal((true, "delete_success"), await service.DeleteAsync(created.id));
            Assert.Equal((false, "delete_failed"), await service.DeleteAsync(created.id));
            Assert.Equal((false, "not_exists_entity"), await service.UpdateAsync(Input(404, "无", "{}")));
        }
        finally
        {
            await adminConnection.ExecuteAsync($"DROP DATABASE IF EXISTS `{databaseName}`;");
        }
    }

    private static PrintSolutionService CreateService(IPrintSolutionDataSource source) =>
        new(source, new EchoLocalizer());

    private static CurrentUser CurrentTenant(long tenantId) => new() { tenant_id = tenantId };

    private static PrintSolutionData Solution(
        int id, string path, string tab, string name, string config, long tenantId) => new(
        id, path, tab, name, config, 100m, 80m, "st",
        new DateTime(2026, 8, 17, 8, 0, 0), tenantId);

    private static PrintSolutionViewModel Input(int id, string name, string config) => new()
    {
        id = id,
        vue_path = "shipment",
        tab_page = "main",
        solution_name = name,
        config_json = config,
        report_length = 100m,
        report_width = 80m,
        report_direction = "st"
    };

    private sealed class InMemoryPrintSolutionDataSource : IPrintSolutionDataSource
    {
        public List<PrintSolutionData> Rows { get; } = [];
        public List<(long TenantId, int PageIndex, int PageSize)> PageRequests { get; } = [];

        public Task<(List<PrintSolutionData> Rows, int Total)> PageAsync(PageSearch pageSearch, long tenantId)
        {
            PageRequests.Add((tenantId, pageSearch.pageIndex, pageSearch.pageSize));
            IEnumerable<PrintSolutionData> query = Rows.Where(row => row.tenant_id == tenantId);
            foreach (var filter in pageSearch.searchObjects)
            {
                if (filter.Name == "solution_name" && filter.Operator == Operators.Contains
                    && !string.IsNullOrWhiteSpace(filter.Text))
                {
                    query = query.Where(row => row.solution_name.Contains(filter.Text));
                }
            }

            var all = query.OrderByDescending(row => row.id).ToList();
            return Task.FromResult((
                all.Skip((pageSearch.pageIndex - 1) * pageSearch.pageSize).Take(pageSearch.pageSize).ToList(),
                all.Count));
        }

        public Task<List<PrintSolutionData>> GetAllAsync(long tenantId) =>
            Task.FromResult(Rows.Where(row => row.tenant_id == tenantId).ToList());

        public Task<PrintSolutionData?> GetAsync(int id) =>
            Task.FromResult(Rows.SingleOrDefault(row => row.id == id));

        public Task<List<PrintSolutionData>> GetByPathAsync(string vuePath, string tabPage, long tenantId) =>
            Task.FromResult(Rows.Where(row => row.tenant_id == tenantId
                && row.vue_path == vuePath && row.tab_page == tabPage).ToList());

        public Task<int> AddAsync(PrintSolutionData row)
        {
            var id = Rows.Count == 0 ? 1 : Rows.Max(existing => existing.id) + 1;
            Rows.Add(row with { id = id });
            return Task.FromResult(id);
        }

        public Task<PrintSolutionWriteStatus> UpdateAsync(PrintSolutionData row)
        {
            var index = Rows.FindIndex(existing => existing.id == row.id);
            if (index < 0)
            {
                return Task.FromResult(PrintSolutionWriteStatus.NotFound);
            }

            Rows[index] = row with { tenant_id = Rows[index].tenant_id };
            return Task.FromResult(PrintSolutionWriteStatus.Succeeded);
        }

        public Task<bool> DeleteAsync(int id) =>
            Task.FromResult(Rows.RemoveAll(row => row.id == id) > 0);
    }

    private sealed class EchoLocalizer : IStringLocalizer<ModernWMS.Core.MultiLanguage>
    {
        public LocalizedString this[string name] => new(name, name);
        public LocalizedString this[string name, params object[] arguments] =>
            new(name, string.Format(name, arguments));
        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => [];
    }
}
