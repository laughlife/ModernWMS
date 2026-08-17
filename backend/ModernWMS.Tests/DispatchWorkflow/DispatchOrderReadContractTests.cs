using Microsoft.EntityFrameworkCore;
using ModernWMS.WMS.Entities.Models;
using ModernWMS.WMS.Entities.ViewModels.DispatchWorkflow;
using ModernWMS.WMS.Services.DispatchWorkflow;

namespace ModernWMS.Tests.DispatchWorkflow;

public class DispatchOrderReadContractTests
{
    [Fact(Skip = "依赖已移除的 EF InMemory 服务实现；等待 Dapper 集成测试夹具替换")]
    public async Task Page_and_detail_expose_source_decision_and_signing_facts()
    {
        await using var db = TestContext.CreateDatabase();
        var source = new MutableSourceReader(
            TestContext.Task(101, "CW-101", 320118, TestContext.Item(1001, "SKU", 1)));
        var access = new RecordingWarehouseAccess();
        var workflow = TestContext.CreateService(db, source, access);
        var created = await workflow.CreateAsync(new CreateDispatchOrderRequest
        {
            warehouse_id = 320118,
            source_task_ids = [101]
        }, TestContext.User());
        var order = await db.GetDbSet<DispatchOrderEntity>().SingleAsync();
        order.status = DispatchOrderStatus.Outbound;
        order.source_change_pending = true;
        order.pending_source_version = "pending-v2";
        order.source_change_snapshot = "{\"changed\":true}";
        order.accepted_source_version = "accepted-v1";
        order.signed_qty = 12;
        order.damaged_qty = 1;
        order.signed_at = new DateTime(2026, 8, 16, 12, 30, 0);
        order.signed_by_name = "receiver";
        order.notification_status = DispatchSignNotificationStatus.Failed;
        order.notification_last_error = "remote unavailable";
        db.GetDbSet<DispatchSourceChangeEventEntity>().AddRange(
            new DispatchSourceChangeEventEntity
            {
                dispatch_order_id = order.id,
                source_version = "anomaly-v1",
                event_idempotency_key = "anomaly-v1",
                decision = DispatchSourceChangeDecision.OutboundAnomaly,
                decision_time = new DateTime(2026, 8, 16, 12, 0, 0),
                diff_snapshot = "{\"version\":1}"
            },
            new DispatchSourceChangeEventEntity
            {
                dispatch_order_id = order.id,
                source_version = "anomaly-v2",
                event_idempotency_key = "anomaly-v2",
                decision = DispatchSourceChangeDecision.OutboundAnomaly,
                decision_time = new DateTime(2026, 8, 16, 13, 0, 0),
                diff_snapshot = "{\"version\":2}"
            });
        await db.SaveChangesAsync();
        var query = new DispatchOrderQueryService(TestContext.CreateConnectionFactory(), access.Contract, workflow);

        var page = await query.PageAsync(new DispatchOrderPageRequest
        {
            warehouse_id = 320118,
            status = "OUTBOUND"
        }, TestContext.User());
        var summary = Assert.Single(page.Data);
        AssertReadFacts(summary);

        var detail = await query.GetAsync(created.id, TestContext.User());
        AssertReadFacts(detail);
    }

    private static void AssertReadFacts(DispatchOrderSummaryViewModel result)
    {
        Assert.True(result.source_change_pending);
        Assert.Equal("pending-v2", result.pending_source_version);
        Assert.Equal("{\"changed\":true}", result.source_change_snapshot);
        Assert.Equal("accepted-v1", result.accepted_source_version);
        Assert.Equal(12, result.signed_qty);
        Assert.Equal(1, result.damaged_qty);
        Assert.Equal("receiver", result.signed_by_name);
        Assert.NotNull(result.signed_at);
        Assert.Equal("FAILED", result.notification_status);
        Assert.Equal("remote unavailable", result.notification_last_error);
        Assert.True(result.outbound_source_anomaly);
        Assert.Equal("{\"version\":2}", result.outbound_source_anomaly_snapshot);
    }
}
