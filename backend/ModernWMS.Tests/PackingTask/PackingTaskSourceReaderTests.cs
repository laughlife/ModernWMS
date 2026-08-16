using Microsoft.EntityFrameworkCore;
using ModernWMS.Core.DBContext;
using ModernWMS.Core.DBContext.Entities;
using ModernWMS.WMS.Entities.ViewModels.PackingTask;
using ModernWMS.WMS.Services.PackingTask;

namespace ModernWMS.Tests.PackingTask;

public class PackingTaskSourceReaderTests
{
    [Fact]
    public async Task ReadAsync_preserves_task_and_item_boundaries_and_computes_stable_version()
    {
        await using var database = CreateDatabase();
        database.PackingTasks.AddRange(
            Task(1, 101, "TASK-101", 320118, "[{\"weight\":2,\"id\":\"B-2\"},{\"id\":\"B-1\"}]"),
            Task(2, 102, "TASK-102", 320118, "[{\"id\":\"B-3\"}]")
        );
        database.PackingTaskItems.AddRange(
            Item(13, 1003, 102, "SAME-SKU", 3),
            Item(12, 1002, 101, "SAME-SKU", 2),
            Item(11, 1001, 101, "SAME-SKU", 1));
        await database.SaveChangesAsync();

        var reader = new PackingTaskSourceReader(database);
        var first = await reader.ReadAsync([102, 101]);
        var second = await reader.ReadAsync([101, 102]);

        Assert.Equal([101L, 102L], first.Select(x => x.SourceTaskId));
        Assert.Equal([1001L, 1002L], first[0].Items.Select(x => x.SourceItemId));
        Assert.Single(first[1].Items);
        Assert.Equal(["B-2", "B-1"], first[0].Boxes.Select(x => x.SourceBoxIdentity));
        Assert.Equal(first.Select(x => x.SourceVersion), second.Select(x => x.SourceVersion));
        Assert.All(first, task => Assert.Matches("^[0-9a-f]{64}$", task.SourceVersion));
    }

    [Fact]
    public async Task ReadAsync_changes_version_when_latest_item_quantity_changes()
    {
        await using var database = CreateDatabase();
        database.PackingTasks.Add(Task(1, 101, "TASK-101", 320118, "[{\"id\":\"B-1\"}]"));
        var item = Item(11, 1001, 101, "SKU-1", 1);
        database.PackingTaskItems.Add(item);
        await database.SaveChangesAsync();
        var reader = new PackingTaskSourceReader(database);
        var before = Assert.Single(await reader.ReadAsync([101]));

        item.task_num = 2;
        await database.SaveChangesAsync();
        var after = Assert.Single(await reader.ReadAsync([101]));

        Assert.NotEqual(before.SourceVersion, after.SourceVersion);
    }

    [Fact]
    public async Task ReadAsync_fails_closed_for_duplicate_item_identity()
    {
        await using var database = CreateDatabase();
        database.PackingTasks.Add(Task(1, 101, "TASK-101", 320118, "[{\"id\":\"B-1\"}]"));
        database.PackingTaskItems.AddRange(
            Item(11, 1001, 101, "SKU-1", 1),
            Item(12, 1001, 101, "SKU-2", 1));
        await database.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => new PackingTaskSourceReader(database).ReadAsync([101]));

        Assert.Contains("商品ID", exception.Message);
    }

    [Fact]
    public async Task ReadAsync_fails_the_whole_batch_and_lists_every_missing_task_id()
    {
        await using var database = CreateDatabase();
        database.PackingTasks.Add(Task(1, 101, "TASK-101", 320118, "[{\"id\":\"B-1\"}]"));
        database.PackingTaskItems.Add(Item(11, 1001, 101, "SKU-1", 1));
        await database.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => new PackingTaskSourceReader(database).ReadAsync([103, 101, 102]));

        Assert.Contains("102, 103", exception.Message);
        Assert.Contains("缺少", exception.Message);
    }

    [Fact]
    public async Task ReadAsync_reports_cancelled_tombstone_without_validating_warehouse_items_or_boxes()
    {
        await using var database = CreateDatabase();
        var task = Task(1, 101, "TASK-101", 320118, "not-json");
        task.source_canceled = true;
        task.warehouse_id = null;
        task.task_num = null;
        database.PackingTasks.Add(task);
        database.PackingTaskItems.Add(new ErpPackingTaskItemEntity
        {
            id = 11,
            sellfox_task_id = 101,
            sellfox_item_id = 0,
            task_num = -1
        });
        await database.SaveChangesAsync();

        var snapshot = Assert.Single(await new PackingTaskSourceReader(database).ReadAsync([101]));

        Assert.True(snapshot.IsCancelled);
        Assert.Equal(101, snapshot.SourceTaskId);
        Assert.Equal(0, snapshot.WarehouseId);
        Assert.Empty(snapshot.Items);
        Assert.Empty(snapshot.Boxes);
        Assert.Empty(snapshot.CartonsJson);
    }

    [Fact]
    public async Task ReadAsync_accepts_an_active_task_without_physical_boxes_before_weighing()
    {
        await using var database = CreateDatabase();
        database.PackingTasks.Add(Task(1, 101, "TASK-101", 320118, "[]"));
        database.PackingTaskItems.Add(Item(11, 1001, 101, "SKU-1", 1));
        await database.SaveChangesAsync();

        var snapshot = Assert.Single(await new PackingTaskSourceReader(database).ReadAsync([101]));

        Assert.False(snapshot.IsCancelled);
        Assert.Single(snapshot.Items);
        Assert.Empty(snapshot.Boxes);
        Assert.Equal("[]", snapshot.CartonsJson);
    }

    [Fact]
    public async Task ReadAsync_rejects_active_task_without_stable_box_identity()
    {
        await using var database = CreateDatabase();
        database.PackingTasks.Add(Task(1, 101, "TASK-101", 320118, "[{\"weight\":1}]") );
        database.PackingTaskItems.Add(Item(11, 1001, 101, "SKU-1", 1));
        await database.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => new PackingTaskSourceReader(database).ReadAsync([101]));

        Assert.Contains("稳定箱ID", exception.Message);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task ReadAsync_rejects_non_positive_header_task_quantity(int? quantity)
    {
        await using var database = CreateDatabase();
        var task = Task(1, 101, "TASK-101", 320118, "[{\"id\":\"B-1\"}]");
        task.task_num = quantity;
        database.PackingTasks.Add(task);
        database.PackingTaskItems.Add(Item(11, 1001, 101, "SKU-1", 1));
        await database.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => new PackingTaskSourceReader(database).ReadAsync([101]));

        Assert.Contains("task_num 必须大于 0", exception.Message);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task ReadAsync_rejects_non_positive_item_task_quantity(int? quantity)
    {
        await using var database = CreateDatabase();
        database.PackingTasks.Add(Task(1, 101, "TASK-101", 320118, "[{\"id\":\"B-1\"}]"));
        var item = Item(11, 1001, 101, "SKU-1", 1);
        item.task_num = quantity;
        database.PackingTaskItems.Add(item);
        await database.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => new PackingTaskSourceReader(database).ReadAsync([101]));

        Assert.Contains("task_num 必须大于 0", exception.Message);
    }

    [Fact]
    public async Task ReadAsync_rejects_active_task_without_items()
    {
        await using var database = CreateDatabase();
        database.PackingTasks.Add(Task(1, 101, "TASK-101", 320118, "[{\"id\":\"B-1\"}]"));
        await database.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => new PackingTaskSourceReader(database).ReadAsync([101]));

        Assert.Contains("未包含有效商品", exception.Message);
    }

    [Fact]
    public async Task VerifyCapabilityAsync_uses_model_contract_for_non_relational_tests()
    {
        await using var database = CreateDatabase();

        var capability = await new PackingTaskSourceReader(database).VerifyCapabilityAsync();

        Assert.True(capability.IsSupported, capability.Error);
    }

    [Fact]
    public async Task ReadAsync_fails_before_query_when_required_source_column_is_missing()
    {
        await using var database = CreateDatabase();
        var reader = new PackingTaskSourceReader(
            database,
            _ => System.Threading.Tasks.Task.FromResult(new PackingTaskSourceCapability(
                false,
                "共享表缺少必需字段 ruiyi_sellfox_packing_task.cartons_json")));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => reader.ReadAsync([101]));

        Assert.Contains("cartons_json", exception.Message);
    }

    private static RuoyiDbContext CreateDatabase()
    {
        var options = new DbContextOptionsBuilder<RuoyiDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new RuoyiDbContext(options);
    }

    private static ErpPackingTaskEntity Task(
        long id, long sourceTaskId, string taskNo, long warehouseId, string cartonsJson) => new()
    {
        id = id,
        sellfox_task_id = sourceTaskId,
        packing_task_sn = taskNo,
        warehouse_id = warehouseId,
        warehouse_name = "Warehouse",
        cartons_json = cartonsJson,
        task_num = 1,
        source_hash = "header-hash"
    };

    private static ErpPackingTaskItemEntity Item(
        long id, long sourceItemId, long sourceTaskId, string sku, int quantity) => new()
    {
        id = id,
        sellfox_item_id = sourceItemId,
        sellfox_task_id = sourceTaskId,
        sku = sku,
        task_num = quantity,
        source_hash = $"item-{sourceItemId}"
    };
}
