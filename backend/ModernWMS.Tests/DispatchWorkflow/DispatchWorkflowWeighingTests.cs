using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Reflection;
using ModernWMS.WMS.Controllers.DispatchWorkflow;
using ModernWMS.WMS.Entities.Models;
using ModernWMS.WMS.Entities.ViewModels.DispatchWorkflow;
using ModernWMS.WMS.IServices.DispatchWorkflow;
using ModernWMS.WMS.IServices.PackingTask;
using ModernWMS.WMS.Entities.ViewModels.PackingTask;
using ModernWMS.WMS.Services.DispatchWorkflow;

namespace ModernWMS.Tests.DispatchWorkflow;

public class DispatchWorkflowWeighingTests
{
    [Fact]
    public async Task StartWeighingAsync_materializes_exact_source_boxes_without_importing_measurements()
    {
        await using var db = TestContext.CreateDatabase();
        var first = TestContext.Task(101, "CW-101", 320118, TestContext.Item(1001, "SKU-1", 1)) with
        {
            Boxes =
            [
                new("BOX-A", 1, "{\"boxId\":\"BOX-A\",\"weight\":9}"),
                new("BOX-B", 2, "{\"boxId\":\"BOX-B\",\"length\":88}")
            ],
            CartonsJson = "[{\"boxId\":\"BOX-A\",\"weight\":9},{\"boxId\":\"BOX-B\",\"length\":88}]"
        };
        var second = TestContext.Task(102, "CW-102", 320118, TestContext.Item(1002, "SKU-2", 1)) with
        {
            Boxes = [new("BOX-C", 1, "{\"boxId\":\"BOX-C\",\"height\":77}")],
            CartonsJson = "[{\"boxId\":\"BOX-C\",\"height\":77}]"
        };
        var source = new MutableSourceReader(first, second);
        var service = TestContext.CreateService(db, source);
        var order = await service.CreateAsync(new CreateDispatchOrderRequest
        {
            warehouse_id = 320118,
            source_task_ids = [101, 102]
        }, TestContext.User());
        await MoveToPickedAsync(db, order.id);

        var result = await service.StartWeighingAsync(order.id, new WeighingOrderCommandRequest
        {
            request_id = "start-1",
            row_version = 1
        }, TestContext.User());

        Assert.Equal("WEIGHING", result.status);
        var boxes = await db.GetDbSet<WeighingBoxEntity>()
            .OrderBy(t => t.packing_task_id).ThenBy(t => t.box_sequence).ToListAsync();
        Assert.Equal(["BOX-A", "BOX-B", "BOX-C"], boxes.Select(t => t.source_box_identity).ToArray());
        Assert.All(boxes, box =>
        {
            Assert.Null(box.weight);
            Assert.Null(box.length);
            Assert.Null(box.width);
            Assert.Null(box.height);
            Assert.Equal("UNMEASURED", box.measurement_status);
        });
        Assert.Equal(2, boxes.Select(t => t.packing_task_id).Distinct().Count());
    }

    [Fact]
    public async Task StartWeighingAsync_fails_closed_when_source_has_no_physical_boxes()
    {
        await using var db = TestContext.CreateDatabase();
        var source = new MutableSourceReader(TestContext.Task(
            101, "CW-101", 320118, TestContext.Item(1001, "SKU-1", 1)));
        var service = TestContext.CreateService(db, source);
        var order = await service.CreateAsync(new CreateDispatchOrderRequest
        {
            warehouse_id = 320118,
            source_task_ids = [101]
        }, TestContext.User());
        await MoveToPickedAsync(db, order.id);

        var exception = await Assert.ThrowsAsync<DispatchWorkflowCommandException>(() =>
            service.StartWeighingAsync(order.id, OrderRequest("unsupported", 1), TestContext.User()));

        Assert.Equal("SOURCE_BOX_ID_UNSUPPORTED", exception.ErrorCode);
        Assert.Empty(await db.GetDbSet<WeighingBoxEntity>().ToListAsync());
        Assert.Equal(DispatchOrderStatus.Picked,
            (await db.GetDbSet<DispatchOrderEntity>().SingleAsync()).status);
    }

    [Fact]
    public async Task StartWeighingAsync_fails_closed_when_source_capability_is_unsupported()
    {
        await using var db = TestContext.CreateDatabase();
        var source = new MutableSourceReader(BoxTask(101, "CW-101", "BOX-A"));
        var setup = TestContext.CreateService(db, source);
        var order = await setup.CreateAsync(new CreateDispatchOrderRequest
        {
            warehouse_id = 320118,
            source_task_ids = [101]
        }, TestContext.User());
        await MoveToPickedAsync(db, order.id);
        var unsupported = CapabilityFailingSource.Create(source, "cartons_json capability missing");
        var service = new DispatchWorkflowService(
            db, unsupported, new RecordingWarehouseAccess().Contract);

        var exception = await Assert.ThrowsAsync<DispatchWorkflowCommandException>(() =>
            service.StartWeighingAsync(order.id, OrderRequest("capability", 1), TestContext.User()));

        Assert.Equal("SOURCE_BOX_ID_UNSUPPORTED", exception.ErrorCode);
        Assert.Empty(await db.GetDbSet<WeighingBoxEntity>().ToListAsync());
    }

    [Fact]
    public async Task StartWeighingAsync_replay_returns_original_result_without_duplicate_boxes()
    {
        var context = await StartReadyAsync();

        var replay = await context.Service.StartWeighingAsync(
            context.OrderId, OrderRequest("start", 1), TestContext.User());

        Assert.Equal(context.StartResult.row_version, replay.row_version);
        Assert.Equal(context.StartResult.status, replay.status);
        Assert.Single(await context.Db.GetDbSet<WeighingBoxEntity>().ToListAsync());
        await context.Db.DisposeAsync();
    }

    [Fact]
    public async Task StartWeighingAsync_commits_source_change_freeze_and_does_not_materialize_boxes()
    {
        await using var db = TestContext.CreateDatabase();
        var original = BoxTask(101, "CW-101", "BOX-A");
        var source = new MutableSourceReader(original);
        var service = TestContext.CreateService(db, source);
        var order = await service.CreateAsync(new CreateDispatchOrderRequest
        {
            warehouse_id = 320118,
            source_task_ids = [101]
        }, TestContext.User());
        await MoveToPickedAsync(db, order.id);
        source.Set(original with { SourceVersion = "changed-v2" });

        var exception = await Assert.ThrowsAsync<DispatchWorkflowCommandException>(() =>
            service.StartWeighingAsync(order.id, OrderRequest("freeze", 1), TestContext.User()));

        Assert.Equal("SOURCE_CHANGE_PENDING", exception.ErrorCode);
        Assert.True((await db.GetDbSet<DispatchOrderEntity>().SingleAsync()).source_change_pending);
        Assert.Empty(await db.GetDbSet<WeighingBoxEntity>().ToListAsync());
    }

    [Theory]
    [InlineData(0, 2, 3, 4)]
    [InlineData(1, 0, 3, 4)]
    [InlineData(1, 2, 0, 4)]
    [InlineData(1, 2, 3, 0)]
    public async Task SaveWeighingBoxAsync_rejects_non_positive_measurements(
        decimal weight, decimal length, decimal width, decimal height)
    {
        var context = await StartReadyAsync();
        var box = await context.Db.GetDbSet<WeighingBoxEntity>().SingleAsync();

        await Assert.ThrowsAsync<ArgumentException>(() => context.Service.SaveWeighingBoxAsync(
            context.OrderId, box.id,
            new SaveWeighingBoxRequest
            {
                request_id = $"invalid-{weight}-{length}-{width}-{height}",
                row_version = context.StartResult.row_version,
                box_row_version = box.row_version,
                weight = weight,
                length = length,
                width = width,
                height = height
            }, TestContext.User()));

        context.Db.ChangeTracker.Clear();
        Assert.Equal("UNMEASURED",
            (await context.Db.GetDbSet<WeighingBoxEntity>().SingleAsync()).measurement_status);
        await context.Db.DisposeAsync();
    }

    [Fact]
    public async Task SaveWeighingBoxAsync_saves_only_an_existing_active_box_and_replays_original_result()
    {
        var context = await StartReadyAsync();
        var box = await context.Db.GetDbSet<WeighingBoxEntity>().SingleAsync();
        var request = SaveRequest("save", context.StartResult.row_version, box, 1, 2, 3, 4);

        var first = await context.Service.SaveWeighingBoxAsync(
            context.OrderId, box.id, request, TestContext.User());
        var replay = await context.Service.SaveWeighingBoxAsync(
            context.OrderId, box.id, request, TestContext.User());

        Assert.Equal(first.row_version, replay.row_version);
        context.Db.ChangeTracker.Clear();
        var saved = await context.Db.GetDbSet<WeighingBoxEntity>().SingleAsync();
        Assert.Equal((1m, 2m, 3m, 4m), (saved.weight, saved.length, saved.width, saved.height));
        Assert.Equal("MEASURED", saved.measurement_status);
        Assert.Equal(TestContext.User().user_id, saved.measured_by);
        await context.Db.DisposeAsync();
    }

    [Fact]
    public async Task SaveWeighingBoxAsync_rejects_invalidated_foreign_and_stale_boxes()
    {
        var context = await StartReadyAsync();
        var box = await context.Db.GetDbSet<WeighingBoxEntity>().SingleAsync();
        box.is_invalidated = true;
        await context.Db.SaveChangesAsync();

        var invalidated = await Assert.ThrowsAsync<DispatchWorkflowCommandException>(() =>
            context.Service.SaveWeighingBoxAsync(context.OrderId, box.id,
                SaveRequest("invalidated", context.StartResult.row_version, box, 1, 2, 3, 4), TestContext.User()));
        Assert.Equal("BOX_NOT_AVAILABLE", invalidated.ErrorCode);

        box = await context.Db.GetDbSet<WeighingBoxEntity>().SingleAsync();
        box.is_invalidated = false;
        await context.Db.SaveChangesAsync();
        var stale = await Assert.ThrowsAsync<DispatchWorkflowCommandException>(() =>
            context.Service.SaveWeighingBoxAsync(context.OrderId, box.id,
                SaveRequest("stale", context.StartResult.row_version, box, 1, 2, 3, 4) with
                {
                    box_row_version = 99
                }, TestContext.User()));
        Assert.Equal("CONCURRENCY_CONFLICT", stale.ErrorCode);

        var foreign = await Assert.ThrowsAsync<DispatchWorkflowCommandException>(() =>
            context.Service.SaveWeighingBoxAsync(context.OrderId, 999999,
                SaveRequest("foreign", context.StartResult.row_version, box, 1, 2, 3, 4), TestContext.User()));
        Assert.Equal("BOX_NOT_AVAILABLE", foreign.ErrorCode);
        await context.Db.DisposeAsync();
    }

    [Fact]
    public async Task CopyWeighingBoxAsync_copies_within_task_and_target_remains_editable()
    {
        var context = await StartReadyAsync("BOX-A", "BOX-B");
        var boxes = await context.Db.GetDbSet<WeighingBoxEntity>().OrderBy(t => t.id).ToListAsync();
        var saved = await context.Service.SaveWeighingBoxAsync(context.OrderId, boxes[0].id,
            SaveRequest("save-source", context.StartResult.row_version, boxes[0], 1, 2, 3, 4), TestContext.User());
        context.Db.ChangeTracker.Clear();
        boxes = await context.Db.GetDbSet<WeighingBoxEntity>().OrderBy(t => t.id).ToListAsync();

        var copied = await context.Service.CopyWeighingBoxAsync(context.OrderId, boxes[1].id,
            new CopyWeighingBoxRequest
            {
                request_id = "copy",
                row_version = saved.row_version,
                source_box_id = boxes[0].id,
                target_box_row_version = boxes[1].row_version
            }, TestContext.User());
        context.Db.ChangeTracker.Clear();
        var target = await context.Db.GetDbSet<WeighingBoxEntity>().SingleAsync(t => t.id == boxes[1].id);
        Assert.Equal(boxes[0].id, target.copied_from_box_id);
        Assert.Equal((1m, 2m, 3m, 4m), (target.weight, target.length, target.width, target.height));

        await context.Service.SaveWeighingBoxAsync(context.OrderId, target.id,
            SaveRequest("edit-copy", copied.row_version, target, 5, 6, 7, 8), TestContext.User());
        context.Db.ChangeTracker.Clear();
        target = await context.Db.GetDbSet<WeighingBoxEntity>().SingleAsync(t => t.id == boxes[1].id);
        Assert.Equal((5m, 6m, 7m, 8m), (target.weight, target.length, target.width, target.height));
        await context.Db.DisposeAsync();
    }

    [Fact]
    public async Task CopyWeighingBoxAsync_rejects_a_target_from_another_packing_task()
    {
        await using var db = TestContext.CreateDatabase();
        var source = new MutableSourceReader(
            BoxTask(101, "CW-101", "BOX-A"), BoxTask(102, "CW-102", "BOX-B"));
        var service = TestContext.CreateService(db, source);
        var order = await service.CreateAsync(new CreateDispatchOrderRequest
        {
            warehouse_id = 320118,
            source_task_ids = [101, 102]
        }, TestContext.User());
        await MoveToPickedAsync(db, order.id);
        var started = await service.StartWeighingAsync(
            order.id, OrderRequest("start-cross", 1), TestContext.User());
        var boxes = await db.GetDbSet<WeighingBoxEntity>().OrderBy(t => t.id).ToListAsync();
        var saved = await service.SaveWeighingBoxAsync(order.id, boxes[0].id,
            SaveRequest("save-cross-source", started.row_version, boxes[0], 1, 2, 3, 4), TestContext.User());
        db.ChangeTracker.Clear();
        boxes = await db.GetDbSet<WeighingBoxEntity>().OrderBy(t => t.id).ToListAsync();

        var exception = await Assert.ThrowsAsync<DispatchWorkflowCommandException>(() =>
            service.CopyWeighingBoxAsync(order.id, boxes[1].id, new CopyWeighingBoxRequest
            {
                request_id = "copy-cross",
                row_version = saved.row_version,
                source_box_id = boxes[0].id,
                target_box_row_version = boxes[1].row_version
            }, TestContext.User()));

        Assert.Equal("BOX_NOT_AVAILABLE", exception.ErrorCode);
    }

    [Fact]
    public async Task Save_and_copy_with_the_same_client_request_id_have_independent_ledgers()
    {
        var context = await StartReadyAsync("BOX-A", "BOX-B");
        var boxes = await context.Db.GetDbSet<WeighingBoxEntity>().OrderBy(t => t.id).ToListAsync();
        var saved = await context.Service.SaveWeighingBoxAsync(context.OrderId, boxes[0].id,
            SaveRequest("same-request", context.StartResult.row_version, boxes[0], 1, 2, 3, 4), TestContext.User());
        context.Db.ChangeTracker.Clear();
        boxes = await context.Db.GetDbSet<WeighingBoxEntity>().OrderBy(t => t.id).ToListAsync();

        var copied = await context.Service.CopyWeighingBoxAsync(context.OrderId, boxes[1].id,
            new CopyWeighingBoxRequest
            {
                request_id = "same-request",
                row_version = saved.row_version,
                source_box_id = boxes[0].id,
                target_box_row_version = boxes[1].row_version
            }, TestContext.User());

        Assert.Equal("same-request", copied.request_id);
        var operations = await context.Db.GetDbSet<DispatchWorkflowOperationEntity>()
            .Where(t => t.operation == DispatchWorkflowOperation.SaveWeighing
                || t.operation == DispatchWorkflowOperation.CopyWeighing)
            .OrderBy(t => t.operation).ToListAsync();
        Assert.Equal(2, operations.Count);
        Assert.NotEqual(operations[0].request_id, operations[1].request_id);
        await context.Db.DisposeAsync();
    }

    [Fact]
    public async Task Complete_two_tasks_with_same_client_request_id_does_not_replay_the_wrong_task()
    {
        await using var db = TestContext.CreateDatabase();
        var source = new MutableSourceReader(
            BoxTask(101, "CW-101", "BOX-A"), BoxTask(102, "CW-102", "BOX-B"));
        var service = TestContext.CreateService(db, source);
        var order = await service.CreateAsync(new CreateDispatchOrderRequest
        {
            warehouse_id = 320118,
            source_task_ids = [101, 102]
        }, TestContext.User());
        await MoveToPickedAsync(db, order.id);
        var result = await service.StartWeighingAsync(
            order.id, OrderRequest("start-two", 1), TestContext.User());
        var tasks = await db.GetDbSet<DispatchPackingTaskEntity>().OrderBy(t => t.id).ToListAsync();
        var boxes = await db.GetDbSet<WeighingBoxEntity>().OrderBy(t => t.packing_task_id).ToListAsync();
        for (var index = 0; index < boxes.Count; index++)
        {
            result = await service.SaveWeighingBoxAsync(order.id, boxes[index].id,
                SaveRequest($"save-{index}", result.row_version, boxes[index], 1, 2, 3, 4), TestContext.User());
            db.ChangeTracker.Clear();
            boxes = await db.GetDbSet<WeighingBoxEntity>().OrderBy(t => t.packing_task_id).ToListAsync();
        }

        result = await service.CompleteTaskWeighingAsync(order.id, tasks[0].id,
            OrderRequest("same-task-request", result.row_version), TestContext.User());
        result = await service.CompleteTaskWeighingAsync(order.id, tasks[1].id,
            OrderRequest("same-task-request", result.row_version), TestContext.User());

        db.ChangeTracker.Clear();
        Assert.All(await db.GetDbSet<DispatchPackingTaskEntity>().ToListAsync(),
            task => Assert.Equal(DispatchOrderStatus.PendingOutbound, task.status));
        Assert.Equal(2, await db.GetDbSet<DispatchWorkflowOperationEntity>()
            .CountAsync(t => t.operation == DispatchWorkflowOperation.CompleteTaskWeighing));
    }

    [Theory]
    [InlineData("CANCEL")]
    [InlineData("ADD")]
    [InlineData("DELETE")]
    public async Task Start_after_human_continue_materializes_only_the_picked_WMS_carton_snapshot(string change)
    {
        await using var db = TestContext.CreateDatabase();
        var original = BoxTask(101, "CW-101", "BOX-A");
        var source = new MutableSourceReader(original);
        var service = TestContext.CreateService(db, source);
        var order = await service.CreateAsync(new CreateDispatchOrderRequest
        {
            warehouse_id = 320118,
            source_task_ids = [101]
        }, TestContext.User());
        await MoveToPickedAsync(db, order.id);
        var changed = change switch
        {
            "CANCEL" => original with { SourceVersion = "v2-cancel", IsCancelled = true },
            "ADD" => BoxTask(101, "CW-101", "BOX-A", "BOX-B") with { SourceVersion = "v2-add" },
            _ => original with { SourceVersion = "v2-delete", Boxes = [], CartonsJson = "[]" }
        };
        source.Set(changed);
        var frozen = await service.EnsurePostPickSourceCurrentAsync(order.id, TestContext.User());
        Assert.True(frozen.source_change_pending);
        var decision = await service.DecideSourceChangeAsync(order.id, new SourceDecisionRequest
        {
            decision = "CONTINUE",
            source_version = frozen.source_version,
            reason = "按拣货完成时的WMS事实继续",
            request_id = $"continue-{change}",
            row_version = frozen.row_version
        }, TestContext.User());

        await service.StartWeighingAsync(order.id,
            OrderRequest($"start-{change}", decision.row_version), TestContext.User());

        var box = Assert.Single(await db.GetDbSet<WeighingBoxEntity>().ToListAsync());
        Assert.Equal("BOX-A", box.source_box_identity);
    }

    [Fact]
    public async Task StartWeighing_reads_source_only_in_guard_and_has_no_second_source_window()
    {
        await using var db = TestContext.CreateDatabase();
        var source = new MutableSourceReader(BoxTask(101, "CW-101", "BOX-A"));
        var service = TestContext.CreateService(db, source);
        var order = await service.CreateAsync(new CreateDispatchOrderRequest
        {
            warehouse_id = 320118,
            source_task_ids = [101]
        }, TestContext.User());
        await MoveToPickedAsync(db, order.id);
        source.BeforeRead = _ => { };
        var before = source.ReadCount;

        await service.StartWeighingAsync(order.id,
            OrderRequest("single-source-read", 1), TestContext.User());

        Assert.Equal(before + 1, source.ReadCount);
    }

    [Theory]
    [InlineData(" leading")]
    [InlineData("trailing ")]
    [InlineData(" ")]
    public async Task Weighing_commands_reject_noncanonical_request_ids(string requestId)
    {
        var context = await StartReadyAsync();
        var box = await context.Db.GetDbSet<WeighingBoxEntity>().SingleAsync();

        await Assert.ThrowsAsync<ArgumentException>(() => context.Service.SaveWeighingBoxAsync(
            context.OrderId, box.id,
            SaveRequest(requestId, context.StartResult.row_version, box, 1, 2, 3, 4), TestContext.User()));

        await context.Db.DisposeAsync();
    }

    [Fact]
    public async Task CompleteTaskAndOrderWeighing_require_every_active_box_then_move_whole_order()
    {
        var context = await StartReadyAsync("BOX-A", "BOX-B");
        var task = await context.Db.GetDbSet<DispatchPackingTaskEntity>().SingleAsync();
        var boxes = await context.Db.GetDbSet<WeighingBoxEntity>().OrderBy(t => t.id).ToListAsync();

        var incomplete = await Assert.ThrowsAsync<DispatchWorkflowCommandException>(() =>
            context.Service.CompleteTaskWeighingAsync(context.OrderId, task.id,
                OrderRequest("task-incomplete", context.StartResult.row_version), TestContext.User()));
        Assert.Equal("WEIGHING_INCOMPLETE", incomplete.ErrorCode);

        var first = await context.Service.SaveWeighingBoxAsync(context.OrderId, boxes[0].id,
            SaveRequest("box-1", context.StartResult.row_version, boxes[0], 1, 2, 3, 4), TestContext.User());
        context.Db.ChangeTracker.Clear();
        boxes = await context.Db.GetDbSet<WeighingBoxEntity>().OrderBy(t => t.id).ToListAsync();
        var second = await context.Service.SaveWeighingBoxAsync(context.OrderId, boxes[1].id,
            SaveRequest("box-2", first.row_version, boxes[1], 5, 6, 7, 8), TestContext.User());
        var taskResult = await context.Service.CompleteTaskWeighingAsync(context.OrderId, task.id,
            OrderRequest("task-complete", second.row_version), TestContext.User());
        var orderResult = await context.Service.CompleteOrderWeighingAsync(context.OrderId,
            OrderRequest("order-complete", taskResult.row_version), TestContext.User());

        Assert.Equal("PENDING_OUTBOUND", orderResult.status);
        context.Db.ChangeTracker.Clear();
        Assert.Equal(DispatchOrderStatus.PendingOutbound,
            (await context.Db.GetDbSet<DispatchOrderEntity>().SingleAsync()).status);
        Assert.Equal(2, (await context.Db.GetDbSet<DispatchPackingTaskEntity>().SingleAsync()).measured_box_count);
        await context.Db.DisposeAsync();
    }

    [Fact]
    public async Task GetTaskBoxesAsync_checks_order_task_ownership_and_warehouse_permission()
    {
        var access = new RecordingWarehouseAccess();
        var context = await StartReadyAsync(access: access);
        var task = await context.Db.GetDbSet<DispatchPackingTaskEntity>().SingleAsync();

        var boxes = await context.Service.GetTaskBoxesAsync(
            context.OrderId, task.id, TestContext.User());

        Assert.Single(boxes);
        Assert.Contains(320118, access.CheckedWarehouseIds);
        await Assert.ThrowsAsync<KeyNotFoundException>(() => context.Service.GetTaskBoxesAsync(
            context.OrderId, 999999, TestContext.User()));
        await context.Db.DisposeAsync();
    }

    [Theory]
    [InlineData(nameof(DispatchWorkflowController.StartWeighingAsync))]
    [InlineData(nameof(DispatchWorkflowController.SaveWeighingBoxAsync))]
    [InlineData(nameof(DispatchWorkflowController.CopyWeighingBoxAsync))]
    [InlineData(nameof(DispatchWorkflowController.CompleteTaskWeighingAsync))]
    [InlineData(nameof(DispatchWorkflowController.CompleteOrderWeighingAsync))]
    public void Weighing_mutation_endpoints_have_explicit_authorization(string methodName)
    {
        var method = typeof(DispatchWorkflowController).GetMethod(methodName);

        Assert.NotNull(method);
        Assert.NotNull(method!.GetCustomAttribute<AuthorizeAttribute>());
    }

    [Fact]
    public async Task StartWeighing_endpoint_maps_domain_conflict_to_409()
    {
        var exception = DispatchWorkflowCommandException.WeighingIncomplete("incomplete");
        var controller = new DispatchWorkflowController(
            WeighingThrowingWorkflow.Create(exception), ThrowingQuery.Create(exception));

        var response = await controller.StartWeighingAsync(
            1, OrderRequest("request", 0), CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(response.Result);
        Assert.Equal(409, objectResult.StatusCode);
        Assert.Equal("WEIGHING_INCOMPLETE",
            Assert.IsType<ModernWMS.Core.Models.ResultModel<WeighingCommandResult>>(objectResult.Value).ErrorMessage);
    }

    [Fact]
    public void Weighing_box_routes_use_resource_ids_and_do_not_repeat_target_ids_in_bodies()
    {
        var save = typeof(DispatchWorkflowController).GetMethod(
            nameof(DispatchWorkflowController.SaveWeighingBoxAsync))!;
        var copy = typeof(DispatchWorkflowController).GetMethod(
            nameof(DispatchWorkflowController.CopyWeighingBoxAsync))!;

        Assert.Equal("{id:int}/boxes/{boxId:int}",
            save.GetCustomAttribute<Microsoft.AspNetCore.Mvc.HttpPutAttribute>()!.Template);
        Assert.Equal("{id:int}/boxes/{targetBoxId:int}/copy",
            copy.GetCustomAttribute<Microsoft.AspNetCore.Mvc.HttpPostAttribute>()!.Template);
        Assert.DoesNotContain(typeof(SaveWeighingBoxRequest).GetProperties(), p => p.Name == "box_id");
        Assert.DoesNotContain(typeof(CopyWeighingBoxRequest).GetProperties(), p => p.Name == "target_box_id");
    }

    private static async Task MoveToPickedAsync(ModernWMS.Core.DBContext.SqlDBContext db, int orderId)
    {
        var order = await db.GetDbSet<DispatchOrderEntity>()
            .Include(t => t.packing_tasks)
            .SingleAsync(t => t.id == orderId);
        order.status = DispatchOrderStatus.Picked;
        order.row_version = 1;
        foreach (var task in order.packing_tasks)
        {
            task.status = DispatchOrderStatus.Picked;
        }
        await db.SaveChangesAsync();
    }

    private static WeighingOrderCommandRequest OrderRequest(string requestId, long rowVersion) => new()
    {
        request_id = requestId,
        row_version = rowVersion
    };

    private static SaveWeighingBoxRequest SaveRequest(
        string requestId,
        long orderRowVersion,
        WeighingBoxEntity box,
        decimal weight,
        decimal length,
        decimal width,
        decimal height) => new()
        {
            request_id = requestId,
            row_version = orderRowVersion,
            box_row_version = box.row_version,
            weight = weight,
            length = length,
            width = width,
            height = height
        };

    private static ModernWMS.WMS.Entities.ViewModels.PackingTask.PackingTaskSourceSnapshot BoxTask(
        long taskId,
        string taskNo,
        params string[] boxIds)
    {
        var task = TestContext.Task(taskId, taskNo, 320118, TestContext.Item(taskId * 10, "SKU", 1));
        return task with
        {
            Boxes = boxIds.Select((id, index) =>
                new ModernWMS.WMS.Entities.ViewModels.PackingTask.SellFoxSourceBox(
                    id, index + 1, $"{{\"boxId\":\"{id}\"}}"))
                .ToList(),
            CartonsJson = "[" + string.Join(",", boxIds.Select(id => $"{{\"boxId\":\"{id}\"}}")) + "]"
        };
    }

    private static async Task<WeighingContext> StartReadyAsync(
        string firstBox = "BOX-A",
        string? secondBox = null,
        RecordingWarehouseAccess? access = null)
    {
        var db = TestContext.CreateDatabase();
        var ids = secondBox == null ? new[] { firstBox } : new[] { firstBox, secondBox };
        var source = new MutableSourceReader(BoxTask(101, "CW-101", ids));
        var service = TestContext.CreateService(db, source, access);
        var order = await service.CreateAsync(new CreateDispatchOrderRequest
        {
            warehouse_id = 320118,
            source_task_ids = [101]
        }, TestContext.User());
        await MoveToPickedAsync(db, order.id);
        var started = await service.StartWeighingAsync(
            order.id, OrderRequest("start", 1), TestContext.User());
        return new WeighingContext(db, service, source, order.id, started);
    }

    private sealed record WeighingContext(
        ModernWMS.Core.DBContext.SqlDBContext Db,
        ModernWMS.WMS.Services.DispatchWorkflow.DispatchWorkflowService Service,
        MutableSourceReader Source,
        int OrderId,
        WeighingCommandResult StartResult);
}

internal sealed class WeighingThrowingWorkflow
{
    public static IDispatchWorkflowService Create(Exception exception)
    {
        var contract = DispatchProxy.Create<IDispatchWorkflowService, WeighingThrowingWorkflowProxy>();
        ((WeighingThrowingWorkflowProxy)(object)contract).Exception = exception;
        return contract;
    }
}

public class WeighingThrowingWorkflowProxy : DispatchProxy
{
    internal Exception Exception { get; set; } = null!;

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => targetMethod?.Name switch
    {
        nameof(IDispatchWorkflowService.StartWeighingAsync) =>
            Task.FromException<WeighingCommandResult>(Exception),
        _ => throw new NotSupportedException(targetMethod?.Name)
    };
}

internal sealed class CapabilityFailingSource
{
    public static IPackingTaskSourceReader Create(MutableSourceReader source, string error)
    {
        var contract = DispatchProxy.Create<IPackingTaskSourceReader, CapabilityFailingSourceProxy>();
        var proxy = (CapabilityFailingSourceProxy)(object)contract;
        proxy.Source = source;
        proxy.Error = error;
        return contract;
    }
}

public class CapabilityFailingSourceProxy : DispatchProxy
{
    internal MutableSourceReader Source { get; set; } = null!;
    internal string Error { get; set; } = string.Empty;

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => targetMethod?.Name switch
    {
        nameof(IPackingTaskSourceReader.VerifyCapabilityAsync) =>
            Task.FromResult(new PackingTaskSourceCapability(false, Error)),
        nameof(IPackingTaskSourceReader.ReadAsync) => Source.ReadAsync(
            (IReadOnlyCollection<long>)args![0]!,
            (CancellationToken)(args[1] ?? default(CancellationToken))),
        _ => throw new NotSupportedException(targetMethod?.Name)
    };
}
