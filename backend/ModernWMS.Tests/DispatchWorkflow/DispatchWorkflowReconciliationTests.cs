using Microsoft.EntityFrameworkCore;
using ModernWMS.WMS.Entities.Models;
using ModernWMS.WMS.Entities.ViewModels.DispatchWorkflow;

namespace ModernWMS.Tests.DispatchWorkflow;

public class DispatchWorkflowReconciliationTests
{
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
        source.Set(TestContext.Task(101, "CW-101", 320118, TestContext.Item(1003, "NEW", 7)));

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
        source.Set(TestContext.Task(101, "CW-101", 320118, TestContext.Item(1002, "MIDDLE", 2)));
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
        source.Set(TestContext.Task(101, "CW-101", 320118, TestContext.Item(1002, "NEW", 4)));

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
        source.Set(TestContext.Task(101, "CW-101", 320118, TestContext.Item(1002, "NEW", 4)));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ReconcileAsync(order.id, TestContext.User()));

        db.ChangeTracker.Clear();
        Assert.True((await db.GetDbSet<DispatchpicklistEntity>().SingleAsync()).is_update_stock);
        Assert.Equal(1001, (await db.GetDbSet<DispatchPackingTaskItemEntity>().SingleAsync(t => t.is_active)).source_item_id);
    }
}
