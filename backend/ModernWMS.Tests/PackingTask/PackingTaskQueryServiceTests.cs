using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using ModernWMS.Core.DBContext;
using ModernWMS.Core.DBContext.Entities;
using ModernWMS.Core.DynamicSearch;
using ModernWMS.Core.JWT;
using ModernWMS.Core.Models;
using ModernWMS.WMS.Entities.Models;
using ModernWMS.WMS.Services;

namespace ModernWMS.Tests.PackingTask;

public class PackingTaskQueryServiceTests
{
    [Fact]
    public async Task PageAsync_returns_feature_disabled_without_reading_tasks()
    {
        await using var wmsDatabase = CreateWmsDatabase();
        await using var ruoyiDatabase = CreateRuoyiDatabase();
        var service = CreateService(ruoyiDatabase, wmsDatabase, enabled: false);

        var result = await service.PageAsync(new PageSearch(), CurrentTenant());

        Assert.False(result.IsSuccess);
        Assert.Equal(0, result.Totals);
        Assert.Empty(result.Data);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task PageAsync_fails_closed_when_warehouse_readiness_is_incomplete(
        bool addErpWarehouse,
        bool addValidTenantBinding)
    {
        await using var wmsDatabase = CreateWmsDatabase();
        await using var ruoyiDatabase = CreateRuoyiDatabase();
        if (addErpWarehouse)
        {
            await ruoyiDatabase.Warehouses.AddAsync(new ErpWarehouseEntity { id = 320118 });
        }
        if (addValidTenantBinding)
        {
            await wmsDatabase.GetDbSet<WarehouseEntity>().AddAsync(ValidWarehouseBinding());
        }
        await ruoyiDatabase.SaveChangesAsync();
        await wmsDatabase.SaveChangesAsync();

        var result = await CreateService(ruoyiDatabase, wmsDatabase).PageAsync(new PageSearch(), CurrentTenant());

        Assert.False(result.IsSuccess);
        Assert.Empty(result.Data);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PageAsync_fails_closed_when_tenant_binding_is_invalid_or_conflicting(bool conflicting)
    {
        await using var wmsDatabase = CreateWmsDatabase();
        await using var ruoyiDatabase = CreateRuoyiDatabase();
        await ruoyiDatabase.Warehouses.AddAsync(new ErpWarehouseEntity { id = 320118 });
        var first = ValidWarehouseBinding();
        first.is_valid = conflicting;
        await wmsDatabase.GetDbSet<WarehouseEntity>().AddAsync(first);
        if (conflicting)
        {
            var second = ValidWarehouseBinding();
            second.id = 2;
            await wmsDatabase.GetDbSet<WarehouseEntity>().AddAsync(second);
        }
        await ruoyiDatabase.SaveChangesAsync();
        await wmsDatabase.SaveChangesAsync();

        var result = await CreateService(ruoyiDatabase, wmsDatabase).PageAsync(new PageSearch(), CurrentTenant());

        Assert.False(result.IsSuccess);
        Assert.Empty(result.Data);
    }

    [Fact]
    public async Task PageAsync_filters_orders_and_preserves_nullable_item_quantities()
    {
        await using var wmsDatabase = CreateWmsDatabase();
        await using var ruoyiDatabase = CreateRuoyiDatabase();
        await AddReadinessAsync(ruoyiDatabase, wmsDatabase);
        var commonTime = new DateTime(2026, 8, 15, 8, 0, 0);
        await ruoyiDatabase.PackingTasks.AddRangeAsync(
            Task(1, 101, "PACK-101", 320118, commonTime),
            Task(2, 102, "PACK-102", 320118, commonTime),
            Task(3, 103, "OTHER-WAREHOUSE", 9, commonTime.AddDays(1)),
            Task(4, 104, "CANCELED", 320118, commonTime.AddDays(1), canceled: true),
            Task(5, 105, "DELETED", 320118, commonTime.AddDays(1), deleted: true),
            Task(6, 106, "NULL-TIME", 320118, null));
        await ruoyiDatabase.PackingTaskItems.AddRangeAsync(
            new ErpPackingTaskItemEntity
            {
                id = 11,
                sellfox_item_id = 1001,
                sellfox_task_id = 102,
                commodity_name = null,
                commodity_sku = "SKU-102",
                fn_sku = null,
                msku = "MSKU-102",
                task_num = null,
                quantity_shipped = 0,
                stock_available = null
            },
            new ErpPackingTaskItemEntity
            {
                id = 12,
                sellfox_item_id = 1002,
                sellfox_task_id = 102,
                commodity_name = "soft deleted",
                source_deleted = true
            });
        await ruoyiDatabase.SaveChangesAsync();

        var result = await CreateService(ruoyiDatabase, wmsDatabase).PageAsync(new PageSearch(), CurrentTenant());

        Assert.True(result.IsSuccess);
        Assert.Equal(3, result.Totals);
        Assert.Equal([102L, 101L, 106L], result.Data.Select(t => t.sellfox_task_id).ToArray());
        var item = Assert.Single(result.Data[0].item_list);
        Assert.Null(item.commodity_name);
        Assert.Null(item.fn_sku);
        Assert.Null(item.task_num);
        Assert.Equal(0, item.quantity_shipped);
        Assert.Null(item.stock_available);
        Assert.Empty(result.Data[1].item_list);
    }

    [Fact]
    public async Task PageAsync_searches_only_task_and_product_identifiers()
    {
        await using var wmsDatabase = CreateWmsDatabase();
        await using var ruoyiDatabase = CreateRuoyiDatabase();
        await AddReadinessAsync(ruoyiDatabase, wmsDatabase);
        await ruoyiDatabase.PackingTasks.AddRangeAsync(
            Task(1, 101, "PACK-101", 320118, DateTime.UtcNow),
            Task(2, 102, "PACK-102", 320118, DateTime.UtcNow));
        await ruoyiDatabase.PackingTaskItems.AddAsync(new ErpPackingTaskItemEntity
        {
            id = 11,
            sellfox_item_id = 1001,
            sellfox_task_id = 102,
            fn_sku = "FNSKU-HIT"
        });
        await ruoyiDatabase.SaveChangesAsync();
        var page = new PageSearch
        {
            searchObjects = [new SearchObject { Name = "keyword", Text = "FNSKU-HIT" }]
        };

        var result = await CreateService(ruoyiDatabase, wmsDatabase).PageAsync(page, CurrentTenant());

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Totals);
        Assert.Equal(102, Assert.Single(result.Data).sellfox_task_id);
    }

    private static PackingTaskQueryService CreateService(
        RuoyiDbContext ruoyiDatabase,
        SqlDBContext wmsDatabase,
        bool enabled = true)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Features:PackingTaskFirstStep"] = enabled.ToString()
            })
            .Build();
        return new PackingTaskQueryService(ruoyiDatabase, wmsDatabase, configuration);
    }

    private static async Task AddReadinessAsync(RuoyiDbContext ruoyiDatabase, SqlDBContext wmsDatabase)
    {
        await ruoyiDatabase.Warehouses.AddAsync(new ErpWarehouseEntity { id = 320118 });
        await wmsDatabase.GetDbSet<WarehouseEntity>().AddAsync(ValidWarehouseBinding());
        await ruoyiDatabase.SaveChangesAsync();
        await wmsDatabase.SaveChangesAsync();
    }

    private static ErpPackingTaskEntity Task(
        long id,
        long sellfoxTaskId,
        string taskNo,
        long warehouseId,
        DateTime? sourceCreateTime,
        bool canceled = false,
        bool deleted = false) => new()
        {
            id = id,
            sellfox_task_id = sellfoxTaskId,
            packing_task_sn = taskNo,
            warehouse_id = warehouseId,
            source_create_time = sourceCreateTime,
            source_canceled = canceled,
            source_deleted = deleted
        };

    private static WarehouseEntity ValidWarehouseBinding() => new()
    {
        id = 1,
        tenant_id = 1,
        warehouse_name = "深圳仓",
        erp_warehouse_id = 320118,
        is_valid = true
    };

    private static CurrentUser CurrentTenant() => new() { tenant_id = 1 };

    private static SqlDBContext CreateWmsDatabase()
    {
        var options = new DbContextOptionsBuilder<SqlDBContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new SqlDBContext(options);
    }

    private static RuoyiDbContext CreateRuoyiDatabase()
    {
        var options = new DbContextOptionsBuilder<RuoyiDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new RuoyiDbContext(options);
    }
}
