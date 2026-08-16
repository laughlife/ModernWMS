using Microsoft.EntityFrameworkCore;
using ModernWMS.WMS.Entities.Models;
using ModernWMS.WMS.Entities.ViewModels.DispatchWorkflow;

namespace ModernWMS.Tests.DispatchWorkflow;

public class DispatchWorkflowPrintTests
{
    [Fact]
    public async Task PrintAsync_reconciles_first_and_returns_fully_expanded_task_owned_items()
    {
        await using var db = TestContext.CreateDatabase();
        var source = new MutableSourceReader(
            TestContext.Task(101, "CW-101", 320118, TestContext.Item(1001, "OLD", 1)),
            TestContext.Task(102, "CW-102", 320118, TestContext.Item(2001, "SECOND", 3)));
        var service = TestContext.CreateService(db, source);
        var order = await service.CreateAsync(
            new CreateDispatchOrderRequest { warehouse_id = 320118, source_task_ids = [101, 102] },
            TestContext.User());
        var latest = TestContext.Task(101, "CW-101", 320118, TestContext.Item(1002, "LATEST", 5));
        TestContext.SeedCommodityMaps(db, latest);
        source.Set(latest);

        var print = await service.PrintAsync(order.id, TestContext.User());

        Assert.Equal(["CW-101", "CW-102"], print.packing_tasks.Select(t => t.source_task_no).ToArray());
        Assert.Equal("LATEST", Assert.Single(print.packing_tasks[0].items).commodity_sku);
        Assert.Equal("SECOND", Assert.Single(print.packing_tasks[1].items).commodity_sku);
    }

    [Fact]
    public async Task PrintAsync_does_not_advance_pending_pick_status()
    {
        await using var db = TestContext.CreateDatabase();
        var source = new MutableSourceReader(TestContext.Task(101, "CW-101", 320118, TestContext.Item(1001, "SKU", 1)));
        var service = TestContext.CreateService(db, source);
        var order = await service.CreateAsync(
            new CreateDispatchOrderRequest { warehouse_id = 320118, source_task_ids = [101] },
            TestContext.User());

        var print = await service.PrintAsync(order.id, TestContext.User());

        Assert.Equal("PENDING_PICK", print.status);
        Assert.Equal(DispatchOrderStatus.PendingPick,
            (await db.GetDbSet<DispatchOrderEntity>().SingleAsync()).status);
        Assert.Empty(await db.GetDbSet<DispatchpicklistEntity>().ToListAsync());
    }

    [Fact]
    public async Task PrintAsync_exposes_the_source_product_image_for_the_picking_sheet()
    {
        await using var db = TestContext.CreateDatabase();
        var item = TestContext.Item(1001, "SKU", 1) with
        {
            SourceSnapshot = "{\"mainImage\":\"https://img.example.com/sku.jpg\"}"
        };
        var source = new MutableSourceReader(TestContext.Task(101, "CW-101", 320118, item));
        var service = TestContext.CreateService(db, source);
        var order = await service.CreateAsync(
            new CreateDispatchOrderRequest { warehouse_id = 320118, source_task_ids = [101] },
            TestContext.User());

        var print = await service.PrintAsync(order.id, TestContext.User());

        Assert.Equal("https://img.example.com/sku.jpg",
            Assert.Single(Assert.Single(print.packing_tasks).items).main_image);
    }

    [Fact]
    public async Task PrintAsync_rejects_orders_that_have_left_pending_pick()
    {
        await using var db = TestContext.CreateDatabase();
        var source = new MutableSourceReader(TestContext.Task(101, "CW-101", 320118, TestContext.Item(1001, "SKU", 1)));
        var service = TestContext.CreateService(db, source);
        var order = await service.CreateAsync(
            new CreateDispatchOrderRequest { warehouse_id = 320118, source_task_ids = [101] },
            TestContext.User());
        (await db.GetDbSet<DispatchOrderEntity>().SingleAsync()).status = DispatchOrderStatus.Picked;
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.PrintAsync(order.id, TestContext.User()));
    }

    [Fact]
    public async Task PrintAsync_rejects_when_reconciliation_cancels_the_last_source_task()
    {
        await using var db = TestContext.CreateDatabase();
        var original = TestContext.Task(101, "CW-101", 320118, TestContext.Item(1001, "SKU", 1));
        var source = new MutableSourceReader(original);
        var service = TestContext.CreateService(db, source);
        var order = await service.CreateAsync(
            new CreateDispatchOrderRequest { warehouse_id = 320118, source_task_ids = [101] },
            TestContext.User());
        source.Set(original with { IsCancelled = true, SourceVersion = "cancelled-v2" });

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.PrintAsync(order.id, TestContext.User()));

        Assert.Equal(DispatchOrderStatus.SourceCancelled,
            (await db.GetDbSet<DispatchOrderEntity>().SingleAsync()).status);
    }
}
