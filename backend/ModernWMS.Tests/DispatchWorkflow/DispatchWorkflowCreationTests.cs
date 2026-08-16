using Microsoft.EntityFrameworkCore;
using ModernWMS.Core.DBContext;
using ModernWMS.Core.DBContext.Entities;
using ModernWMS.Core.JWT;
using ModernWMS.WMS.Entities.Models;
using ModernWMS.WMS.Entities.ViewModels.DispatchWorkflow;
using ModernWMS.WMS.Entities.ViewModels.PackingTask;
using ModernWMS.WMS.IServices;
using ModernWMS.WMS.IServices.PackingTask;
using ModernWMS.WMS.Services.DispatchWorkflow;
using System.Reflection;

namespace ModernWMS.Tests.DispatchWorkflow;

public class DispatchWorkflowCreationTests
{
    [Fact]
    public async Task CreateAsync_fails_atomically_when_shared_commodity_mapping_is_missing()
    {
        await using var db = TestContext.CreateDatabase();
        var source = new MutableSourceReader(
            TestContext.Task(101, "CW-101", 320118, TestContext.Item(1001, "SKU", 2)));
        var service = TestContext.CreateService(db, source);
        db.Remove(await db.GetDbSet<ErpCommodityMapEntity>().SingleAsync());
        await db.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<DispatchWorkflowCommandException>(() => service.CreateAsync(
            new CreateDispatchOrderRequest { warehouse_id = 320118, source_task_ids = [101] },
            TestContext.User()));

        Assert.Equal("SKU_MAPPING_MISSING", exception.ErrorCode);
        Assert.Empty(await db.GetDbSet<DispatchOrderEntity>().ToListAsync());
        Assert.Empty(await db.GetDbSet<DispatchPackingTaskEntity>().ToListAsync());
        Assert.Empty(await db.GetDbSet<DispatchPackingTaskItemEntity>().ToListAsync());
    }

    [Fact]
    public async Task CreateAsync_fails_atomically_when_shared_commodity_mapping_points_to_different_skus()
    {
        await using var db = TestContext.CreateDatabase();
        var item = TestContext.Item(1001, "SKU", 2);
        var source = new MutableSourceReader(TestContext.Task(101, "CW-101", 320118, item));
        var service = TestContext.CreateService(db, source);
        db.GetDbSet<ErpCommodityMapEntity>().Add(new ErpCommodityMapEntity
        {
            erp_commodity_id = item.CommodityId!.Value,
            wms_spu_id = 2,
            wms_sku_id = 99,
            commodity_sku = item.CommoditySku,
            tenant_id = 999
        });
        await db.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<DispatchWorkflowCommandException>(() => service.CreateAsync(
            new CreateDispatchOrderRequest { warehouse_id = 320118, source_task_ids = [101] },
            TestContext.User()));

        Assert.Equal("SKU_MAPPING_CONFLICT", exception.ErrorCode);
        Assert.Empty(await db.GetDbSet<DispatchOrderEntity>().ToListAsync());
        Assert.Empty(await db.GetDbSet<DispatchPackingTaskEntity>().ToListAsync());
        Assert.Empty(await db.GetDbSet<DispatchPackingTaskItemEntity>().ToListAsync());
    }

    [Fact]
    public async Task CreateAsync_uses_a_shared_mapping_even_when_only_tenant_one_owns_the_row()
    {
        await using var db = TestContext.CreateDatabase();
        var item = TestContext.Item(1001, "SKU", 2);
        var source = new MutableSourceReader(TestContext.Task(101, "CW-101", 320118, item));
        var service = TestContext.CreateService(db, source);
        var mapping = await db.GetDbSet<ErpCommodityMapEntity>().SingleAsync();
        mapping.tenant_id = 1;
        mapping.wms_sku_id = 321;
        await db.SaveChangesAsync();

        var created = await service.CreateAsync(
            new CreateDispatchOrderRequest { warehouse_id = 320118, source_task_ids = [101] },
            TestContext.User());

        Assert.Equal(321, Assert.Single(Assert.Single(created.packing_tasks).items).wms_sku_id);
    }

    [Fact]
    public async Task CreateAsync_accepts_duplicate_tenant_rows_when_they_resolve_to_the_same_wms_sku()
    {
        await using var db = TestContext.CreateDatabase();
        var item = TestContext.Item(1001, "SKU", 2);
        var source = new MutableSourceReader(TestContext.Task(101, "CW-101", 320118, item));
        var service = TestContext.CreateService(db, source);
        (await db.GetDbSet<ErpCommodityMapEntity>().SingleAsync()).wms_sku_id = 321;
        db.GetDbSet<ErpCommodityMapEntity>().Add(new ErpCommodityMapEntity
        {
            erp_commodity_id = item.CommodityId!.Value,
            wms_spu_id = 9,
            wms_sku_id = 321,
            commodity_sku = item.CommoditySku,
            tenant_id = 999
        });
        await db.SaveChangesAsync();

        var created = await service.CreateAsync(
            new CreateDispatchOrderRequest { warehouse_id = 320118, source_task_ids = [101] },
            TestContext.User());

        Assert.Equal(321, Assert.Single(Assert.Single(created.packing_tasks).items).wms_sku_id);
    }

    [Fact]
    public async Task CreateAsync_persists_the_current_tenant_commodity_mapping_on_each_source_item()
    {
        await using var db = TestContext.CreateDatabase();
        var source = new MutableSourceReader(
            TestContext.Task(101, "CW-101", 320118, TestContext.Item(1001, "SKU-1", 2)));
        var service = TestContext.CreateService(db, source);
        var commodityId = TestContext.Item(1001, "SKU-1", 2).CommodityId!.Value;
        var map = await db.GetDbSet<ErpCommodityMapEntity>()
            .SingleAsync(t => t.tenant_id == TestContext.User().tenant_id && t.erp_commodity_id == commodityId);
        map.wms_sku_id = 321;
        await db.SaveChangesAsync();

        var created = await service.CreateAsync(
            new CreateDispatchOrderRequest { warehouse_id = 320118, source_task_ids = [101] },
            TestContext.User());

        Assert.Equal(321, Assert.Single(Assert.Single(created.packing_tasks).items).wms_sku_id);
    }

    [Fact]
    public async Task CreateAsync_marks_an_empty_carton_snapshot_as_not_yet_identity_verified()
    {
        await using var db = TestContext.CreateDatabase();
        var source = new MutableSourceReader(
            TestContext.Task(101, "CW-101", 320118, TestContext.Item(1001, "SKU-1", 2)));
        var service = TestContext.CreateService(db, source);

        await service.CreateAsync(
            new CreateDispatchOrderRequest { warehouse_id = 320118, source_task_ids = [101] },
            TestContext.User());

        var task = await db.GetDbSet<DispatchPackingTaskEntity>().SingleAsync();
        Assert.Equal(0, task.expected_box_count);
        Assert.False(task.stable_box_identity_verified);
        Assert.Contains("称重", task.box_identity_validation_error);
    }

    [Fact]
    public async Task CreateAsync_rejects_cross_warehouse_task_set_without_writing_any_order()
    {
        await using var db = TestContext.CreateDatabase();
        var source = new MutableSourceReader(
            TestContext.Task(101, "CW-101", 320118, TestContext.Item(1001, "SAME-SKU", 2)),
            TestContext.Task(102, "CW-102", 9, TestContext.Item(1002, "SAME-SKU", 3)));
        var service = TestContext.CreateService(db, source);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateAsync(
            new CreateDispatchOrderRequest { warehouse_id = 320118, source_task_ids = [101, 102] },
            TestContext.User()));

        Assert.Empty(await db.GetDbSet<DispatchOrderEntity>().ToListAsync());
        Assert.Empty(await db.GetDbSet<DispatchPackingTaskEntity>().ToListAsync());
    }

    [Fact]
    public async Task CreateAsync_is_idempotent_for_sorted_distinct_task_set_and_keeps_equal_skus_separate()
    {
        await using var db = TestContext.CreateDatabase();
        var source = new MutableSourceReader(
            TestContext.Task(101, "CW-101", 320118, TestContext.Item(1001, "SAME-SKU", 2)),
            TestContext.Task(102, "CW-102", 320118, TestContext.Item(1002, "SAME-SKU", 3)));
        var service = TestContext.CreateService(db, source);

        var first = await service.CreateAsync(
            new CreateDispatchOrderRequest { warehouse_id = 320118, source_task_ids = [102, 101, 102] },
            TestContext.User());
        var retry = await service.CreateAsync(
            new CreateDispatchOrderRequest { warehouse_id = 320118, source_task_ids = [101, 102] },
            TestContext.User());

        Assert.Equal(first.id, retry.id);
        Assert.Single(await db.GetDbSet<DispatchOrderEntity>().ToListAsync());
        var tasks = await db.GetDbSet<DispatchPackingTaskEntity>().OrderBy(t => t.source_task_id).ToListAsync();
        Assert.Equal([101L, 102L], tasks.Select(t => t.source_task_id).ToArray());
        var items = await db.GetDbSet<DispatchPackingTaskItemEntity>().OrderBy(t => t.source_item_id).ToListAsync();
        Assert.Equal(2, items.Count);
        Assert.Equal("SAME-SKU", items[0].commodity_sku);
        Assert.Equal("SAME-SKU", items[1].commodity_sku);
        Assert.NotEqual(items[0].packing_task_id, items[1].packing_task_id);
        Assert.Empty(await db.GetDbSet<DispatchpicklistEntity>().ToListAsync());
    }

    [Fact]
    public async Task CreateAsync_rejects_task_already_in_another_active_order_as_one_atomic_failure()
    {
        await using var db = TestContext.CreateDatabase();
        var source = new MutableSourceReader(
            TestContext.Task(101, "CW-101", 320118, TestContext.Item(1001, "SKU-1", 1)),
            TestContext.Task(102, "CW-102", 320118, TestContext.Item(1002, "SKU-2", 1)));
        var service = TestContext.CreateService(db, source);
        await service.CreateAsync(
            new CreateDispatchOrderRequest { warehouse_id = 320118, source_task_ids = [101] },
            TestContext.User());

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateAsync(
            new CreateDispatchOrderRequest { warehouse_id = 320118, source_task_ids = [101, 102] },
            TestContext.User()));

        Assert.Single(await db.GetDbSet<DispatchOrderEntity>().ToListAsync());
        Assert.Single(await db.GetDbSet<DispatchPackingTaskEntity>().ToListAsync());
    }

    [Fact]
    public async Task CreateAsync_rejects_client_idempotency_key_that_does_not_match_server_task_set()
    {
        await using var db = TestContext.CreateDatabase();
        var source = new MutableSourceReader(TestContext.Task(101, "CW-101", 320118, TestContext.Item(1001, "SKU", 1)));
        var service = TestContext.CreateService(db, source);

        await Assert.ThrowsAsync<ArgumentException>(() => service.CreateAsync(new CreateDispatchOrderRequest
        {
            warehouse_id = 320118,
            source_task_ids = [101],
            idempotency_key = "client-invented-key"
        }, TestContext.User()));

        Assert.Empty(await db.GetDbSet<DispatchOrderEntity>().ToListAsync());
    }

    [Fact]
    public async Task CreateAsync_rolls_back_when_source_changes_during_commit_double_read()
    {
        await using var db = TestContext.CreateDatabase();
        var source = new MutableSourceReader(TestContext.Task(101, "CW-101", 320118, TestContext.Item(1001, "FIRST", 1)));
        source.BeforeRead = readCount =>
        {
            if (readCount == 2)
            {
                source.Set(TestContext.Task(101, "CW-101", 320118, TestContext.Item(1002, "LATEST", 2)));
            }
        };
        var service = TestContext.CreateService(db, source);

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => service.CreateAsync(
            new CreateDispatchOrderRequest { warehouse_id = 320118, source_task_ids = [101] },
            TestContext.User()));

        db.ChangeTracker.Clear();
        Assert.Empty(await db.GetDbSet<DispatchOrderEntity>().ToListAsync());
        Assert.Empty(await db.GetDbSet<DispatchPackingTaskEntity>().ToListAsync());
    }

    [Fact]
    public async Task PageAsync_returns_one_row_per_order_with_task_numbers_and_checks_warehouse_access()
    {
        await using var db = TestContext.CreateDatabase();
        var source = new MutableSourceReader(
            TestContext.Task(101, "CW-101", 320118, TestContext.Item(1001, "SKU-1", 1)),
            TestContext.Task(102, "CW-102", 320118, TestContext.Item(1002, "SKU-2", 1)));
        var access = new RecordingWarehouseAccess();
        var workflow = TestContext.CreateService(db, source, access);
        await workflow.CreateAsync(
            new CreateDispatchOrderRequest { warehouse_id = 320118, source_task_ids = [101, 102] },
            TestContext.User());
        source.Set(TestContext.Task(101, "CW-101-UPDATED", 320118, TestContext.Item(1001, "SKU-1", 2)));
        var query = new DispatchOrderQueryService(db, access.Contract, workflow);

        var page = await query.PageAsync(new DispatchOrderPageRequest
        {
            warehouse_id = 320118,
            status = "PENDING_PICK",
            keyword = "UPDATED"
        }, TestContext.User());

        var row = Assert.Single(page.Data);
        Assert.Equal(["CW-101-UPDATED", "CW-102"], row.packing_task_nos);
        Assert.Equal("PENDING_PICK", row.status);
        Assert.Contains(320118, access.CheckedWarehouseIds);

        var counts = await query.CountsAsync(320118, TestContext.User());
        Assert.Equal(1, counts.Counts["PENDING_PICK"]);
        Assert.Equal(0, counts.Counts["OUTBOUND"]);
    }
}

internal static class TestContext
{
    public static SqlDBContext CreateDatabase() => new(
        new DbContextOptionsBuilder<SqlDBContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    public static DispatchWorkflowService CreateService(
        SqlDBContext db,
        MutableSourceReader source,
        RecordingWarehouseAccess? access = null)
    {
        source.AttachMappingDatabase(db);
        return new(db, source.Contract, (access ?? new RecordingWarehouseAccess()).Contract);
    }

    public static CurrentUser User() => new()
    {
        user_id = 7,
        user_name = "picker",
        user_role = "admin",
        tenant_id = 88
    };

    public static void SeedCommodityMaps(
        SqlDBContext database,
        params PackingTaskSourceSnapshot[] snapshots)
    {
        var tenantId = User().tenant_id;
        var existing = database.GetDbSet<ErpCommodityMapEntity>()
            .Where(t => t.tenant_id == tenantId)
            .Select(t => t.erp_commodity_id)
            .ToHashSet();
        foreach (var item in snapshots.SelectMany(t => t.Items)
                     .Where(t => t.CommodityId.HasValue)
                     .GroupBy(t => t.CommodityId!.Value)
                     .Select(t => t.First()))
        {
            if (!existing.Add(item.CommodityId!.Value))
            {
                continue;
            }

            database.GetDbSet<ErpCommodityMapEntity>().Add(new ErpCommodityMapEntity
            {
                erp_commodity_id = item.CommodityId.Value,
                wms_spu_id = 1,
                wms_sku_id = 10,
                commodity_sku = item.CommoditySku,
                tenant_id = tenantId
            });
        }

        database.SaveChanges();
    }

    public static PackingTaskSourceItem Item(long id, string sku, int quantity) =>
        new(id, id + 10000, sku, $"商品-{id}", $"img-{id}", $"FN-{id}", sku, $"M-{id}", quantity, $"item-v-{id}-{quantity}");

    public static PackingTaskSourceSnapshot Task(
        long id,
        string taskNo,
        long warehouseId,
        params PackingTaskSourceItem[] items) =>
        new(id, taskNo, warehouseId, $"仓库-{warehouseId}", $"task-v-{id}-{items.Sum(t => t.Quantity)}", false, items, [], "[]");
}

internal sealed class MutableSourceReader
{
    private readonly Dictionary<long, PackingTaskSourceSnapshot> _snapshots;
    private int _readCount;
    public IPackingTaskSourceReader Contract { get; }
    public Action<int>? BeforeRead { get; set; }
    public int ReadCount => Volatile.Read(ref _readCount);

    public MutableSourceReader(params PackingTaskSourceSnapshot[] snapshots)
    {
        _snapshots = snapshots.ToDictionary(t => t.SourceTaskId);
        Contract = DispatchProxy.Create<IPackingTaskSourceReader, SourceReaderProxy>();
        ((SourceReaderProxy)(object)Contract).Owner = this;
    }

    public void Set(PackingTaskSourceSnapshot snapshot) => _snapshots[snapshot.SourceTaskId] = snapshot;

    public void AttachMappingDatabase(SqlDBContext database) =>
        TestContext.SeedCommodityMaps(database, _snapshots.Values.ToArray());

    internal Task<PackingTaskSourceCapability> VerifyCapabilityAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(new PackingTaskSourceCapability(true, string.Empty));

    internal Task<IReadOnlyList<PackingTaskSourceSnapshot>> ReadAsync(
        IReadOnlyCollection<long> sourceTaskIds,
        CancellationToken cancellationToken = default)
    {
        BeforeRead?.Invoke(Interlocked.Increment(ref _readCount));
        return Task.FromResult<IReadOnlyList<PackingTaskSourceSnapshot>>(sourceTaskIds
            .Where(_snapshots.ContainsKey)
            .Select(id => _snapshots[id])
            .ToList());
    }
}

public class SourceReaderProxy : DispatchProxy
{
    internal MutableSourceReader Owner { get; set; } = null!;

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => targetMethod?.Name switch
    {
        nameof(IPackingTaskSourceReader.VerifyCapabilityAsync) =>
            Owner.VerifyCapabilityAsync((CancellationToken)(args?[0] ?? default(CancellationToken))),
        nameof(IPackingTaskSourceReader.ReadAsync) =>
            Owner.ReadAsync((IReadOnlyCollection<long>)args![0]!, (CancellationToken)(args[1] ?? default(CancellationToken))),
        _ => throw new NotSupportedException(targetMethod?.Name)
    };
}

internal sealed class RecordingWarehouseAccess
{
    public List<long> CheckedWarehouseIds { get; } = [];
    public long? DefaultWarehouseId { get; set; } = 320118;
    public IWarehouseAccessService Contract { get; }

    public RecordingWarehouseAccess()
    {
        Contract = DispatchProxy.Create<IWarehouseAccessService, WarehouseAccessProxy>();
        ((WarehouseAccessProxy)(object)Contract).Owner = this;
    }

    internal Task<ModernWMS.WMS.Entities.ViewModels.WarehouseAccessViewModel> GetAllowedAsync(CurrentUser currentUser) =>
        Task.FromResult(new ModernWMS.WMS.Entities.ViewModels.WarehouseAccessViewModel
        {
            default_warehouse_id = DefaultWarehouseId,
            warehouses = DefaultWarehouseId is long id
                ? [new ModernWMS.WMS.Entities.ViewModels.ErpWarehouseOptionViewModel { id = id, name = $"仓库-{id}" }]
                : []
        });

    internal Task EnsureAllowedAsync(long warehouseId, CurrentUser currentUser)
    {
        CheckedWarehouseIds.Add(warehouseId);
        return Task.CompletedTask;
    }
}

public class WarehouseAccessProxy : DispatchProxy
{
    internal RecordingWarehouseAccess Owner { get; set; } = null!;

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => targetMethod?.Name switch
    {
        nameof(IWarehouseAccessService.GetAllowedAsync) => Owner.GetAllowedAsync((CurrentUser)args![0]!),
        nameof(IWarehouseAccessService.EnsureAllowedAsync) => Owner.EnsureAllowedAsync((long)args![0]!, (CurrentUser)args[1]!),
        _ => throw new NotSupportedException(targetMethod?.Name)
    };
}
