using ModernWMS.Core.Database;
using ModernWMS.Core.DBContext.Entities;
using ModernWMS.WMS.Entities.ViewModels.PackingTask;
using ModernWMS.WMS.Services.PackingTask;
using Dapper;
using MySqlConnector;

namespace ModernWMS.Tests.PackingTask;

public class PackingTaskSourceReaderTests
{
    [Database.DevelopmentMySqlFact]
    public async Task ReadAsync_reads_the_current_development_sellfox_schema()
    {
        var connectionString = Environment.GetEnvironmentVariable("MODERNWMS_TEST_MYSQL")!;
        var settings = new MySqlConnectionStringBuilder(connectionString);
        Assert.Contains(settings.Server, ["127.0.0.1", "localhost", "::1"], StringComparer.OrdinalIgnoreCase);
        Assert.Equal("ruoyi-vue-pro", settings.Database);

        await using var factory = new MySqlConnectionFactory(connectionString);
        await using var connection = await factory.OpenConnectionAsync();
        var sourceTaskId = await connection.QueryFirstOrDefaultAsync<long?>("""
            SELECT `sellfox_task_id`
            FROM `ruiyi_sellfox_packing_task`
            WHERE `source_deleted` = 0
            ORDER BY `sellfox_task_id`
            LIMIT 1;
            """);
        Assert.NotNull(sourceTaskId);

        var reader = new PackingTaskSourceReader(factory);
        Assert.True((await reader.VerifyCapabilityAsync()).IsSupported);
        var snapshot = Assert.Single(await reader.ReadAsync([sourceTaskId.Value]));
        Assert.Equal(sourceTaskId.Value, snapshot.SourceTaskId);
    }

    [Fact]
    public async Task ReadAsync_preserves_task_and_item_boundaries_and_computes_stable_version()
    {
        var tasks = new List<ErpPackingTaskEntity>
        {
            Task(1, 101, "TASK-101", 320118, "[{\"weight\":2,\"id\":\"B-2\"},{\"id\":\"B-1\"}]"),
            Task(2, 102, "TASK-102", 320118, "[{\"id\":\"B-3\"}]")
        };
        var items = new List<ErpPackingTaskItemEntity>
        {
            Item(13, 1003, 102, "SAME-SKU", 3),
            Item(12, 1002, 101, "SAME-SKU", 2),
            Item(11, 1001, 101, "SAME-SKU", 1)
        };
        var reader = CreateReader(tasks, items);

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
        var tasks = new[] { Task(1, 101, "TASK-101", 320118, "[{\"id\":\"B-1\"}]") };
        var item = Item(11, 1001, 101, "SKU-1", 1);
        var reader = CreateReader(tasks, [item]);
        var before = Assert.Single(await reader.ReadAsync([101]));

        item.task_num = 2;
        var after = Assert.Single(await reader.ReadAsync([101]));

        Assert.NotEqual(before.SourceVersion, after.SourceVersion);
    }

    [Fact]
    public async Task ReadAsync_fails_closed_for_duplicate_item_identity()
    {
        var reader = CreateReader(
            [Task(1, 101, "TASK-101", 320118, "[{\"id\":\"B-1\"}]")],
            [Item(11, 1001, 101, "SKU-1", 1), Item(12, 1001, 101, "SKU-2", 1)]);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => reader.ReadAsync([101]));

        Assert.Contains("商品ID", exception.Message);
    }

    [Fact]
    public async Task ReadAsync_fails_the_whole_batch_and_lists_every_missing_task_id()
    {
        var reader = CreateReader(
            [Task(1, 101, "TASK-101", 320118, "[{\"id\":\"B-1\"}]")],
            [Item(11, 1001, 101, "SKU-1", 1)]);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => reader.ReadAsync([103, 101, 102]));

        Assert.Contains("102, 103", exception.Message);
        Assert.Contains("缺少", exception.Message);
    }

    [Fact]
    public async Task ReadAsync_reports_cancelled_tombstone_without_validating_warehouse_items_or_boxes()
    {
        var task = Task(1, 101, "TASK-101", 320118, "not-json");
        task.source_canceled = true;
        task.warehouse_id = null;
        task.task_num = null;
        var reader = CreateReader(
            [task],
            [new ErpPackingTaskItemEntity
            {
                id = 11,
                sellfox_task_id = 101,
                sellfox_item_id = 0,
                task_num = -1
            }]);

        var snapshot = Assert.Single(await reader.ReadAsync([101]));

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
        var reader = CreateReader(
            [Task(1, 101, "TASK-101", 320118, "[]")],
            [Item(11, 1001, 101, "SKU-1", 1)]);

        var snapshot = Assert.Single(await reader.ReadAsync([101]));

        Assert.False(snapshot.IsCancelled);
        Assert.Single(snapshot.Items);
        Assert.Empty(snapshot.Boxes);
        Assert.Equal("[]", snapshot.CartonsJson);
    }

    [Fact]
    public async Task ReadAsync_rejects_active_task_without_stable_box_identity()
    {
        var reader = CreateReader(
            [Task(1, 101, "TASK-101", 320118, "[{\"weight\":1}]")],
            [Item(11, 1001, 101, "SKU-1", 1)]);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => reader.ReadAsync([101]));

        Assert.Contains("稳定箱ID", exception.Message);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task ReadAsync_rejects_non_positive_header_task_quantity(int? quantity)
    {
        var task = Task(1, 101, "TASK-101", 320118, "[{\"id\":\"B-1\"}]");
        task.task_num = quantity;
        var reader = CreateReader([task], [Item(11, 1001, 101, "SKU-1", 1)]);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => reader.ReadAsync([101]));

        Assert.Contains("task_num 必须大于 0", exception.Message);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task ReadAsync_rejects_non_positive_item_task_quantity(int? quantity)
    {
        var item = Item(11, 1001, 101, "SKU-1", 1);
        item.task_num = quantity;
        var reader = CreateReader(
            [Task(1, 101, "TASK-101", 320118, "[{\"id\":\"B-1\"}]")],
            [item]);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => reader.ReadAsync([101]));

        Assert.Contains("task_num 必须大于 0", exception.Message);
    }

    [Fact]
    public async Task ReadAsync_rejects_active_task_without_items()
    {
        var reader = CreateReader(
            [Task(1, 101, "TASK-101", 320118, "[{\"id\":\"B-1\"}]")],
            []);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => reader.ReadAsync([101]));

        Assert.Contains("未包含有效商品", exception.Message);
    }

    [Fact]
    public async Task VerifyCapabilityAsync_returns_injected_capability_without_opening_a_connection()
    {
        var reader = CreateReader([], []);

        var capability = await reader.VerifyCapabilityAsync();

        Assert.True(capability.IsSupported, capability.Error);
    }

    [Fact]
    public async Task ReadAsync_fails_before_query_when_required_source_column_is_missing()
    {
        var sourceRead = false;
        var reader = new PackingTaskSourceReader(
            new ThrowingConnectionFactory(),
            _ => System.Threading.Tasks.Task.FromResult(new PackingTaskSourceCapability(
                false,
                "共享表缺少必需字段 ruiyi_sellfox_packing_task.cartons_json")),
            (_, _) =>
            {
                sourceRead = true;
                return System.Threading.Tasks.Task.FromResult((
                    (IReadOnlyList<ErpPackingTaskEntity>)[],
                    (IReadOnlyList<ErpPackingTaskItemEntity>)[]));
            });

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => reader.ReadAsync([101]));

        Assert.Contains("cartons_json", exception.Message);
        Assert.False(sourceRead);
    }

    private static PackingTaskSourceReader CreateReader(
        IReadOnlyCollection<ErpPackingTaskEntity> tasks,
        IReadOnlyCollection<ErpPackingTaskItemEntity> items) => new(
            new ThrowingConnectionFactory(),
            _ => System.Threading.Tasks.Task.FromResult(new PackingTaskSourceCapability(true, string.Empty)),
            (requestedIds, _) =>
            {
                var ids = requestedIds.ToHashSet();
                return System.Threading.Tasks.Task.FromResult((
                    (IReadOnlyList<ErpPackingTaskEntity>)tasks
                        .Where(x => ids.Contains(x.sellfox_task_id))
                        .OrderBy(x => x.sellfox_task_id)
                        .ThenBy(x => x.id)
                        .ToArray(),
                    (IReadOnlyList<ErpPackingTaskItemEntity>)items
                        .Where(x => ids.Contains(x.sellfox_task_id) && !x.source_deleted)
                        .OrderBy(x => x.sellfox_task_id)
                        .ThenBy(x => x.sellfox_item_id)
                        .ThenBy(x => x.id)
                        .ToArray()));
            });

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

    private sealed class ThrowingConnectionFactory : IMySqlConnectionFactory
    {
        public MySqlConnection CreateConnection() => throw new InvalidOperationException("测试不允许连接数据库");

        public ValueTask<MySqlConnection> OpenConnectionAsync(CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("测试不允许连接数据库");
    }
}
