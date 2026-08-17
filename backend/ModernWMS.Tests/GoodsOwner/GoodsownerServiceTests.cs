using Dapper;
using Microsoft.Extensions.Localization;
using ModernWMS.Core.Database;
using ModernWMS.Core.DynamicSearch;
using ModernWMS.Core.JWT;
using ModernWMS.Core.Models;
using ModernWMS.WMS.Entities.ViewModels;
using ModernWMS.WMS.Services;
using MySqlConnector;

namespace ModernWMS.Tests.GoodsOwner;

public sealed class GoodsownerServiceTests
{
    [Fact]
    public void Constructor_uses_Dapper_connection_factory_and_has_no_EF_DbContext_dependency()
    {
        var parameterTypes = typeof(GoodsownerService).GetConstructors()
            .SelectMany(constructor => constructor.GetParameters())
            .Select(parameter => parameter.ParameterType)
            .ToArray();

        Assert.Contains(typeof(IMySqlConnectionFactory), parameterTypes);
        Assert.DoesNotContain(parameterTypes,
            type => type.Name.EndsWith("DbContext", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Page_and_all_queries_are_tenant_scoped_filtered_and_ordered()
    {
        var source = new InMemoryGoodsownerDataSource();
        source.Rows.AddRange([
            Owner(1, "深圳甲", 1, new DateTime(2026, 8, 17, 8, 0, 0)),
            Owner(2, "上海乙", 2, new DateTime(2026, 8, 17, 9, 0, 0)),
            Owner(3, "上海丙", 2, new DateTime(2026, 8, 17, 10, 0, 0))
        ]);
        var service = CreateService(source);
        var page = new PageSearch
        {
            pageIndex = 1,
            pageSize = 1,
            searchObjects =
            [
                new SearchObject
                {
                    Name = "goods_owner_name",
                    Operator = Operators.Contains,
                    Text = "上海"
                }
            ]
        };

        var (rows, total) = await service.PageAsync(page, CurrentTenant(2));
        var all = await service.GetAllAsync(CurrentTenant(2));

        Assert.Equal(2, total);
        Assert.Equal(3, Assert.Single(rows).id);
        Assert.Equal([3, 2], all.Select(row => row.id));
        Assert.Equal([2L, 2L], source.ReadTenantIds);
    }

    [Fact]
    public async Task GetAsync_returns_an_empty_view_model_when_the_record_does_not_exist()
    {
        var service = CreateService(new InMemoryGoodsownerDataSource());

        var row = await service.GetAsync(404);

        Assert.Equal(0, row.id);
        Assert.Equal(string.Empty, row.goods_owner_name);
    }

    [Fact]
    public async Task AddAsync_enforces_same_tenant_name_and_overrides_identity_and_audit_fields()
    {
        var source = new InMemoryGoodsownerDataSource();
        source.Rows.Add(Owner(1, "已有货主", 7, DateTime.Today));
        var service = CreateService(source);
        var input = Input(999, "新货主");
        input.creator = "伪造创建人";

        Assert.Equal((0, "exists_entity"),
            await service.AddAsync(Input(0, "已有货主"), CurrentTenant(7, "alice")));
        var created = await service.AddAsync(input, CurrentTenant(7, "alice"));

        Assert.True(created.id > 0);
        Assert.Equal("save_success", created.msg);
        var row = Assert.Single(source.Rows, owner => owner.goods_owner_name == "新货主");
        Assert.Equal(7, row.tenant_id);
        Assert.Equal("alice", row.creator);
        Assert.Equal(row.create_time, row.last_update_time);
        Assert.NotEqual(default, row.create_time);
    }

    [Fact]
    public async Task UpdateAsync_preserves_original_tenant_and_audit_fields_and_reports_outcomes()
    {
        var source = new InMemoryGoodsownerDataSource();
        var originalCreated = new DateTime(2026, 8, 16, 8, 0, 0);
        source.Rows.AddRange([
            Owner(1, "货主甲", 5, originalCreated) with { creator = "creator" },
            Owner(2, "货主乙", 5, originalCreated),
            Owner(3, "货主乙", 6, originalCreated)
        ]);
        var service = CreateService(source);

        Assert.Equal((false, "not_exists_entity"), await service.UpdateAsync(Input(404, "不存在")));
        Assert.Equal((false, "exists_entity"), await service.UpdateAsync(Input(1, "货主乙")));
        Assert.Equal((true, "save_success"), await service.UpdateAsync(Input(1, "货主甲-新")));

        var row = Assert.Single(source.Rows, owner => owner.id == 1);
        Assert.Equal(5, row.tenant_id);
        Assert.Equal("creator", row.creator);
        Assert.Equal(originalCreated, row.create_time);
        Assert.Equal("货主甲-新", row.goods_owner_name);
        Assert.True(row.last_update_time > originalCreated);
    }

    [Fact]
    public async Task Delete_and_excel_preserve_messages_duplicate_rules_and_atomicity()
    {
        var source = new InMemoryGoodsownerDataSource();
        source.Rows.AddRange([
            Owner(1, "待删除", 1, DateTime.Today),
            Owner(2, "数据库已有", 8, DateTime.Today)
        ]);
        var service = CreateService(source);

        Assert.Equal((true, "delete_success"), await service.DeleteAsync(1));
        Assert.Equal((false, "delete_failed"), await service.DeleteAsync(1));

        var rejectedInput = new List<GoodsownerImportViewModel>
        {
            Excel("新货主"),
            Excel("数据库已有")
        };
        var rejected = await service.ExcelAsync(rejectedInput, CurrentTenant(8, "importer"));
        Assert.False(rejected.flag);
        Assert.Equal("数据库已有", Assert.Single(rejected.errorData).goods_owner_name);
        Assert.DoesNotContain(source.Rows, row => row.goods_owner_name == "新货主");

        var accepted = await service.ExcelAsync([
            Excel("文件内重名"),
            Excel("文件内重名")
        ], CurrentTenant(8, "importer"));
        Assert.True(accepted.flag);
        Assert.Empty(accepted.errorData);
        var imported = source.Rows.Where(row => row.goods_owner_name == "文件内重名").ToList();
        Assert.Equal(2, imported.Count);
        Assert.All(imported, row =>
        {
            Assert.Equal(8, row.tenant_id);
            Assert.Equal("importer", row.creator);
            Assert.True(row.is_valid);
            Assert.Equal(row.create_time, row.last_update_time);
        });
    }

    [Database.DevelopmentMySqlFact]
    public async Task Dapper_goodsowner_crud_import_and_search_use_isolated_wms_table()
    {
        var sourceConnectionString = Environment.GetEnvironmentVariable("MODERNWMS_TEST_MYSQL")!;
        var source = new MySqlConnectionStringBuilder(sourceConnectionString);
        Assert.Contains(source.Server, ["127.0.0.1", "localhost", "::1"], StringComparer.OrdinalIgnoreCase);
        Assert.Equal("ruoyi-vue-pro", source.Database);

        var databaseName = $"modernwms_goodsowner_test_{Guid.NewGuid():N}";
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
                CREATE TABLE `wms_goodsowner` (
                    `id` INT NOT NULL AUTO_INCREMENT PRIMARY KEY,
                    `goods_owner_name` LONGTEXT NOT NULL,
                    `city` LONGTEXT NOT NULL,
                    `address` LONGTEXT NOT NULL,
                    `manager` LONGTEXT NOT NULL,
                    `contact_tel` LONGTEXT NOT NULL,
                    `creator` LONGTEXT NOT NULL,
                    `create_time` DATETIME(6) NOT NULL,
                    `last_update_time` DATETIME(6) NOT NULL,
                    `is_valid` TINYINT(1) NOT NULL,
                    `tenant_id` BIGINT NOT NULL,
                    INDEX `ix_goodsowner_tenant_name` (`tenant_id`, `goods_owner_name`(128))
                ) ENGINE=InnoDB;
                INSERT INTO `wms_goodsowner`
                    (`goods_owner_name`,`city`,`address`,`manager`,`contact_tel`,`creator`,
                     `create_time`,`last_update_time`,`is_valid`,`tenant_id`)
                VALUES
                    ('其他租户','广州','地址','','','other',NOW(6),NOW(6),1,2),
                    ('已有货主','深圳','地址','','','alice',NOW(6),NOW(6),1,1);
                """);

            await using var factory = new MySqlConnectionFactory(isolated.ConnectionString);
            var service = new GoodsownerService(factory, new EchoLocalizer());
            var tenant = CurrentTenant(1, "alice");

            var (injected, injectedTotal) = await service.PageAsync(new PageSearch
            {
                pageIndex = 1,
                pageSize = 10,
                searchObjects =
                [
                    new SearchObject
                    {
                        Name = "goods_owner_name",
                        Operator = Operators.Contains,
                        Text = "货主' OR 1=1 --"
                    }
                ]
            }, tenant);
            Assert.Empty(injected);
            Assert.Equal(0, injectedTotal);

            Assert.Equal((0, "exists_entity"), await service.AddAsync(Input(0, "已有货主"), tenant));
            var created = await service.AddAsync(Input(0, "新增货主"), tenant);
            Assert.True(created.id > 0);
            Assert.Equal((true, "save_success"), await service.UpdateAsync(Input(created.id, "更新货主")));
            Assert.Equal("更新货主", (await service.GetAsync(created.id)).goods_owner_name);

            var rejected = await service.ExcelAsync([
                Excel("不得落库"),
                Excel("已有货主")
            ], tenant);
            Assert.False(rejected.flag);
            Assert.DoesNotContain(await service.GetAllAsync(tenant), row => row.goods_owner_name == "不得落库");

            var imported = await service.ExcelAsync([Excel("导入一"), Excel("导入二")], tenant);
            Assert.True(imported.flag);
            Assert.Equal(4, (await service.GetAllAsync(tenant)).Count);

            Assert.Equal((true, "delete_success"), await service.DeleteAsync(created.id));
            Assert.Equal((false, "delete_failed"), await service.DeleteAsync(created.id));
        }
        finally
        {
            await adminConnection.ExecuteAsync($"DROP DATABASE IF EXISTS `{databaseName}`;");
        }
    }

    private static GoodsownerService CreateService(IGoodsownerDataSource source) =>
        new(source, new EchoLocalizer());

    private static CurrentUser CurrentTenant(long tenantId, string userName = "user") =>
        new() { tenant_id = tenantId, user_name = userName };

    private static GoodsownerData Owner(int id, string name, long tenantId, DateTime created) => new(
        id, name, "深圳", "地址", "负责人", "13800000000", "creator",
        created, created, true, tenantId);

    private static GoodsownerViewModel Input(int id, string name) => new()
    {
        id = id,
        goods_owner_name = name,
        city = "深圳",
        address = "地址",
        manager = "负责人",
        contact_tel = "13800000000",
        is_valid = true
    };

    private static GoodsownerImportViewModel Excel(string name) => new()
    {
        goods_owner_name = name,
        city = "深圳",
        address = "地址",
        manager = "负责人",
        contact_tel = "13800000000"
    };

    private sealed class InMemoryGoodsownerDataSource : IGoodsownerDataSource
    {
        public List<GoodsownerData> Rows { get; } = [];
        public List<long> ReadTenantIds { get; } = [];

        public Task<(List<GoodsownerData> Rows, int Total)> PageAsync(PageSearch pageSearch, long tenantId)
        {
            ReadTenantIds.Add(tenantId);
            IEnumerable<GoodsownerData> query = Rows.Where(row => row.tenant_id == tenantId);
            foreach (var filter in pageSearch.searchObjects)
            {
                if (filter.Name == "goods_owner_name" && filter.Operator == Operators.Contains)
                {
                    query = query.Where(row => row.goods_owner_name.Contains(filter.Text, StringComparison.Ordinal));
                }
            }

            var total = query.Count();
            return Task.FromResult((query.OrderByDescending(row => row.create_time)
                .Skip((pageSearch.pageIndex - 1) * pageSearch.pageSize)
                .Take(pageSearch.pageSize).ToList(), total));
        }

        public Task<List<GoodsownerData>> GetAllAsync(long tenantId)
        {
            ReadTenantIds.Add(tenantId);
            return Task.FromResult(Rows.Where(row => row.tenant_id == tenantId)
                .OrderByDescending(row => row.create_time).ToList());
        }

        public Task<GoodsownerData?> GetAsync(int id) =>
            Task.FromResult(Rows.SingleOrDefault(row => row.id == id));

        public Task<GoodsownerAddResult> AddAsync(GoodsownerData owner)
        {
            if (Rows.Any(row => row.tenant_id == owner.tenant_id
                && row.goods_owner_name == owner.goods_owner_name))
            {
                return Task.FromResult(new GoodsownerAddResult(GoodsownerWriteStatus.Duplicate, 0));
            }

            var id = Rows.Count == 0 ? 1 : Rows.Max(row => row.id) + 1;
            Rows.Add(owner with { id = id });
            return Task.FromResult(new GoodsownerAddResult(GoodsownerWriteStatus.Succeeded, id));
        }

        public Task<GoodsownerWriteStatus> UpdateAsync(GoodsownerData owner)
        {
            var index = Rows.FindIndex(row => row.id == owner.id);
            if (index < 0)
            {
                return Task.FromResult(GoodsownerWriteStatus.NotFound);
            }

            var original = Rows[index];
            if (Rows.Any(row => row.id != owner.id
                && row.tenant_id == original.tenant_id
                && row.goods_owner_name == owner.goods_owner_name))
            {
                return Task.FromResult(GoodsownerWriteStatus.Duplicate);
            }

            Rows[index] = owner with
            {
                creator = original.creator,
                create_time = original.create_time,
                tenant_id = original.tenant_id
            };
            return Task.FromResult(GoodsownerWriteStatus.Succeeded);
        }

        public Task<bool> DeleteAsync(int id) =>
            Task.FromResult(Rows.RemoveAll(row => row.id == id) > 0);

        public Task<GoodsownerImportResult> ImportAsync(
            IReadOnlyCollection<GoodsownerData> owners,
            long tenantId)
        {
            var duplicateNames = owners.Select(row => row.goods_owner_name)
                .Where(name => Rows.Any(row => row.tenant_id == tenantId
                    && row.goods_owner_name == name))
                .Distinct(StringComparer.Ordinal)
                .ToHashSet(StringComparer.Ordinal);
            if (duplicateNames.Count > 0)
            {
                return Task.FromResult(new GoodsownerImportResult(0, duplicateNames));
            }

            foreach (var owner in owners)
            {
                var id = Rows.Count == 0 ? 1 : Rows.Max(row => row.id) + 1;
                Rows.Add(owner with { id = id });
            }
            return Task.FromResult(new GoodsownerImportResult(owners.Count, duplicateNames));
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
