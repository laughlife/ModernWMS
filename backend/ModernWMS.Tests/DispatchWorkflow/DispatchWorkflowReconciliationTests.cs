using Microsoft.EntityFrameworkCore;
using ModernWMS.Core.DBContext.Entities;
using ModernWMS.WMS.Entities.Models;
using ModernWMS.WMS.Entities.ViewModels.DispatchWorkflow;
using ModernWMS.WMS.Services.DispatchWorkflow;

namespace ModernWMS.Tests.DispatchWorkflow;

public class DispatchWorkflowReconciliationTests
{
    [Fact]
    public async Task ReconcileAsync_keeps_items_unbound_without_merging_tasks()
    {
        await using var db = TestContext.CreateDatabase();
        var source = new MutableSourceReader(
            TestContext.Task(101, "CW-101", 320118, TestContext.Item(1001, "SAME", 2)),
            TestContext.Task(102, "CW-102", 320118, TestContext.Item(1002, "SAME", 3)));
        var service = TestContext.CreateService(db, source);
        var order = await service.CreateAsync(
            new CreateDispatchOrderRequest { warehouse_id = 320118, source_task_ids = [101, 102] },
            TestContext.User());
        var maps = await db.GetDbSet<ErpCommodityMapEntity>().ToListAsync();
        maps.ForEach(t => t.wms_sku_id = 55);
        await db.SaveChangesAsync();

        var reconciled = await service.ReconcileAsync(order.id, TestContext.User());

        Assert.Equal(2, reconciled.packing_tasks.Count);
        Assert.All(reconciled.packing_tasks, task => Assert.Null(Assert.Single(task.items).wms_sku_id));
        Assert.Equal(2, await db.GetDbSet<DispatchPackingTaskItemEntity>().CountAsync(t => t.is_active));
    }

    [Fact]
    public async Task ReconcileAsync_clears_a_legacy_prebound_sku_while_order_is_pending_pick()
    {
        await using var db = TestContext.CreateDatabase();
        var source = new MutableSourceReader(
            TestContext.Task(101, "CW-101", 320118, TestContext.Item(1001, "SKU", 2)));
        var service = TestContext.CreateService(db, source);
        var order = await service.CreateAsync(
            new CreateDispatchOrderRequest { warehouse_id = 320118, source_task_ids = [101] },
            TestContext.User());
        var item = await db.GetDbSet<DispatchPackingTaskItemEntity>().SingleAsync();
        item.wms_sku_id = 10;
        await db.SaveChangesAsync();

        var reconciled = await service.ReconcileAsync(order.id, TestContext.User());

        Assert.Null(Assert.Single(Assert.Single(reconciled.packing_tasks).items).wms_sku_id);
    }

    [Fact]
    public async Task ReconcileAsync_does_not_require_a_shared_commodity_mapping()
    {
        await using var db = TestContext.CreateDatabase();
        var source = new MutableSourceReader(
            TestContext.Task(101, "CW-101", 320118, TestContext.Item(1001, "SKU", 2)));
        var service = TestContext.CreateService(db, source);
        var order = await service.CreateAsync(
            new CreateDispatchOrderRequest { warehouse_id = 320118, source_task_ids = [101] },
            TestContext.User());
        var itemBefore = await db.GetDbSet<DispatchPackingTaskItemEntity>().SingleAsync();
        db.GetDbSet<DispatchpicklistEntity>().Add(new DispatchpicklistEntity
        {
            packing_task_item_id = itemBefore.id,
            pick_qty = 1,
            picked_qty = 0
        });
        await db.SaveChangesAsync();
        db.Remove(await db.GetDbSet<ErpCommodityMapEntity>().SingleAsync());
        await db.SaveChangesAsync();

        var reconciled = await service.ReconcileAsync(order.id, TestContext.User());

        Assert.Equal("PENDING_PICK", reconciled.status);
        db.ChangeTracker.Clear();
        var itemAfter = await db.GetDbSet<DispatchPackingTaskItemEntity>().SingleAsync();
        Assert.Null(itemAfter.wms_sku_id);
        Assert.Single(await db.GetDbSet<DispatchpicklistEntity>().ToListAsync());
    }

    [Fact]
    public async Task ReconcileAsync_does_not_resolve_conflicting_shared_mappings()
    {
        await using var db = TestContext.CreateDatabase();
        var item = TestContext.Item(1001, "SKU", 2);
        var source = new MutableSourceReader(TestContext.Task(101, "CW-101", 320118, item));
        var service = TestContext.CreateService(db, source);
        var order = await service.CreateAsync(
            new CreateDispatchOrderRequest { warehouse_id = 320118, source_task_ids = [101] },
            TestContext.User());
        var itemBefore = await db.GetDbSet<DispatchPackingTaskItemEntity>().SingleAsync();
        db.GetDbSet<DispatchpicklistEntity>().Add(new DispatchpicklistEntity
        {
            packing_task_item_id = itemBefore.id,
            pick_qty = 1,
            picked_qty = 0
        });
        db.GetDbSet<ErpCommodityMapEntity>().Add(new ErpCommodityMapEntity
        {
            erp_commodity_id = item.CommodityId!.Value,
            wms_spu_id = 2,
            wms_sku_id = 99,
            commodity_sku = item.CommoditySku,
            tenant_id = 999
        });
        await db.SaveChangesAsync();

        var reconciled = await service.ReconcileAsync(order.id, TestContext.User());

        Assert.Equal("PENDING_PICK", reconciled.status);
        db.ChangeTracker.Clear();
        var itemAfter = await db.GetDbSet<DispatchPackingTaskItemEntity>().SingleAsync();
        Assert.Null(itemAfter.wms_sku_id);
        Assert.Single(await db.GetDbSet<DispatchpicklistEntity>().ToListAsync());
    }

    [Fact]
    public async Task ReconcileAsync_rebuilds_only_changed_task_items_and_preserves_other_task_boundary()
    {
        await using var db = TestContext.CreateDatabase();
        var source = new MutableSourceReader(
            TestContext.Task(101, "CW-101", 320118, TestContext.Item(1001, "OLD", 2)),
            TestContext.Task(102, "CW-102", 320118, TestContext.Item(2001, "UNCHANGED", 4)));
        var service = TestContext.CreateService(db, source);
        var order = await service.CreateAsync(
            new CreateDispatchOrderRequest { warehouse_id = 320118, source_task_ids = [101, 102] },
            TestContext.User());
        var changed = TestContext.Task(101, "CW-101", 320118, TestContext.Item(1003, "NEW", 7));
        TestContext.SeedCommodityMaps(db, changed);
        source.Set(changed);

        var reconciled = await service.ReconcileAsync(order.id, TestContext.User());

        Assert.Equal("PENDING_PICK", reconciled.status);
        Assert.Equal(2, reconciled.packing_tasks.Count);
        Assert.Equal("NEW", Assert.Single(reconciled.packing_tasks.Single(t => t.source_task_id == 101).items).commodity_sku);
        Assert.Equal("UNCHANGED", Assert.Single(reconciled.packing_tasks.Single(t => t.source_task_id == 102).items).commodity_sku);
        Assert.False((await db.GetDbSet<DispatchPackingTaskItemEntity>().SingleAsync(t => t.source_item_id == 1001)).is_active);
    }

    [Fact]
    public async Task ReconcileAsync_removes_cancelled_task_but_keeps_remaining_task_active()
    {
        await using var db = TestContext.CreateDatabase();
        var source = new MutableSourceReader(
            TestContext.Task(101, "CW-101", 320118, TestContext.Item(1001, "SKU-1", 2)),
            TestContext.Task(102, "CW-102", 320118, TestContext.Item(2001, "SKU-2", 4)));
        var service = TestContext.CreateService(db, source);
        var order = await service.CreateAsync(
            new CreateDispatchOrderRequest { warehouse_id = 320118, source_task_ids = [101, 102] },
            TestContext.User());
        source.Set(TestContext.Task(101, "CW-101", 320118, TestContext.Item(1001, "SKU-1", 2)) with { IsCancelled = true });

        var reconciled = await service.ReconcileAsync(order.id, TestContext.User());

        Assert.Equal([102L], reconciled.packing_tasks.Select(t => t.source_task_id).ToArray());
        var cancelled = await db.GetDbSet<DispatchPackingTaskEntity>().SingleAsync(t => t.source_task_id == 101);
        Assert.False(cancelled.is_active);
        Assert.Null(cancelled.active_source_task_id);
        Assert.Equal("PENDING_PICK", reconciled.status);
    }

    [Fact]
    public async Task ReconcileAsync_marks_order_source_cancelled_when_all_tasks_cancelled()
    {
        await using var db = TestContext.CreateDatabase();
        var original = TestContext.Task(101, "CW-101", 320118, TestContext.Item(1001, "SKU-1", 2));
        var source = new MutableSourceReader(original);
        var service = TestContext.CreateService(db, source);
        var order = await service.CreateAsync(
            new CreateDispatchOrderRequest { warehouse_id = 320118, source_task_ids = [101] },
            TestContext.User());
        source.Set(original with { IsCancelled = true, SourceVersion = "cancelled-v2" });

        var reconciled = await service.ReconcileAsync(order.id, TestContext.User());

        Assert.Equal("SOURCE_CANCELLED", reconciled.status);
        Assert.Empty(reconciled.packing_tasks);
        Assert.Equal(DispatchOrderStatus.SourceCancelled,
            (await db.GetDbSet<DispatchOrderEntity>().SingleAsync()).status);
    }

    [Fact]
    public async Task ReconcileAsync_rejects_source_task_that_moves_to_another_warehouse()
    {
        await using var db = TestContext.CreateDatabase();
        var source = new MutableSourceReader(TestContext.Task(101, "CW-101", 320118, TestContext.Item(1001, "SKU", 2)));
        var service = TestContext.CreateService(db, source);
        var order = await service.CreateAsync(
            new CreateDispatchOrderRequest { warehouse_id = 320118, source_task_ids = [101] },
            TestContext.User());
        source.Set(TestContext.Task(101, "CW-101", 9, TestContext.Item(1001, "SKU", 2)));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ReconcileAsync(order.id, TestContext.User()));

        Assert.Equal(DispatchOrderStatus.PendingPick,
            (await db.GetDbSet<DispatchOrderEntity>().SingleAsync()).status);
    }

    [Fact]
    public async Task ReconcileAsync_rolls_back_when_source_changes_during_double_read()
    {
        await using var db = TestContext.CreateDatabase();
        var original = TestContext.Task(101, "CW-101", 320118, TestContext.Item(1001, "OLD", 1));
        var source = new MutableSourceReader(original);
        var service = TestContext.CreateService(db, source);
        var order = await service.CreateAsync(
            new CreateDispatchOrderRequest { warehouse_id = 320118, source_task_ids = [101] },
            TestContext.User());
        var middle = TestContext.Task(101, "CW-101", 320118, TestContext.Item(1002, "MIDDLE", 2));
        TestContext.SeedCommodityMaps(db, middle);
        source.Set(middle);
        var commitRead = source.ReadCount + 2;
        source.BeforeRead = readCount =>
        {
            if (readCount == commitRead)
            {
                source.Set(TestContext.Task(101, "CW-101", 320118, TestContext.Item(1003, "LATEST", 3)));
            }
        };

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() =>
            service.ReconcileAsync(order.id, TestContext.User()));

        db.ChangeTracker.Clear();
        var active = await db.GetDbSet<DispatchPackingTaskItemEntity>().Where(t => t.is_active).ToListAsync();
        Assert.Equal(1001, Assert.Single(active).source_item_id);
    }

    [Fact]
    public async Task ReconcileAsync_removes_all_old_allocations_before_rebuilding_changed_task()
    {
        await using var db = TestContext.CreateDatabase();
        var source = new MutableSourceReader(TestContext.Task(101, "CW-101", 320118, TestContext.Item(1001, "OLD", 1)));
        var service = TestContext.CreateService(db, source);
        var order = await service.CreateAsync(
            new CreateDispatchOrderRequest { warehouse_id = 320118, source_task_ids = [101] },
            TestContext.User());
        var oldItemId = (await db.GetDbSet<DispatchPackingTaskItemEntity>().SingleAsync()).id;
        await db.GetDbSet<DispatchpicklistEntity>().AddAsync(new DispatchpicklistEntity
        {
            packing_task_item_id = oldItemId,
            pick_qty = 1,
            picked_qty = 1
        });
        await db.SaveChangesAsync();
        var changed = TestContext.Task(101, "CW-101", 320118, TestContext.Item(1002, "NEW", 4));
        TestContext.SeedCommodityMaps(db, changed);
        source.Set(changed);

        await service.ReconcileAsync(order.id, TestContext.User());

        Assert.Empty(await db.GetDbSet<DispatchpicklistEntity>().ToListAsync());
        Assert.Equal(4, (await db.GetDbSet<DispatchPackingTaskItemEntity>().SingleAsync(t => t.is_active)).required_qty);
    }

    [Fact]
    public async Task ReconcileAsync_rejects_changed_task_when_an_allocation_already_updated_stock()
    {
        await using var db = TestContext.CreateDatabase();
        var source = new MutableSourceReader(TestContext.Task(101, "CW-101", 320118, TestContext.Item(1001, "OLD", 1)));
        var service = TestContext.CreateService(db, source);
        var order = await service.CreateAsync(
            new CreateDispatchOrderRequest { warehouse_id = 320118, source_task_ids = [101] },
            TestContext.User());
        var oldItemId = (await db.GetDbSet<DispatchPackingTaskItemEntity>().SingleAsync()).id;
        await db.GetDbSet<DispatchpicklistEntity>().AddAsync(new DispatchpicklistEntity
        {
            packing_task_item_id = oldItemId,
            pick_qty = 1,
            picked_qty = 1,
            is_update_stock = true
        });
        await db.SaveChangesAsync();
        var changed = TestContext.Task(101, "CW-101", 320118, TestContext.Item(1002, "NEW", 4));
        TestContext.SeedCommodityMaps(db, changed);
        source.Set(changed);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ReconcileAsync(order.id, TestContext.User()));

        db.ChangeTracker.Clear();
        Assert.True((await db.GetDbSet<DispatchpicklistEntity>().SingleAsync()).is_update_stock);
        Assert.Equal(1001, (await db.GetDbSet<DispatchPackingTaskItemEntity>().SingleAsync(t => t.is_active)).source_item_id);
    }
}
