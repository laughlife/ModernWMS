using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using ModernWMS.WMS.Controllers.DispatchWorkflow;
using ModernWMS.WMS.Entities.Models;
using ModernWMS.WMS.Entities.ViewModels.DispatchWorkflow;
using ModernWMS.WMS.Services.DispatchWorkflow;

namespace ModernWMS.Tests.DispatchWorkflow;

public sealed class DispatchWorkflowSourceAdjudicationTests
{
    [Fact]
    public async Task Guard_freezes_a_new_source_version_and_deduplicates_the_detected_event()
    {
        await using var db = TestContext.CreateDatabase();
        var original = TestContext.Task(101, "CW-101", 320118, TestContext.Item(1001, "SKU", 1));
        var source = new MutableSourceReader(original);
        var service = TestContext.CreateService(db, source);
        var order = await CreatePostPickOrderAsync(service, db, DispatchOrderStatus.Picked);
        source.Set(TestContext.Task(101, "CW-101", 320118, TestContext.Item(1001, "SKU", 2)));

        var first = await service.EnsurePostPickSourceCurrentAsync(order.id, TestContext.User());
        var second = await service.EnsurePostPickSourceCurrentAsync(order.id, TestContext.User());

        Assert.Equal("SOURCE_CHANGE_PENDING", first.error_code);
        Assert.Equal("SOURCE_CHANGE_PENDING", second.error_code);
        var stored = await db.GetDbSet<DispatchOrderEntity>().SingleAsync();
        Assert.True(stored.source_change_pending);
        Assert.Equal(first.source_version, stored.pending_source_version);
        Assert.Equal(1, stored.row_version);
        var detected = Assert.Single(await db.GetDbSet<DispatchSourceChangeEventEntity>().ToListAsync());
        Assert.Equal(DispatchSourceChangeDecision.Detected, detected.decision);
        Assert.NotEmpty(detected.diff_snapshot);
    }

    [Theory]
    [InlineData(DispatchOrderStatus.Picked)]
    [InlineData(DispatchOrderStatus.Weighing)]
    [InlineData(DispatchOrderStatus.PendingOutbound)]
    public async Task Guard_applies_to_every_mutable_post_pick_state(DispatchOrderStatus status)
    {
        await using var db = TestContext.CreateDatabase();
        var source = new MutableSourceReader(TestContext.Task(101, "CW-101", 320118, TestContext.Item(1001, "SKU", 1)));
        var service = TestContext.CreateService(db, source);
        var order = await CreatePostPickOrderAsync(service, db, status);
        source.Set(TestContext.Task(101, "CW-101", 320118, TestContext.Item(1002, "SKU-NEW", 2)));

        var result = await service.EnsurePostPickSourceCurrentAsync(order.id, TestContext.User());

        Assert.Equal("SOURCE_CHANGE_PENDING", result.error_code);
    }

    [Fact]
    public async Task Continue_accepts_the_pending_version_replays_idempotently_and_later_version_refreezes()
    {
        await using var db = TestContext.CreateDatabase();
        var source = new MutableSourceReader(TestContext.Task(101, "CW-101", 320118, TestContext.Item(1001, "SKU", 1)));
        var service = TestContext.CreateService(db, source);
        var order = await CreatePostPickOrderAsync(service, db, DispatchOrderStatus.Weighing);
        source.Set(TestContext.Task(101, "CW-101", 320118, TestContext.Item(1001, "SKU", 2)));
        Assert.True((await service.EnsurePostPickSourceCurrentAsync(order.id, TestContext.User())).source_change_pending);
        var frozen = await db.GetDbSet<DispatchOrderEntity>().AsNoTracking().SingleAsync();
        var pendingVersion = (await db.GetDbSet<DispatchSourceChangeEventEntity>().SingleAsync()).source_version;
        var request = Decision("CONTINUE", pendingVersion, "继续按仓库实物发货", "decision-1", frozen.row_version);

        var first = await service.DecideSourceChangeAsync(order.id, request, TestContext.User());
        var replay = await service.DecideSourceChangeAsync(order.id,
            Decision("CONTINUE", "different-version", "不同的重试载荷", "decision-1", 999), TestContext.User());
        await service.EnsurePostPickSourceCurrentAsync(order.id, TestContext.User());

        Assert.False(first.source_change_pending);
        Assert.Empty((await db.GetDbSet<DispatchOrderEntity>().AsNoTracking().SingleAsync()).pending_source_version);
        Assert.Equal(first.row_version, replay.row_version);
        Assert.Equal(first.source_version, replay.source_version);
        Assert.Equal(2, await db.GetDbSet<DispatchSourceChangeEventEntity>().CountAsync());
        source.Set(TestContext.Task(101, "CW-101", 320118, TestContext.Item(1001, "SKU", 3)));
        var guard = await service.EnsurePostPickSourceCurrentAsync(order.id, TestContext.User());
        Assert.Equal("SOURCE_CHANGE_PENDING", guard.error_code);
        Assert.Equal(3, await db.GetDbSet<DispatchSourceChangeEventEntity>().CountAsync());

        await service.DecideSourceChangeAsync(order.id,
            Decision("CONTINUE", guard.source_version, "接受后续版本", "decision-2", guard.row_version),
            TestContext.User());
        source.Set(TestContext.Task(101, "CW-101", 320118, TestContext.Item(1001, "SKU", 2)));
        var historicalAccepted = await service.EnsurePostPickSourceCurrentAsync(order.id, TestContext.User());
        Assert.False(historicalAccepted.source_change_pending);
        Assert.Empty(historicalAccepted.error_code);
    }

    [Fact]
    public async Task Guard_concurrent_first_detection_returns_the_committed_winner_freeze()
    {
        var databaseName = Guid.NewGuid().ToString();
        var databaseRoot = new InMemoryDatabaseRoot();
        var options = new DbContextOptionsBuilder<ModernWMS.Core.DBContext.SqlDBContext>()
            .UseInMemoryDatabase(databaseName, databaseRoot)
            .Options;
        var original = TestContext.Task(101, "CW-101", 320118, TestContext.Item(1001, "SKU", 1));
        var source = new MutableSourceReader(original);
        int orderId;
        await using (var setupDb = new ModernWMS.Core.DBContext.SqlDBContext(options))
        {
            var setupService = TestContext.CreateService(setupDb, source);
            orderId = (await CreatePostPickOrderAsync(
                setupService, setupDb, DispatchOrderStatus.Picked)).id;
        }
        source.Set(TestContext.Task(101, "CW-101", 320118, TestContext.Item(1001, "SKU", 2)));

        await using var conflictingDb = new SaveConflictSqlDbContext(options)
        {
            ConflictException = new DbUpdateConcurrencyException("simulated first-detection winner")
        };
        conflictingDb.BeforeConflict = async () =>
        {
            await using var winnerDb = new ModernWMS.Core.DBContext.SqlDBContext(options);
            var winner = TestContext.CreateService(winnerDb, source);
            var winnerResult = await winner.EnsurePostPickSourceCurrentAsync(orderId, TestContext.User());
            Assert.True(winnerResult.source_change_pending);
        };
        var service = TestContext.CreateService(conflictingDb, source);

        var result = await service.EnsurePostPickSourceCurrentAsync(orderId, TestContext.User());

        Assert.Equal("SOURCE_CHANGE_PENDING", result.error_code);
        await using var verifyDb = new ModernWMS.Core.DBContext.SqlDBContext(options);
        Assert.True((await verifyDb.GetDbSet<DispatchOrderEntity>().SingleAsync()).source_change_pending);
        Assert.Single(await verifyDb.GetDbSet<DispatchSourceChangeEventEntity>()
            .Where(t => t.decision == DispatchSourceChangeDecision.Detected).ToListAsync());
    }

    [Fact]
    public void Guard_joins_an_existing_relational_transaction_instead_of_starting_a_nested_one()
    {
        var method = typeof(DispatchWorkflowService).GetMethod(
            "ShouldOwnGuardTransaction", BindingFlags.Static | BindingFlags.NonPublic);

        Assert.NotNull(method);
        Assert.False((bool)method!.Invoke(null, [true, true])!);
        Assert.True((bool)method.Invoke(null, [true, false])!);
        Assert.False((bool)method.Invoke(null, [false, false])!);
    }

    [Fact]
    public async Task Guard_preserves_non_concurrency_database_errors()
    {
        var databaseName = Guid.NewGuid().ToString();
        var databaseRoot = new InMemoryDatabaseRoot();
        var options = new DbContextOptionsBuilder<ModernWMS.Core.DBContext.SqlDBContext>()
            .UseInMemoryDatabase(databaseName, databaseRoot)
            .Options;
        var source = new MutableSourceReader(
            TestContext.Task(101, "CW-101", 320118, TestContext.Item(1001, "SKU", 1)));
        int orderId;
        await using (var setupDb = new ModernWMS.Core.DBContext.SqlDBContext(options))
        {
            var setupService = TestContext.CreateService(setupDb, source);
            orderId = (await CreatePostPickOrderAsync(
                setupService, setupDb, DispatchOrderStatus.Picked)).id;
        }
        source.Set(TestContext.Task(101, "CW-101", 320118, TestContext.Item(1001, "SKU", 2)));
        var databaseError = new DbUpdateException("foreign key failed");
        await using var failingDb = new SaveConflictSqlDbContext(options)
        {
            ConflictException = databaseError,
            BeforeConflict = () => Task.CompletedTask
        };
        var service = TestContext.CreateService(failingDb, source);

        var thrown = await Assert.ThrowsAsync<DbUpdateException>(() =>
            service.EnsurePostPickSourceCurrentAsync(orderId, TestContext.User()));

        Assert.Same(databaseError, thrown);
    }

    [Fact]
    public async Task Decision_request_id_cannot_be_reused_for_the_opposite_decision()
    {
        await using var db = TestContext.CreateDatabase();
        var source = new MutableSourceReader(TestContext.Task(101, "CW-101", 320118, TestContext.Item(1001, "SKU", 1)));
        var service = TestContext.CreateService(db, source);
        var order = await CreatePostPickOrderAsync(service, db, DispatchOrderStatus.Picked);
        source.Set(TestContext.Task(101, "CW-101", 320118, TestContext.Item(1001, "SKU", 2)));
        Assert.True((await service.EnsurePostPickSourceCurrentAsync(order.id, TestContext.User())).source_change_pending);
        var frozen = await db.GetDbSet<DispatchOrderEntity>().AsNoTracking().SingleAsync();
        var version = (await db.GetDbSet<DispatchSourceChangeEventEntity>().SingleAsync()).source_version;
        await service.DecideSourceChangeAsync(order.id,
            Decision("CONTINUE", version, "继续", "same-decision-request", frozen.row_version), TestContext.User());

        var exception = await Assert.ThrowsAsync<DispatchWorkflowCommandException>(() =>
            service.DecideSourceChangeAsync(order.id,
                Decision("CANCEL", version, "取消", "same-decision-request", frozen.row_version), TestContext.User()));

        Assert.Equal("IDEMPOTENCY_CONFLICT", exception.ErrorCode);
    }

    [Fact]
    public async Task Cancel_releases_unposted_allocations_invalidates_measurements_and_preserves_audit()
    {
        await using var db = TestContext.CreateDatabase();
        var source = new MutableSourceReader(TestContext.Task(101, "CW-101", 320118, TestContext.Item(1001, "SKU", 1)));
        var service = TestContext.CreateService(db, source);
        var order = await CreatePostPickOrderAsync(service, db, DispatchOrderStatus.Weighing);
        var task = await db.GetDbSet<DispatchPackingTaskEntity>().SingleAsync();
        var item = await db.GetDbSet<DispatchPackingTaskItemEntity>().SingleAsync();
        var detail = new DispatchlistEntity { dispatch_order_id = order.id, packing_task_id = task.id, packing_task_item_id = item.id, dispatch_no = order.dispatch_no, dispatch_status = 3 };
        db.GetDbSet<DispatchlistEntity>().Add(detail);
        db.GetDbSet<DispatchpicklistEntity>().Add(new DispatchpicklistEntity { Dispatchlist = detail, packing_task_item_id = item.id, pick_qty = 1, picked_qty = 1 });
        db.GetDbSet<WeighingBoxEntity>().Add(new WeighingBoxEntity { packing_task_id = task.id, box_identity = "BOX-1", source_box_identity = "BOX-1", measurement_status = "MEASURED", weight = 1, length = 1, width = 1, height = 1 });
        await db.SaveChangesAsync();
        source.Set(TestContext.Task(101, "CW-101", 320118, TestContext.Item(1001, "SKU", 2)));
        Assert.True((await service.EnsurePostPickSourceCurrentAsync(order.id, TestContext.User())).source_change_pending);
        var frozen = await db.GetDbSet<DispatchOrderEntity>().AsNoTracking().SingleAsync();
        var version = (await db.GetDbSet<DispatchSourceChangeEventEntity>().SingleAsync(t => t.decision == DispatchSourceChangeDecision.Detected)).source_version;

        var result = await service.DecideSourceChangeAsync(order.id,
            Decision("CANCEL", version, "来源取消，人工终止", "cancel-1", frozen.row_version), TestContext.User());

        Assert.Equal("MANUAL_CANCELLED", result.status);
        Assert.Empty((await db.GetDbSet<DispatchOrderEntity>().AsNoTracking().SingleAsync()).pending_source_version);
        Assert.Empty(await db.GetDbSet<DispatchpicklistEntity>().ToListAsync());
        Assert.True((await db.GetDbSet<WeighingBoxEntity>().SingleAsync()).is_invalidated);
        Assert.Contains(await db.GetDbSet<DispatchSourceChangeEventEntity>().ToListAsync(), t => t.decision == DispatchSourceChangeDecision.CancelShipment);
    }

    [Fact]
    public async Task Cancel_refuses_when_inventory_was_already_deducted()
    {
        await using var db = TestContext.CreateDatabase();
        var source = new MutableSourceReader(TestContext.Task(101, "CW-101", 320118, TestContext.Item(1001, "SKU", 1)));
        var service = TestContext.CreateService(db, source);
        var order = await CreatePostPickOrderAsync(service, db, DispatchOrderStatus.PendingOutbound);
        var task = await db.GetDbSet<DispatchPackingTaskEntity>().SingleAsync();
        var item = await db.GetDbSet<DispatchPackingTaskItemEntity>().SingleAsync();
        var detail = new DispatchlistEntity { dispatch_order_id = order.id, packing_task_id = task.id, packing_task_item_id = item.id, dispatch_no = order.dispatch_no };
        db.Add(detail);
        db.Add(new DispatchpicklistEntity { Dispatchlist = detail, packing_task_item_id = item.id, is_update_stock = true });
        await db.SaveChangesAsync();
        source.Set(TestContext.Task(101, "CW-101", 320118, TestContext.Item(1001, "SKU", 2)));
        Assert.True((await service.EnsurePostPickSourceCurrentAsync(order.id, TestContext.User())).source_change_pending);
        var frozen = await db.GetDbSet<DispatchOrderEntity>().AsNoTracking().SingleAsync();
        var version = (await db.GetDbSet<DispatchSourceChangeEventEntity>().SingleAsync()).source_version;

        var exception = await Assert.ThrowsAsync<DispatchWorkflowCommandException>(() => service.DecideSourceChangeAsync(
            order.id, Decision("CANCEL", version, "不能再发货", "cancel-posted", frozen.row_version), TestContext.User()));

        Assert.Equal("STOCK_ALREADY_DEDUCTED", exception.ErrorCode);
    }

    [Fact]
    public async Task Decision_requires_reason_and_matching_row_and_source_versions()
    {
        await using var db = TestContext.CreateDatabase();
        var source = new MutableSourceReader(TestContext.Task(101, "CW-101", 320118, TestContext.Item(1001, "SKU", 1)));
        var service = TestContext.CreateService(db, source);
        var order = await CreatePostPickOrderAsync(service, db, DispatchOrderStatus.Picked);

        await Assert.ThrowsAsync<ArgumentException>(() => service.DecideSourceChangeAsync(order.id,
            Decision("CONTINUE", "version", " ", "bad", 0), TestContext.User()));
    }

    [Fact]
    public async Task Guard_checks_warehouse_permission_before_reading_or_mutating_source_state()
    {
        await using var db = TestContext.CreateDatabase();
        var source = new MutableSourceReader(TestContext.Task(101, "CW-101", 320118, TestContext.Item(1001, "SKU", 1)));
        var setup = TestContext.CreateService(db, source);
        var order = await CreatePostPickOrderAsync(setup, db, DispatchOrderStatus.Picked);
        var denied = new DispatchWorkflowService(db, source.Contract, DenyingWarehouseAccess.Create());

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            denied.EnsurePostPickSourceCurrentAsync(order.id, TestContext.User()));

        Assert.False((await db.GetDbSet<DispatchOrderEntity>().SingleAsync()).source_change_pending);
    }

    [Fact]
    public async Task Guard_refreezes_a_previously_detected_but_never_accepted_version_when_it_reappears()
    {
        await using var db = TestContext.CreateDatabase();
        var original = TestContext.Task(101, "CW-101", 320118, TestContext.Item(1001, "SKU", 1));
        var source = new MutableSourceReader(original);
        var service = TestContext.CreateService(db, source);
        var order = await CreatePostPickOrderAsync(service, db, DispatchOrderStatus.Picked);
        var changed = TestContext.Task(101, "CW-101", 320118, TestContext.Item(1001, "SKU", 2));
        source.Set(changed);
        Assert.True((await service.EnsurePostPickSourceCurrentAsync(order.id, TestContext.User())).source_change_pending);

        source.Set(original);
        Assert.False((await service.EnsurePostPickSourceCurrentAsync(order.id, TestContext.User())).source_change_pending);
        var afterRestore = await db.GetDbSet<DispatchOrderEntity>().AsNoTracking().SingleAsync();
        Assert.Empty(afterRestore.pending_source_version);

        source.Set(changed);
        var reappeared = await service.EnsurePostPickSourceCurrentAsync(order.id, TestContext.User());

        Assert.True(reappeared.source_change_pending);
        Assert.Equal("SOURCE_CHANGE_PENDING", reappeared.error_code);
        var refrozen = await db.GetDbSet<DispatchOrderEntity>().SingleAsync();
        Assert.True(refrozen.source_change_pending);
        Assert.Equal(reappeared.source_version, refrozen.pending_source_version);
        Assert.True(refrozen.row_version > afterRestore.row_version);
        Assert.Single(await db.GetDbSet<DispatchSourceChangeEventEntity>()
            .Where(t => t.decision == DispatchSourceChangeDecision.Detected).ToListAsync());
    }

    [Fact]
    public async Task Guard_clears_pending_version_when_a_historical_continue_accepts_current_source()
    {
        await using var db = TestContext.CreateDatabase();
        var source = new MutableSourceReader(TestContext.Task(101, "CW-101", 320118, TestContext.Item(1001, "SKU", 1)));
        var service = TestContext.CreateService(db, source);
        var order = await CreatePostPickOrderAsync(service, db, DispatchOrderStatus.Picked);
        source.Set(TestContext.Task(101, "CW-101", 320118, TestContext.Item(1001, "SKU", 2)));
        var pending = await service.EnsurePostPickSourceCurrentAsync(order.id, TestContext.User());
        db.GetDbSet<DispatchSourceChangeEventEntity>().Add(new DispatchSourceChangeEventEntity
        {
            dispatch_order_id = order.id,
            source_version = pending.source_version,
            event_idempotency_key = "historical-continue",
            decision = DispatchSourceChangeDecision.ContinueShipment,
            decision_time = DateTime.Now
        });
        await db.SaveChangesAsync();

        var result = await service.EnsurePostPickSourceCurrentAsync(order.id, TestContext.User());

        Assert.False(result.source_change_pending);
        var stored = await db.GetDbSet<DispatchOrderEntity>().AsNoTracking().SingleAsync();
        Assert.Empty(stored.pending_source_version);
        Assert.Empty(stored.source_change_snapshot);
    }

    [Fact]
    public async Task Outbound_change_is_anomaly_only_and_does_not_freeze_or_revert()
    {
        await using var db = TestContext.CreateDatabase();
        var source = new MutableSourceReader(TestContext.Task(101, "CW-101", 320118, TestContext.Item(1001, "SKU", 1)));
        var service = TestContext.CreateService(db, source);
        var order = await CreatePostPickOrderAsync(service, db, DispatchOrderStatus.Outbound);
        source.Set(TestContext.Task(101, "CW-101", 320118, TestContext.Item(1001, "SKU", 2)));

        await service.EnsurePostPickSourceCurrentAsync(order.id, TestContext.User());

        var stored = await db.GetDbSet<DispatchOrderEntity>().SingleAsync();
        Assert.Equal(DispatchOrderStatus.Outbound, stored.status);
        Assert.False(stored.source_change_pending);
        Assert.Equal(DispatchSourceChangeDecision.OutboundAnomaly,
            (await db.GetDbSet<DispatchSourceChangeEventEntity>().SingleAsync()).decision);
    }

    [Fact]
    public void Source_decision_endpoint_has_explicit_authorization()
    {
        var method = typeof(DispatchWorkflowController).GetMethod(nameof(DispatchWorkflowController.DecideSourceChangeAsync));
        Assert.NotNull(method!.GetCustomAttribute<AuthorizeAttribute>());
    }

    private static async Task<DispatchOrderDetailViewModel> CreatePostPickOrderAsync(
        DispatchWorkflowService service,
        ModernWMS.Core.DBContext.SqlDBContext db,
        DispatchOrderStatus status)
    {
        var created = await service.CreateAsync(new CreateDispatchOrderRequest
        {
            warehouse_id = 320118,
            source_task_ids = [101]
        }, TestContext.User());
        var order = await db.GetDbSet<DispatchOrderEntity>().SingleAsync();
        order.status = status;
        foreach (var task in await db.GetDbSet<DispatchPackingTaskEntity>().ToListAsync()) task.status = status;
        await db.SaveChangesAsync();
        return created;
    }

    private static SourceDecisionRequest Decision(
        string decision, string sourceVersion, string reason, string requestId, long rowVersion) => new()
        {
            decision = decision,
            source_version = sourceVersion,
            reason = reason,
            request_id = requestId,
            row_version = rowVersion
        };
}
