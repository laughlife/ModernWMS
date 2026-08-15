using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authorization;
using ModernWMS.Core.DBContext.Entities;
using ModernWMS.WMS.Entities.Models;
using ModernWMS.WMS.Entities.ViewModels.DispatchWorkflow;
using ModernWMS.WMS.Services.DispatchWorkflow;
using ModernWMS.WMS.Services.Dispatchlist;

namespace ModernWMS.Tests.DispatchWorkflow;

public class DispatchWorkflowOutboundTests
{
    [Theory]
    [InlineData(nameof(ModernWMS.WMS.Controllers.DispatchWorkflow.DispatchWorkflowController.ConfirmOutboundAsync))]
    [InlineData(nameof(ModernWMS.WMS.Controllers.DispatchWorkflow.DispatchWorkflowController.CancelOutboundAsync))]
    [InlineData(nameof(ModernWMS.WMS.Controllers.DispatchWorkflow.DispatchWorkflowController.SignAsync))]
    public void Outbound_mutation_endpoints_require_authorization(string methodName)
    {
        var method = typeof(ModernWMS.WMS.Controllers.DispatchWorkflow.DispatchWorkflowController)
            .GetMethod(methodName);

        Assert.NotNull(method);
        Assert.NotNull(method!.GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true).SingleOrDefault());
    }

    [Fact]
    public async Task ConfirmOutboundAsync_deducts_the_exact_allocated_stock_row_and_writes_ledger()
    {
        var context = await ReadyOrderAsync(stockQuantity: 7, pickedQuantity: 2);

        var result = await context.Service.ConfirmOutboundAsync(context.OrderId,
            Request("confirm-1", context.RowVersion), TestContext.User());

        Assert.Equal("OUTBOUND", result.status);
        context.Db.ChangeTracker.Clear();
        Assert.Equal(5, (await context.Db.GetDbSet<StockEntity>().SingleAsync(t => t.id == context.StockId)).qty);
        Assert.True((await context.Db.GetDbSet<DispatchpicklistEntity>().SingleAsync()).is_update_stock);
        var movement = await context.Db.GetDbSet<WmsStockRecordEntity>().SingleAsync();
        Assert.Equal("DISPATCH_OUT", movement.biz_type);
        Assert.Equal(context.OrderId, movement.biz_id);
        Assert.Equal(context.StockId, movement.stock_id);
        Assert.Equal(-2, movement.change_qty);
        await context.Db.DisposeAsync();
    }

    [Fact]
    public async Task ConfirmOutboundAsync_validates_every_allocation_before_any_stock_is_changed()
    {
        var context = await ReadyOrderAsync(stockQuantity: 7, pickedQuantity: 2);
        var secondStock = new StockEntity
        {
            id = 22, sku_id = 11, goods_location_id = 11, goods_owner_id = 31,
            qty = 0, tenant_id = 88, last_update_time = DateTime.Now
        };
        context.Db.GetDbSet<StockEntity>().Add(secondStock);
        var detail = await context.Db.GetDbSet<DispatchlistEntity>().SingleAsync();
        context.Db.GetDbSet<DispatchpicklistEntity>().Add(new DispatchpicklistEntity
        {
            dispatchlist_id = detail.id, stock_id = secondStock.id, sku_id = secondStock.sku_id,
            goods_location_id = secondStock.goods_location_id, goods_owner_id = secondStock.goods_owner_id,
            pick_qty = 1, picked_qty = 1, is_update_stock = false, last_update_time = DateTime.Now
        });
        await context.Db.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<DispatchWorkflowCommandException>(() =>
            context.Service.ConfirmOutboundAsync(context.OrderId,
                Request("short", context.RowVersion), TestContext.User()));

        Assert.Equal("STOCK_CONFLICT", exception.ErrorCode);
        context.Db.ChangeTracker.Clear();
        Assert.Equal(7, (await context.Db.GetDbSet<StockEntity>().SingleAsync(t => t.id == context.StockId)).qty);
        Assert.Empty(await context.Db.GetDbSet<WmsStockRecordEntity>().ToListAsync());
        Assert.All(await context.Db.GetDbSet<DispatchpicklistEntity>().ToListAsync(), t => Assert.False(t.is_update_stock));
        await context.Db.DisposeAsync();
    }

    [Fact]
    public async Task ConfirmOutboundAsync_rejects_an_allocated_stock_row_outside_the_order_warehouse()
    {
        var context = await ReadyOrderAsync(stockQuantity: 7, pickedQuantity: 2);
        var location = await context.Db.GetDbSet<GoodslocationEntity>().SingleAsync();
        location.warehouse_id = 999;
        await context.Db.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<DispatchWorkflowCommandException>(() =>
            context.Service.ConfirmOutboundAsync(context.OrderId,
                Request("wrong-warehouse", context.RowVersion), TestContext.User()));

        Assert.Equal("STOCK_CONFLICT", exception.ErrorCode);
        context.Db.ChangeTracker.Clear();
        Assert.Equal(7, (await context.Db.GetDbSet<StockEntity>().SingleAsync()).qty);
        Assert.Empty(await context.Db.GetDbSet<WmsStockRecordEntity>().ToListAsync());
        await context.Db.DisposeAsync();
    }

    [Fact]
    public async Task ConfirmOutboundAsync_rejects_a_detail_with_no_allocation_without_writing_anything()
    {
        var context = await ReadyOrderAsync(stockQuantity: 7, pickedQuantity: 2);
        context.Db.GetDbSet<DispatchpicklistEntity>().Remove(
            await context.Db.GetDbSet<DispatchpicklistEntity>().SingleAsync());
        await context.Db.SaveChangesAsync();

        await AssertStockConflictLeavesOrderUntouchedAsync(context, "missing-allocation");
    }

    [Fact]
    public async Task ConfirmOutboundAsync_rejects_an_allocation_attached_to_the_wrong_task_item()
    {
        var context = await ReadyOrderAsync(stockQuantity: 7, pickedQuantity: 2);
        (await context.Db.GetDbSet<DispatchpicklistEntity>().SingleAsync()).packing_task_item_id = null;
        await context.Db.SaveChangesAsync();

        await AssertStockConflictLeavesOrderUntouchedAsync(context, "wrong-item");
    }

    [Fact]
    public async Task ConfirmOutboundAsync_rejects_a_detail_attached_to_another_orders_active_task()
    {
        var context = await ReadyOrderAsync(stockQuantity: 7, pickedQuantity: 2);
        var otherSource = TestContext.Task(
            202, "CW-202", 320118, TestContext.Item(2002, "SKU-1", 2)) with
        {
            Boxes = [new("BOX-B", 1, "{\"boxId\":\"BOX-B\"}")],
            CartonsJson = "[{\"boxId\":\"BOX-B\"}]"
        };
        TestContext.SeedCommodityMaps(context.Db, otherSource);
        context.Source.Set(otherSource);
        var other = await context.Service.CreateAsync(new CreateDispatchOrderRequest
        {
            warehouse_id = 320118,
            source_task_ids = [202]
        }, TestContext.User());
        var otherTask = await context.Db.GetDbSet<DispatchPackingTaskEntity>()
            .Include(t => t.items).SingleAsync(t => t.dispatch_order_id == other.id);
        var otherItem = Assert.Single(otherTask.items);
        otherItem.wms_sku_id = 10;
        var detail = await context.Db.GetDbSet<DispatchlistEntity>()
            .SingleAsync(t => t.dispatch_order_id == context.OrderId);
        var allocation = await context.Db.GetDbSet<DispatchpicklistEntity>().SingleAsync();
        detail.packing_task_id = otherTask.id;
        detail.packing_task_item_id = otherItem.id;
        allocation.packing_task_item_id = otherItem.id;
        await context.Db.SaveChangesAsync();

        await AssertStockConflictLeavesOrderUntouchedAsync(context, "cross-order-task");
    }

    [Fact]
    public async Task ConfirmOutboundAsync_rejects_overallocated_detail_quantity()
    {
        var context = await ReadyOrderAsync(stockQuantity: 7, pickedQuantity: 2);
        var allocation = await context.Db.GetDbSet<DispatchpicklistEntity>().SingleAsync();
        allocation.pick_qty = 3;
        allocation.picked_qty = 3;
        await context.Db.SaveChangesAsync();

        await AssertStockConflictLeavesOrderUntouchedAsync(context, "overallocated");
    }

    [Fact]
    public async Task ConfirmOutboundAsync_replays_the_original_success_without_a_second_deduction()
    {
        var context = await ReadyOrderAsync(stockQuantity: 7, pickedQuantity: 2);
        var request = Request("replay", context.RowVersion);

        var first = await context.Service.ConfirmOutboundAsync(
            context.OrderId, request, TestContext.User());
        var replay = await context.Service.ConfirmOutboundAsync(
            context.OrderId, request, TestContext.User());

        Assert.Equal(first.row_version, replay.row_version);
        context.Db.ChangeTracker.Clear();
        Assert.Equal(5, (await context.Db.GetDbSet<StockEntity>().SingleAsync(t => t.id == context.StockId)).qty);
        Assert.Single(await context.Db.GetDbSet<WmsStockRecordEntity>().ToListAsync());
        await context.Db.DisposeAsync();
    }

    [Fact]
    public async Task ConfirmOutboundAsync_rejects_an_incompletely_measured_order()
    {
        var context = await ReadyOrderAsync(stockQuantity: 7, pickedQuantity: 2);
        var task = await context.Db.GetDbSet<DispatchPackingTaskEntity>().SingleAsync();
        task.measured_box_count = 0;
        (await context.Db.GetDbSet<WeighingBoxEntity>().SingleAsync()).measurement_status = "UNMEASURED";
        await context.Db.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<DispatchWorkflowCommandException>(() =>
            context.Service.ConfirmOutboundAsync(context.OrderId,
                Request("incomplete", context.RowVersion), TestContext.User()));

        Assert.Equal("WEIGHING_INCOMPLETE", exception.ErrorCode);
        context.Db.ChangeTracker.Clear();
        Assert.Equal(7, (await context.Db.GetDbSet<StockEntity>().SingleAsync(t => t.id == context.StockId)).qty);
        await context.Db.DisposeAsync();
    }

    [Fact]
    public async Task ConfirmOutboundAsync_commits_a_source_change_freeze_without_deducting_stock()
    {
        var context = await ReadyOrderAsync(stockQuantity: 7, pickedQuantity: 2);
        context.Source.Set(TestContext.Task(
            101, "CW-101", 320118, TestContext.Item(1001, "SKU-1", 3)) with
        {
            SourceVersion = "changed-v2",
            Boxes = [new("BOX-A", 1, "{\"boxId\":\"BOX-A\"}")],
            CartonsJson = "[{\"boxId\":\"BOX-A\"}]"
        });

        var exception = await Assert.ThrowsAsync<DispatchWorkflowCommandException>(() =>
            context.Service.ConfirmOutboundAsync(context.OrderId,
                Request("freeze", context.RowVersion), TestContext.User()));

        Assert.Equal("SOURCE_CHANGE_PENDING", exception.ErrorCode);
        context.Db.ChangeTracker.Clear();
        Assert.True((await context.Db.GetDbSet<DispatchOrderEntity>().SingleAsync()).source_change_pending);
        Assert.Equal(7, (await context.Db.GetDbSet<StockEntity>().SingleAsync(t => t.id == context.StockId)).qty);
        Assert.Empty(await context.Db.GetDbSet<WmsStockRecordEntity>().ToListAsync());
        await context.Db.DisposeAsync();
    }

    [Fact]
    public async Task CancelOutboundAsync_restores_the_exact_stock_row_and_is_idempotent()
    {
        var context = await ReadyOrderAsync(stockQuantity: 7, pickedQuantity: 2);
        var outbound = await context.Service.ConfirmOutboundAsync(context.OrderId,
            Request("confirm", context.RowVersion), TestContext.User());
        var request = Request("cancel", outbound.row_version);

        var first = await context.Service.CancelOutboundAsync(
            context.OrderId, request, TestContext.User());
        var replay = await context.Service.CancelOutboundAsync(
            context.OrderId, request, TestContext.User());

        Assert.Equal("PENDING_OUTBOUND", first.status);
        Assert.Equal(first.row_version, replay.row_version);
        context.Db.ChangeTracker.Clear();
        Assert.Equal(7, (await context.Db.GetDbSet<StockEntity>().SingleAsync(t => t.id == context.StockId)).qty);
        Assert.False((await context.Db.GetDbSet<DispatchpicklistEntity>().SingleAsync()).is_update_stock);
        Assert.Equal(["DISPATCH_IN", "DISPATCH_OUT"],
            (await context.Db.GetDbSet<WmsStockRecordEntity>().OrderBy(t => t.biz_type)
                .Select(t => t.biz_type).ToListAsync()).ToArray());
        await context.Db.DisposeAsync();
    }

    [Fact]
    public async Task Confirm_cancel_confirm_cycle_writes_balanced_append_only_stock_records()
    {
        var context = await ReadyOrderAsync(stockQuantity: 7, pickedQuantity: 2);
        var firstOut = await context.Service.ConfirmOutboundAsync(context.OrderId,
            Request("out-1", context.RowVersion), TestContext.User());
        var cancelled = await context.Service.CancelOutboundAsync(context.OrderId,
            Request("in-1", firstOut.row_version), TestContext.User());

        var secondOut = await context.Service.ConfirmOutboundAsync(context.OrderId,
            Request("out-2", cancelled.row_version), TestContext.User());

        Assert.Equal("OUTBOUND", secondOut.status);
        context.Db.ChangeTracker.Clear();
        Assert.Equal(5, (await context.Db.GetDbSet<StockEntity>().SingleAsync(t => t.id == context.StockId)).qty);
        Assert.Equal(["DISPATCH_OUT", "DISPATCH_IN", "DISPATCH_OUT_2"],
            (await context.Db.GetDbSet<WmsStockRecordEntity>().OrderBy(t => t.id)
                .Select(t => t.biz_type).ToListAsync()).ToArray());
        Assert.Equal(-2, (await context.Db.GetDbSet<WmsStockRecordEntity>().SumAsync(t => t.change_qty)));
        await context.Db.DisposeAsync();
    }

    [Fact]
    public async Task Source_change_after_outbound_appends_anomaly_without_reversing_stock()
    {
        var context = await ReadyOrderAsync(stockQuantity: 7, pickedQuantity: 2);
        await context.Service.ConfirmOutboundAsync(context.OrderId,
            Request("outbound", context.RowVersion), TestContext.User());
        context.Source.Set(TestContext.Task(
            101, "CW-101", 320118, TestContext.Item(1001, "SKU-1", 3)) with
        {
            SourceVersion = "post-outbound-v2",
            Boxes = [new("BOX-A", 1, "{\"boxId\":\"BOX-A\"}")],
            CartonsJson = "[{\"boxId\":\"BOX-A\"}]"
        });

        var guard = await context.Service.EnsurePostPickSourceCurrentAsync(
            context.OrderId, TestContext.User());

        Assert.False(guard.source_change_pending);
        context.Db.ChangeTracker.Clear();
        Assert.Equal(DispatchOrderStatus.Outbound,
            (await context.Db.GetDbSet<DispatchOrderEntity>().SingleAsync()).status);
        Assert.Equal(5, (await context.Db.GetDbSet<StockEntity>().SingleAsync(t => t.id == context.StockId)).qty);
        Assert.Single(await context.Db.GetDbSet<DispatchSourceChangeEventEntity>()
            .Where(t => t.decision == DispatchSourceChangeDecision.OutboundAnomaly).ToListAsync());
        await context.Db.DisposeAsync();
    }

    [Fact]
    public async Task SignAsync_commits_the_whole_order_fact_before_notifying_and_notifies_only_once()
    {
        var context = await ReadyOrderAsync(stockQuantity: 7, pickedQuantity: 2);
        var outbound = await context.Service.ConfirmOutboundAsync(context.OrderId,
            Request("outbound", context.RowVersion), TestContext.User());
        var notifier = new RecordingSignNotifier
        {
            BeforeReturn = async () => Assert.NotNull(
                (await context.Db.GetDbSet<DispatchOrderEntity>().SingleAsync()).signed_at)
        };
        var service = new ModernWMS.WMS.Services.DispatchWorkflow.DispatchWorkflowService(
            context.Db, context.Source.Contract, new RecordingWarehouseAccess().Contract, notifier);

        var first = await service.SignAsync(context.OrderId, new SignDispatchOrderRequest
        {
            request_id = "sign-1", row_version = outbound.row_version, damaged_qty = 1
        }, TestContext.User());
        var replay = await service.SignAsync(context.OrderId, new SignDispatchOrderRequest
        {
            request_id = "sign-1", row_version = outbound.row_version, damaged_qty = 1
        }, TestContext.User());

        Assert.Equal("SENT", first.notification_status);
        Assert.Equal("SENT", replay.notification_status);
        Assert.Equal(["CW-DISPATCH"], notifier.DispatchNos);
        context.Db.ChangeTracker.Clear();
        var signed = await context.Db.GetDbSet<DispatchOrderEntity>().SingleAsync();
        Assert.Equal(1, signed.signed_qty);
        Assert.Equal(1, signed.damaged_qty);
        Assert.Equal(TestContext.User().user_id, signed.signed_by);
        Assert.NotNull(signed.notification_sent_at);
        await context.Db.DisposeAsync();
    }

    [Fact]
    public async Task SignAsync_keeps_the_committed_fact_failed_and_retries_notification_without_resigning()
    {
        var context = await ReadyOrderAsync(stockQuantity: 7, pickedQuantity: 2);
        var outbound = await context.Service.ConfirmOutboundAsync(context.OrderId,
            Request("outbound", context.RowVersion), TestContext.User());
        var notifier = new RecordingSignNotifier { Results = new Queue<bool>([false, true]) };
        var service = new ModernWMS.WMS.Services.DispatchWorkflow.DispatchWorkflowService(
            context.Db, context.Source.Contract, new RecordingWarehouseAccess().Contract, notifier);

        var failed = await service.SignAsync(context.OrderId, new SignDispatchOrderRequest
        {
            request_id = "sign-1", row_version = outbound.row_version, damaged_qty = 0
        }, TestContext.User());
        var retried = await service.SignAsync(context.OrderId, new SignDispatchOrderRequest
        {
            request_id = "sign-2", row_version = failed.row_version, damaged_qty = 0
        }, TestContext.User());

        Assert.Equal("FAILED", failed.notification_status);
        Assert.Equal("SENT", retried.notification_status);
        Assert.Equal(2, notifier.DispatchNos.Count);
        context.Db.ChangeTracker.Clear();
        var signed = await context.Db.GetDbSet<DispatchOrderEntity>().SingleAsync();
        Assert.NotNull(signed.signed_at);
        Assert.Equal(2, signed.notification_attempt_count);
        Assert.Equal(DispatchSignNotificationStatus.Sent, signed.notification_status);
        Assert.Single(await context.Db.GetDbSet<DispatchWorkflowOperationEntity>()
            .Where(t => t.operation == DispatchWorkflowOperation.Sign).ToListAsync());
        await context.Db.DisposeAsync();
    }

    [Fact]
    public async Task SignAsync_does_not_reclaim_a_fresh_sending_notification()
    {
        var context = await SignedSendingOrderAsync(DateTime.Now);
        var notifier = new RecordingSignNotifier();
        var service = new ModernWMS.WMS.Services.DispatchWorkflow.DispatchWorkflowService(
            context.Db, context.Source.Contract, new RecordingWarehouseAccess().Contract, notifier);

        var result = await service.SignAsync(context.OrderId, new SignDispatchOrderRequest
        {
            request_id = "fresh", row_version = context.RowVersion, damaged_qty = 0
        }, TestContext.User());

        Assert.Equal("SENDING", result.notification_status);
        Assert.Empty(notifier.DispatchNos);
        await context.Db.DisposeAsync();
    }

    [Fact]
    public async Task SignAsync_reclaims_a_sending_notification_after_the_ten_minute_lease_expires()
    {
        var context = await SignedSendingOrderAsync(DateTime.Now.AddMinutes(-11));
        var notifier = new RecordingSignNotifier();
        var service = new ModernWMS.WMS.Services.DispatchWorkflow.DispatchWorkflowService(
            context.Db, context.Source.Contract, new RecordingWarehouseAccess().Contract, notifier);

        var result = await service.SignAsync(context.OrderId, new SignDispatchOrderRequest
        {
            request_id = "stale", row_version = context.RowVersion, damaged_qty = 0
        }, TestContext.User());

        Assert.Equal("SENT", result.notification_status);
        Assert.Single(notifier.DispatchNos);
        await context.Db.DisposeAsync();
    }

    [Fact]
    public async Task SignAsync_finishes_failed_state_even_if_request_is_cancelled_during_remote_delivery()
    {
        var context = await ReadyOrderAsync(stockQuantity: 7, pickedQuantity: 2);
        var outbound = await context.Service.ConfirmOutboundAsync(context.OrderId,
            Request("outbound", context.RowVersion), TestContext.User());
        using var cancellation = new CancellationTokenSource();
        var notifier = new RecordingSignNotifier
        {
            Results = new Queue<bool>([false]),
            BeforeReturn = () =>
            {
                cancellation.Cancel();
                return Task.CompletedTask;
            }
        };
        var service = new ModernWMS.WMS.Services.DispatchWorkflow.DispatchWorkflowService(
            context.Db, context.Source.Contract, new RecordingWarehouseAccess().Contract, notifier);

        var result = await service.SignAsync(context.OrderId, new SignDispatchOrderRequest
        {
            request_id = "cancelled", row_version = outbound.row_version, damaged_qty = 0
        }, TestContext.User(), cancellation.Token);

        Assert.Equal("FAILED", result.notification_status);
        context.Db.ChangeTracker.Clear();
        Assert.Equal(DispatchSignNotificationStatus.Failed,
            (await context.Db.GetDbSet<DispatchOrderEntity>().SingleAsync()).notification_status);
        await context.Db.DisposeAsync();
    }

    [Fact]
    public async Task SignAsync_allows_only_one_caller_to_hold_a_fresh_notification_lease()
    {
        var context = await ReadyOrderAsync(stockQuantity: 7, pickedQuantity: 2);
        var outbound = await context.Service.ConfirmOutboundAsync(context.OrderId,
            Request("outbound", context.RowVersion), TestContext.User());
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var notifier = new RecordingSignNotifier
        {
            BeforeReturn = async () =>
            {
                entered.SetResult();
                await release.Task;
            }
        };
        var service = new ModernWMS.WMS.Services.DispatchWorkflow.DispatchWorkflowService(
            context.Db, context.Source.Contract, new RecordingWarehouseAccess().Contract, notifier);
        var firstTask = service.SignAsync(context.OrderId, new SignDispatchOrderRequest
        {
            request_id = "claim-1", row_version = outbound.row_version, damaged_qty = 0
        }, TestContext.User());
        await entered.Task;
        var currentVersion = (await context.Db.GetDbSet<DispatchOrderEntity>().SingleAsync()).row_version;

        var second = await service.SignAsync(context.OrderId, new SignDispatchOrderRequest
        {
            request_id = "claim-2", row_version = currentVersion, damaged_qty = 0
        }, TestContext.User());
        release.SetResult();
        var first = await firstTask;

        Assert.Equal("SENDING", second.notification_status);
        Assert.Equal("SENT", first.notification_status);
        Assert.Single(notifier.DispatchNos);
        await context.Db.DisposeAsync();
    }

    [Fact]
    public async Task SignAsync_old_expired_lease_cannot_overwrite_the_new_claims_success()
    {
        var context = await ReadyOrderAsync(stockQuantity: 7, pickedQuantity: 2);
        var outbound = await context.Service.ConfirmOutboundAsync(context.OrderId,
            Request("outbound", context.RowVersion), TestContext.User());
        var notifier = new OverlappingLeaseNotifier();
        var service = new ModernWMS.WMS.Services.DispatchWorkflow.DispatchWorkflowService(
            context.Db, context.Source.Contract, new RecordingWarehouseAccess().Contract, notifier);
        var oldLease = service.SignAsync(context.OrderId, new SignDispatchOrderRequest
        {
            request_id = "lease-a", row_version = outbound.row_version, damaged_qty = 0
        }, TestContext.User());
        await notifier.FirstEntered.Task;
        var claimed = await context.Db.GetDbSet<DispatchOrderEntity>().SingleAsync();
        Assert.Equal(1, claimed.notification_attempt_count);
        claimed.notification_updated_at = DateTime.Now.AddMinutes(-11);
        await context.Db.SaveChangesAsync();

        var newLeaseTask = service.SignAsync(context.OrderId, new SignDispatchOrderRequest
        {
            request_id = "lease-b", row_version = claimed.row_version, damaged_qty = 0
        }, TestContext.User());
        await notifier.SecondEntered.Task;
        notifier.ReleaseFirst.SetResult();
        var staleCompletion = await oldLease;
        context.Db.ChangeTracker.Clear();
        var whileNewLeaseRuns = await context.Db.GetDbSet<DispatchOrderEntity>().SingleAsync();
        Assert.Equal(2, whileNewLeaseRuns.notification_attempt_count);
        Assert.Equal(DispatchSignNotificationStatus.Sending, whileNewLeaseRuns.notification_status);
        notifier.ReleaseSecond.SetResult();
        var newLease = await newLeaseTask;

        Assert.Equal("SENDING", staleCompletion.notification_status);
        Assert.Equal("SENT", newLease.notification_status);
        context.Db.ChangeTracker.Clear();
        var completed = await context.Db.GetDbSet<DispatchOrderEntity>().SingleAsync();
        Assert.Equal(2, completed.notification_attempt_count);
        Assert.Equal(DispatchSignNotificationStatus.Sent, completed.notification_status);
        Assert.Equal(2, notifier.RequestCount);
        await context.Db.DisposeAsync();
    }

    [Fact]
    public async Task CancelOutboundAsync_rejects_a_signed_order_without_restoring_stock()
    {
        var context = await ReadyOrderAsync(stockQuantity: 7, pickedQuantity: 2);
        var outbound = await context.Service.ConfirmOutboundAsync(context.OrderId,
            Request("outbound", context.RowVersion), TestContext.User());
        var notifier = new RecordingSignNotifier();
        var service = new ModernWMS.WMS.Services.DispatchWorkflow.DispatchWorkflowService(
            context.Db, context.Source.Contract, new RecordingWarehouseAccess().Contract, notifier);
        var signed = await service.SignAsync(context.OrderId, new SignDispatchOrderRequest
        {
            request_id = "sign", row_version = outbound.row_version, damaged_qty = 0
        }, TestContext.User());

        var exception = await Assert.ThrowsAsync<DispatchWorkflowCommandException>(() =>
            service.CancelOutboundAsync(context.OrderId,
                Request("cancel", signed.row_version), TestContext.User()));

        Assert.Equal("ORDER_ALREADY_SIGNED", exception.ErrorCode);
        context.Db.ChangeTracker.Clear();
        Assert.Equal(5, (await context.Db.GetDbSet<StockEntity>().SingleAsync(t => t.id == context.StockId)).qty);
        await context.Db.DisposeAsync();
    }

    private static OutboundCommandRequest Request(string requestId, long rowVersion) => new()
    {
        request_id = requestId,
        row_version = rowVersion
    };

    private static async Task AssertStockConflictLeavesOrderUntouchedAsync(
        OutboundTestContext context, string requestId)
    {
        var exception = await Assert.ThrowsAsync<DispatchWorkflowCommandException>(() =>
            context.Service.ConfirmOutboundAsync(context.OrderId,
                Request(requestId, context.RowVersion), TestContext.User()));
        Assert.Equal("STOCK_CONFLICT", exception.ErrorCode);
        context.Db.ChangeTracker.Clear();
        Assert.Equal(7, (await context.Db.GetDbSet<StockEntity>().SingleAsync()).qty);
        Assert.Equal(DispatchOrderStatus.PendingOutbound,
            (await context.Db.GetDbSet<DispatchOrderEntity>()
                .SingleAsync(t => t.id == context.OrderId)).status);
        Assert.Empty(await context.Db.GetDbSet<WmsStockRecordEntity>().ToListAsync());
        await context.Db.DisposeAsync();
    }

    private static async Task<OutboundTestContext> SignedSendingOrderAsync(DateTime notificationUpdatedAt)
    {
        var context = await ReadyOrderAsync(stockQuantity: 7, pickedQuantity: 2);
        await context.Service.ConfirmOutboundAsync(context.OrderId,
            Request("outbound", context.RowVersion), TestContext.User());
        var order = await context.Db.GetDbSet<DispatchOrderEntity>().SingleAsync();
        order.signed_qty = 2;
        order.damaged_qty = 0;
        order.signed_by = 7;
        order.signed_by_name = "tester";
        order.signed_at = DateTime.Now.AddMinutes(-20);
        order.notification_status = DispatchSignNotificationStatus.Sending;
        order.notification_attempt_count = 1;
        order.notification_updated_at = notificationUpdatedAt;
        order.row_version++;
        await context.Db.SaveChangesAsync();
        return context with { RowVersion = order.row_version };
    }

    private static async Task<OutboundTestContext> ReadyOrderAsync(int stockQuantity, int pickedQuantity)
    {
        var db = TestContext.CreateDatabase();
        var source = new MutableSourceReader(TestContext.Task(
            101, "CW-101", 320118, TestContext.Item(1001, "SKU-1", pickedQuantity)) with
        {
            Boxes = [new("BOX-A", 1, "{\"boxId\":\"BOX-A\"}")],
            CartonsJson = "[{\"boxId\":\"BOX-A\"}]"
        });
        var service = TestContext.CreateService(db, source);
        var created = await service.CreateAsync(new CreateDispatchOrderRequest
        {
            warehouse_id = 320118,
            source_task_ids = [101]
        }, TestContext.User());
        var order = await db.GetDbSet<DispatchOrderEntity>()
            .Include(t => t.packing_tasks).ThenInclude(t => t.items).SingleAsync();
        var task = Assert.Single(order.packing_tasks);
        var item = Assert.Single(task.items);
        item.wms_sku_id = 10;
        order.dispatch_no = "CW-DISPATCH";
        order.status = DispatchOrderStatus.PendingOutbound;
        order.row_version = 4;
        task.status = DispatchOrderStatus.PendingOutbound;
        task.expected_box_count = 1;
        task.measured_box_count = 1;
        db.GetDbSet<WeighingBoxEntity>().Add(new WeighingBoxEntity
        {
            packing_task = task, source_box_identity = "BOX-A", box_identity = "BOX-A", box_sequence = 1,
            weight = 1, length = 2, width = 3, height = 4, measurement_status = "MEASURED",
            measured_by = 7, measured_by_name = "tester", measured_at = DateTime.Now,
            create_time = DateTime.Now, last_update_time = DateTime.Now
        });
        db.GetDbSet<WarehouseEntity>().Add(new WarehouseEntity
        {
            id = 1, erp_warehouse_id = 320118, warehouse_name = "深圳自建仓", is_valid = true, tenant_id = 88
        });
        db.GetDbSet<GoodslocationEntity>().Add(new GoodslocationEntity
        {
            id = 11, warehouse_id = 1, location_name = "A-01", warehouse_area_property = 1,
            is_valid = true, tenant_id = 88
        });
        var stock = new StockEntity
        {
            id = 21, sku_id = 10, goods_location_id = 11, goods_owner_id = 31,
            qty = stockQuantity, tenant_id = 88, last_update_time = DateTime.Now
        };
        db.GetDbSet<StockEntity>().Add(stock);
        var detail = new DispatchlistEntity
        {
            dispatch_order = order, packing_task = task, packing_task_item = item,
            dispatch_no = order.dispatch_no, dispatch_status = 5, sku_id = 10,
            qty = pickedQuantity, lock_qty = pickedQuantity, picked_qty = pickedQuantity,
            tenant_id = 88, creator = "tester", create_time = DateTime.Now, last_update_time = DateTime.Now
        };
        db.GetDbSet<DispatchlistEntity>().Add(detail);
        await db.SaveChangesAsync();
        db.GetDbSet<DispatchpicklistEntity>().Add(new DispatchpicklistEntity
        {
            dispatchlist_id = detail.id, packing_task_item_id = item.id, stock_id = stock.id,
            sku_id = stock.sku_id, goods_location_id = stock.goods_location_id,
            goods_owner_id = stock.goods_owner_id, pick_qty = pickedQuantity, picked_qty = pickedQuantity,
            is_update_stock = false, last_update_time = DateTime.Now
        });
        await db.SaveChangesAsync();
        return new OutboundTestContext(db, service, source, order.id, stock.id, order.row_version);
    }

    private sealed record OutboundTestContext(
        ModernWMS.Core.DBContext.SqlDBContext Db,
        ModernWMS.WMS.Services.DispatchWorkflow.DispatchWorkflowService Service,
        MutableSourceReader Source,
        int OrderId,
        int StockId,
        long RowVersion);

    private sealed class RecordingSignNotifier : IDispatchSignNotificationClient
    {
        public List<string> DispatchNos { get; } = [];
        public Queue<bool> Results { get; set; } = new([true]);
        public Func<Task>? BeforeReturn { get; set; }

        public Task NotifySignedAsync(string dispatchNo, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public async Task<bool> TryNotifySignedAsync(
            string dispatchNo, CancellationToken cancellationToken = default)
        {
            DispatchNos.Add(dispatchNo);
            if (BeforeReturn != null)
            {
                await BeforeReturn();
            }
            return Results.Count == 0 || Results.Dequeue();
        }
    }

    private sealed class OverlappingLeaseNotifier : IDispatchSignNotificationClient
    {
        public TaskCompletionSource FirstEntered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseFirst { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource SecondEntered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseSecond { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int RequestCount { get; private set; }

        public Task NotifySignedAsync(string dispatchNo, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public async Task<bool> TryNotifySignedAsync(
            string dispatchNo, CancellationToken cancellationToken = default)
        {
            RequestCount++;
            if (RequestCount == 1)
            {
                FirstEntered.SetResult();
                await ReleaseFirst.Task;
                return false;
            }
            SecondEntered.SetResult();
            await ReleaseSecond.Task;
            return true;
        }
    }
}
