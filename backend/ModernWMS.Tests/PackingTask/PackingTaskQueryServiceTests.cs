using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using ModernWMS.Core.DBContext;
using ModernWMS.Core.DBContext.Entities;
using ModernWMS.Core.DynamicSearch;
using ModernWMS.Core.JWT;
using ModernWMS.Core.Models;
using ModernWMS.WMS.Services;
using ModernWMS.WMS.Entities.Models;

namespace ModernWMS.Tests.PackingTask;

public class PackingTaskQueryServiceTests
{
    [Fact]
    public async Task PageAsync_returns_feature_disabled_without_reading_tasks()
    {
        await using var ruoyiDatabase = CreateRuoyiDatabase();
        var service = CreateService(ruoyiDatabase, enabled: false);

        var result = await service.PageAsync(new PageSearch(), CurrentTenant());

        Assert.False(result.IsSuccess);
        Assert.Equal(0, result.Totals);
        Assert.Empty(result.Data);
    }

    [Fact]
    public async Task PageAsync_returns_tasks_from_all_warehouses_without_tenant_binding()
    {
        await using var ruoyiDatabase = CreateRuoyiDatabase();
        await ruoyiDatabase.PackingTasks.AddRangeAsync(
            Task(1, 101, "SHENZHEN", 320118, DateTime.UtcNow),
            Task(2, 102, "OTHER-WAREHOUSE", 9, DateTime.UtcNow));
        await ruoyiDatabase.SaveChangesAsync();

        var result = await CreateService(ruoyiDatabase).PageAsync(new PageSearch(), CurrentTenant());

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Totals);
        Assert.Equal([101L, 102L], result.Data.Select(t => t.sellfox_task_id).Order().ToArray());
    }

    [Fact]
    public async Task PageAsync_filters_orders_and_preserves_nullable_item_quantities()
    {
        await using var ruoyiDatabase = CreateRuoyiDatabase();
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

        var result = await CreateService(ruoyiDatabase).PageAsync(new PageSearch(), CurrentTenant());

        Assert.True(result.IsSuccess);
        Assert.Equal(4, result.Totals);
        Assert.Equal([103L, 102L, 101L, 106L], result.Data.Select(t => t.sellfox_task_id).ToArray());
        var item = Assert.Single(result.Data[1].item_list);
        Assert.Null(item.commodity_name);
        Assert.Null(item.fn_sku);
        Assert.Null(item.task_num);
        Assert.Equal(0, item.quantity_shipped);
        Assert.Null(item.stock_available);
        Assert.Empty(result.Data[2].item_list);
    }

    [Fact]
    public async Task PageAsync_searches_only_task_and_product_identifiers()
    {
        await using var ruoyiDatabase = CreateRuoyiDatabase();
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

        var result = await CreateService(ruoyiDatabase).PageAsync(page, CurrentTenant());

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Totals);
        Assert.Equal(102, Assert.Single(result.Data).sellfox_task_id);
    }

    [Fact]
    public async Task PageAsync_filters_by_authorized_warehouse_and_excludes_tasks_in_active_orders()
    {
        await using var ruoyiDatabase = CreateRuoyiDatabase();
        await using var wmsDatabase = new SqlDBContext(new DbContextOptionsBuilder<SqlDBContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        await ruoyiDatabase.PackingTasks.AddRangeAsync(
            Task(1, 101, "ACTIVE", 320118, DateTime.UtcNow),
            Task(2, 102, "AVAILABLE", 320118, DateTime.UtcNow),
            Task(3, 103, "OTHER", 9, DateTime.UtcNow));
        var order = new DispatchOrderEntity
        {
            dispatch_no = "PK-1",
            create_idempotency_key = "key-1",
            warehouse_id = 320118,
            status = DispatchOrderStatus.PendingPick,
            create_time = DateTime.Now,
            last_update_time = DateTime.Now
        };
        var linked = new DispatchPackingTaskEntity
        {
            dispatch_order = order,
            source_task_id = 101,
            source_task_no = "ACTIVE",
            task_no = "ACTIVE",
            create_time = DateTime.Now,
            last_update_time = DateTime.Now
        };
        linked.SetActiveState(true);
        await wmsDatabase.AddAsync(linked);
        await ruoyiDatabase.SaveChangesAsync();
        await wmsDatabase.SaveChangesAsync();
        var access = new ModernWMS.Tests.DispatchWorkflow.RecordingWarehouseAccess();
        var service = CreateService(ruoyiDatabase, wmsDatabase: wmsDatabase, access: access.Contract);
        var page = new PageSearch
        {
            searchObjects = [new SearchObject { Name = "warehouse_id", Text = "320118" }]
        };

        var result = await service.PageAsync(page, CurrentTenant());

        Assert.Equal(102, Assert.Single(result.Data).sellfox_task_id);
        Assert.Contains(320118, access.CheckedWarehouseIds);
    }

    [Fact]
    public async Task PageAsync_uses_role_authorized_default_instead_of_hardcoded_admin_warehouse()
    {
        await using var ruoyiDatabase = CreateRuoyiDatabase();
        await using var wmsDatabase = new SqlDBContext(new DbContextOptionsBuilder<SqlDBContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        await ruoyiDatabase.PackingTasks.AddRangeAsync(
            Task(1, 101, "ADMIN-DEFAULT", 320118, DateTime.UtcNow),
            Task(2, 102, "ROLE-DEFAULT", 9, DateTime.UtcNow));
        await ruoyiDatabase.SaveChangesAsync();
        var access = new ModernWMS.Tests.DispatchWorkflow.RecordingWarehouseAccess { DefaultWarehouseId = 9 };
        var service = CreateService(ruoyiDatabase, wmsDatabase: wmsDatabase, access: access.Contract);

        var result = await service.PageAsync(new PageSearch(), CurrentTenant());

        Assert.Equal(102, Assert.Single(result.Data).sellfox_task_id);
    }

    private static PackingTaskQueryService CreateService(
        RuoyiDbContext ruoyiDatabase,
        bool enabled = true,
        SqlDBContext? wmsDatabase = null,
        ModernWMS.WMS.IServices.IWarehouseAccessService? access = null)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Features:PackingTaskFirstStep"] = enabled.ToString()
            })
            .Build();
        return wmsDatabase == null
            ? new PackingTaskQueryService(ruoyiDatabase, configuration)
            : new PackingTaskQueryService(ruoyiDatabase, configuration, wmsDatabase, access!);
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

    private static CurrentUser CurrentTenant() => new() { tenant_id = 1 };

    private static RuoyiDbContext CreateRuoyiDatabase()
    {
        var options = new DbContextOptionsBuilder<RuoyiDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new RuoyiDbContext(options);
    }
}
