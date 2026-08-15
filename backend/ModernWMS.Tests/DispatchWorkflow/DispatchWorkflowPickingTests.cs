using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Reflection;
using ModernWMS.Core.Models;
using ModernWMS.WMS.Controllers.DispatchWorkflow;
using ModernWMS.WMS.Entities.Models;
using ModernWMS.WMS.Entities.ViewModels.DispatchWorkflow;
using ModernWMS.WMS.IServices.DispatchWorkflow;
using ModernWMS.WMS.Services.DispatchWorkflow;
using MySql.Data.MySqlClient;

namespace ModernWMS.Tests.DispatchWorkflow;

public sealed class DispatchWorkflowPickingTests
{
    [Fact]
    public async Task CompletePickingAsync_keeps_equal_skus_in_different_tasks_as_separate_allocations()
    {
        await using var db = TestContext.CreateDatabase();
        var source = new MutableSourceReader(
            TestContext.Task(101, "CW-101", 320118, TestContext.Item(1001, "SAME-SKU", 2)),
            TestContext.Task(102, "CW-102", 320118, TestContext.Item(1002, "SAME-SKU", 3)));
        var service = TestContext.CreateService(db, source);
        var created = await service.CreateAsync(new CreateDispatchOrderRequest
        {
            warehouse_id = 320118,
            source_task_ids = [101, 102]
        }, TestContext.User());

        var items = await db.GetDbSet<DispatchPackingTaskItemEntity>().OrderBy(t => t.id).ToListAsync();
        items.ForEach(t => t.wms_sku_id = 10);
        await SeedInventoryAsync(db, 320118, 10, 5);

        var result = await service.CompletePickingAsync(created.id, new CompletePickingRequest
        {
            request_id = "pick-request-1",
            row_version = 0
        }, TestContext.User());

        Assert.Equal("PICKED", result.status);
        var allocations = await db.GetDbSet<DispatchpicklistEntity>()
            .OrderBy(t => t.packing_task_item_id)
            .ToListAsync();
        Assert.Equal(2, allocations.Count);
        Assert.Equal([2, 3], allocations.Select(t => t.pick_qty).ToArray());
        Assert.Equal(items.Select(t => (int?)t.id), allocations.Select(t => t.packing_task_item_id));
    }

    [Fact]
    public async Task CompletePickingAsync_reconciles_latest_source_quantity_before_allocating()
    {
        await using var db = TestContext.CreateDatabase();
        var source = new MutableSourceReader(
            TestContext.Task(101, "CW-101", 320118, TestContext.Item(1001, "SKU-1", 2)));
        var service = TestContext.CreateService(db, source);
        var created = await service.CreateAsync(new CreateDispatchOrderRequest
        {
            warehouse_id = 320118,
            source_task_ids = [101]
        }, TestContext.User());
        var item = await db.GetDbSet<DispatchPackingTaskItemEntity>().SingleAsync();
        item.wms_sku_id = 10;
        await SeedInventoryAsync(db, 320118, 10, 4);
        source.Set(TestContext.Task(101, "CW-101", 320118, TestContext.Item(1001, "SKU-1", 4)));

        await service.CompletePickingAsync(created.id, Request("latest", 0), TestContext.User());

        var allocation = await db.GetDbSet<DispatchpicklistEntity>().SingleAsync();
        Assert.Equal(4, allocation.pick_qty);
        Assert.Equal(4, (await db.GetDbSet<DispatchPackingTaskItemEntity>().SingleAsync()).required_qty);
    }

    [Fact]
    public async Task CompletePickingAsync_rolls_back_every_allocation_when_any_item_is_short()
    {
        await using var db = TestContext.CreateDatabase();
        var source = new MutableSourceReader(
            TestContext.Task(101, "CW-101", 320118,
                TestContext.Item(1001, "SKU-1", 2),
                TestContext.Item(1002, "SKU-2", 3)));
        var service = TestContext.CreateService(db, source);
        var created = await service.CreateAsync(new CreateDispatchOrderRequest
        {
            warehouse_id = 320118,
            source_task_ids = [101]
        }, TestContext.User());
        var items = await db.GetDbSet<DispatchPackingTaskItemEntity>().OrderBy(t => t.source_item_id).ToListAsync();
        items[0].wms_sku_id = 10;
        items[1].wms_sku_id = 20;
        await SeedInventoryAsync(db, 320118, 10, 2);
        db.GetDbSet<StockEntity>().Add(new StockEntity
        {
            id = 22,
            sku_id = 20,
            goods_location_id = 11,
            goods_owner_id = 31,
            qty = 2,
            tenant_id = 88
        });
        await db.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<DispatchWorkflowCommandException>(() =>
            service.CompletePickingAsync(created.id, Request("short", 0), TestContext.User()));

        Assert.Equal("STOCK_SHORTAGE", exception.ErrorCode);
        Assert.Empty(await db.GetDbSet<DispatchlistEntity>().ToListAsync());
        Assert.Empty(await db.GetDbSet<DispatchpicklistEntity>().ToListAsync());
        Assert.Equal(DispatchOrderStatus.PendingPick,
            (await db.GetDbSet<DispatchOrderEntity>().SingleAsync()).status);
    }

    [Fact]
    public async Task CompletePickingAsync_returns_the_persisted_result_for_a_repeated_request()
    {
        await using var db = TestContext.CreateDatabase();
        var source = new MutableSourceReader(
            TestContext.Task(101, "CW-101", 320118, TestContext.Item(1001, "SKU-1", 2)));
        var service = TestContext.CreateService(db, source);
        var created = await service.CreateAsync(new CreateDispatchOrderRequest
        {
            warehouse_id = 320118,
            source_task_ids = [101]
        }, TestContext.User());
        (await db.GetDbSet<DispatchPackingTaskItemEntity>().SingleAsync()).wms_sku_id = 10;
        await SeedInventoryAsync(db, 320118, 10, 2);

        var first = await service.CompletePickingAsync(created.id, Request("same-request", 0), TestContext.User());
        var retry = await service.CompletePickingAsync(created.id, Request("same-request", 0), TestContext.User());

        Assert.Equal(first.row_version, retry.row_version);
        Assert.Equal(first.status, retry.status);
        Assert.Single(await db.GetDbSet<DispatchWorkflowOperationEntity>().ToListAsync());
        Assert.Single(await db.GetDbSet<DispatchlistEntity>().ToListAsync());
        Assert.Single(await db.GetDbSet<DispatchpicklistEntity>().ToListAsync());
    }

    [Fact]
    public async Task CompletePickingAsync_replay_returns_first_result_after_order_moves_to_a_later_status()
    {
        await using var db = TestContext.CreateDatabase();
        var source = new MutableSourceReader(
            TestContext.Task(101, "CW-101", 320118, TestContext.Item(1001, "SKU-1", 1)));
        var service = TestContext.CreateService(db, source);
        var created = await service.CreateAsync(new CreateDispatchOrderRequest
        {
            warehouse_id = 320118,
            source_task_ids = [101]
        }, TestContext.User());
        (await db.GetDbSet<DispatchPackingTaskItemEntity>().SingleAsync()).wms_sku_id = 10;
        await SeedInventoryAsync(db, 320118, 10, 1);
        var first = await service.CompletePickingAsync(created.id, Request("replay", 0), TestContext.User());
        var order = await db.GetDbSet<DispatchOrderEntity>().SingleAsync();
        order.status = DispatchOrderStatus.Weighing;
        order.row_version++;
        await db.SaveChangesAsync();

        var replay = await service.CompletePickingAsync(created.id, Request("replay", 0), TestContext.User());

        Assert.Equal("PICKED", replay.status);
        Assert.Equal(first.row_version, replay.row_version);
    }

    [Fact]
    public async Task CompletePickingAsync_rejects_source_change_during_commit_and_keeps_order_pending()
    {
        await using var db = TestContext.CreateDatabase();
        var source = new MutableSourceReader(
            TestContext.Task(101, "CW-101", 320118, TestContext.Item(1001, "SKU-1", 2)));
        var service = TestContext.CreateService(db, source);
        var created = await service.CreateAsync(new CreateDispatchOrderRequest
        {
            warehouse_id = 320118,
            source_task_ids = [101]
        }, TestContext.User());
        (await db.GetDbSet<DispatchPackingTaskItemEntity>().SingleAsync()).wms_sku_id = 10;
        await SeedInventoryAsync(db, 320118, 10, 3);
        var readCountBeforePicking = source.ReadCount;
        source.BeforeRead = readCount =>
        {
            if (readCount == readCountBeforePicking + 2)
            {
                source.Set(TestContext.Task(101, "CW-101", 320118, TestContext.Item(1001, "SKU-1", 3)));
            }
        };

        var exception = await Assert.ThrowsAsync<DispatchWorkflowCommandException>(() =>
            service.CompletePickingAsync(created.id, Request("source-race", 0), TestContext.User()));

        Assert.Equal("SOURCE_CHANGED", exception.ErrorCode);
        Assert.Empty(await db.GetDbSet<DispatchpicklistEntity>().ToListAsync());
        Assert.Equal(DispatchOrderStatus.PendingPick,
            (await db.GetDbSet<DispatchOrderEntity>().SingleAsync()).status);
    }

    [Fact]
    public async Task CompletePickingAsync_rejects_wrong_row_version_before_allocating()
    {
        await using var db = TestContext.CreateDatabase();
        var source = new MutableSourceReader(
            TestContext.Task(101, "CW-101", 320118, TestContext.Item(1001, "SKU-1", 1)));
        var service = TestContext.CreateService(db, source);
        var created = await service.CreateAsync(new CreateDispatchOrderRequest
        {
            warehouse_id = 320118,
            source_task_ids = [101]
        }, TestContext.User());

        var exception = await Assert.ThrowsAsync<DispatchWorkflowCommandException>(() =>
            service.CompletePickingAsync(created.id, Request("stale", 99), TestContext.User()));

        Assert.Equal("CONCURRENCY_CONFLICT", exception.ErrorCode);
        Assert.Empty(await db.GetDbSet<DispatchpicklistEntity>().ToListAsync());
    }

    [Fact]
    public async Task CompletePickingAsync_does_not_ledger_a_shortage_and_allows_same_request_to_retry()
    {
        await using var db = TestContext.CreateDatabase();
        var source = new MutableSourceReader(
            TestContext.Task(101, "CW-101", 320118, TestContext.Item(1001, "SKU-1", 2)));
        var service = TestContext.CreateService(db, source);
        var created = await service.CreateAsync(new CreateDispatchOrderRequest
        {
            warehouse_id = 320118,
            source_task_ids = [101]
        }, TestContext.User());
        (await db.GetDbSet<DispatchPackingTaskItemEntity>().SingleAsync()).wms_sku_id = 10;
        await SeedInventoryAsync(db, 320118, 10, 1);

        await Assert.ThrowsAsync<DispatchWorkflowCommandException>(() =>
            service.CompletePickingAsync(created.id, Request("retry-shortage", 0), TestContext.User()));
        Assert.Empty(await db.GetDbSet<DispatchWorkflowOperationEntity>().ToListAsync());
        (await db.GetDbSet<StockEntity>().SingleAsync()).qty = 2;
        await db.SaveChangesAsync();

        var retry = await service.CompletePickingAsync(
            created.id, Request("retry-shortage", 0), TestContext.User());

        Assert.Equal("PICKED", retry.status);
        Assert.Single(await db.GetDbSet<DispatchWorkflowOperationEntity>().ToListAsync());
    }

    [Fact]
    public async Task CompletePickingAsync_fails_closed_when_one_sku_matches_multiple_goods_owners()
    {
        await using var db = TestContext.CreateDatabase();
        var source = new MutableSourceReader(
            TestContext.Task(101, "CW-101", 320118, TestContext.Item(1001, "SKU-1", 1)));
        var service = TestContext.CreateService(db, source);
        var created = await service.CreateAsync(new CreateDispatchOrderRequest
        {
            warehouse_id = 320118,
            source_task_ids = [101]
        }, TestContext.User());
        (await db.GetDbSet<DispatchPackingTaskItemEntity>().SingleAsync()).wms_sku_id = 10;
        await SeedInventoryAsync(db, 320118, 10, 1);
        db.GetDbSet<StockEntity>().Add(new StockEntity
        {
            id = 22,
            sku_id = 10,
            goods_location_id = 11,
            goods_owner_id = 32,
            qty = 1,
            tenant_id = 88
        });
        await db.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<DispatchWorkflowCommandException>(() =>
            service.CompletePickingAsync(created.id, Request("ambiguous-owner", 0), TestContext.User()));

        Assert.Equal("STOCK_SHORTAGE", exception.ErrorCode);
        Assert.Empty(await db.GetDbSet<DispatchpicklistEntity>().ToListAsync());
    }

    [Theory]
    [InlineData(1062, "23000")]
    [InlineData(1213, "40001")]
    [InlineData(1205, "HY000")]
    [InlineData(9999, "40001")]
    public async Task CompletePickingAsync_concurrent_database_conflict_returns_the_winning_ledger_result(
        int errorNumber,
        string sqlState)
    {
        var databaseName = Guid.NewGuid().ToString();
        var databaseRoot = new InMemoryDatabaseRoot();
        var options = new DbContextOptionsBuilder<ModernWMS.Core.DBContext.SqlDBContext>()
            .UseInMemoryDatabase(databaseName, databaseRoot)
            .Options;
        var source = new MutableSourceReader(
            TestContext.Task(101, "CW-101", 320118, TestContext.Item(1001, "SKU-1", 1)));
        int orderId;
        await using (var setupDb = new ModernWMS.Core.DBContext.SqlDBContext(options))
        {
            var setupService = TestContext.CreateService(setupDb, source);
            var created = await setupService.CreateAsync(new CreateDispatchOrderRequest
            {
                warehouse_id = 320118,
                source_task_ids = [101]
            }, TestContext.User());
            orderId = created.id;
            (await setupDb.GetDbSet<DispatchPackingTaskItemEntity>().SingleAsync()).wms_sku_id = 10;
            await SeedInventoryAsync(setupDb, 320118, 10, 1);
        }

        await using var conflictingDb = new SaveConflictSqlDbContext(options);
        conflictingDb.ConflictException = new DbUpdateException(
            "concurrent operation",
            CreateMySqlException(errorNumber, sqlState));
        conflictingDb.BeforeConflict = async () =>
        {
            await using var winnerDb = new ModernWMS.Core.DBContext.SqlDBContext(options);
            var winnerOrder = await winnerDb.GetDbSet<DispatchOrderEntity>().SingleAsync(t => t.id == orderId);
            winnerOrder.status = DispatchOrderStatus.Picked;
            winnerOrder.row_version = 42;
            winnerDb.GetDbSet<DispatchWorkflowOperationEntity>().Add(new DispatchWorkflowOperationEntity
            {
                dispatch_order_id = orderId,
                operation = DispatchWorkflowOperation.CompletePicking,
                request_id = "concurrent",
                result_status = DispatchWorkflowOperationResultStatus.Succeeded,
                result_order_status = DispatchOrderStatus.Picked,
                result_row_version = 42,
                create_operator = 7,
                create_operator_name = "winner",
                create_time = DateTime.Now
            });
            await winnerDb.SaveChangesAsync();
        };
        var service = TestContext.CreateService(conflictingDb, source);

        var result = await service.CompletePickingAsync(
            orderId, Request("concurrent", 0), TestContext.User());

        Assert.Equal("PICKED", result.status);
        Assert.Equal(42, result.row_version);
    }

    [Fact]
    public async Task CompletePickingAsync_preserves_non_concurrency_database_errors()
    {
        var databaseName = Guid.NewGuid().ToString();
        var databaseRoot = new InMemoryDatabaseRoot();
        var options = new DbContextOptionsBuilder<ModernWMS.Core.DBContext.SqlDBContext>()
            .UseInMemoryDatabase(databaseName, databaseRoot)
            .Options;
        var source = new MutableSourceReader(
            TestContext.Task(101, "CW-101", 320118, TestContext.Item(1001, "SKU-1", 1)));
        int orderId;
        await using (var setupDb = new ModernWMS.Core.DBContext.SqlDBContext(options))
        {
            var setupService = TestContext.CreateService(setupDb, source);
            orderId = (await setupService.CreateAsync(new CreateDispatchOrderRequest
            {
                warehouse_id = 320118,
                source_task_ids = [101]
            }, TestContext.User())).id;
            (await setupDb.GetDbSet<DispatchPackingTaskItemEntity>().SingleAsync()).wms_sku_id = 10;
            await SeedInventoryAsync(setupDb, 320118, 10, 1);
        }

        var databaseError = new DbUpdateException(
            "foreign key failed",
            CreateMySqlException(1452, "23000"));
        await using var failingDb = new SaveConflictSqlDbContext(options)
        {
            ConflictException = databaseError,
            BeforeConflict = () => Task.CompletedTask
        };
        var service = TestContext.CreateService(failingDb, source);

        var thrown = await Assert.ThrowsAsync<DbUpdateException>(() =>
            service.CompletePickingAsync(orderId, Request("not-concurrency", 0), TestContext.User()));

        Assert.Same(databaseError, thrown);
    }

    [Fact]
    public async Task Pending_pick_page_returns_the_current_row_version_for_commands()
    {
        await using var db = TestContext.CreateDatabase();
        var source = new MutableSourceReader(
            TestContext.Task(101, "CW-101", 320118, TestContext.Item(1001, "SKU-1", 1)));
        var access = new RecordingWarehouseAccess();
        var workflow = TestContext.CreateService(db, source, access);
        await workflow.CreateAsync(new CreateDispatchOrderRequest
        {
            warehouse_id = 320118,
            source_task_ids = [101]
        }, TestContext.User());
        var order = await db.GetDbSet<DispatchOrderEntity>().SingleAsync();
        order.row_version = 7;
        await db.SaveChangesAsync();
        var query = new DispatchOrderQueryService(db, access.Contract, workflow);

        var page = await query.PageAsync(new DispatchOrderPageRequest
        {
            warehouse_id = 320118,
            status = "PENDING_PICK"
        }, TestContext.User());

        Assert.Equal(7, Assert.Single(page.Data).row_version);
    }

    [Fact]
    public async Task CompletePickingAsync_rejects_a_new_request_after_order_left_pending_pick()
    {
        await using var db = TestContext.CreateDatabase();
        var source = new MutableSourceReader(
            TestContext.Task(101, "CW-101", 320118, TestContext.Item(1001, "SKU-1", 1)));
        var service = TestContext.CreateService(db, source);
        var created = await service.CreateAsync(new CreateDispatchOrderRequest
        {
            warehouse_id = 320118,
            source_task_ids = [101]
        }, TestContext.User());
        (await db.GetDbSet<DispatchPackingTaskItemEntity>().SingleAsync()).wms_sku_id = 10;
        await SeedInventoryAsync(db, 320118, 10, 1);
        var first = await service.CompletePickingAsync(created.id, Request("first", 0), TestContext.User());

        var exception = await Assert.ThrowsAsync<DispatchWorkflowCommandException>(() =>
            service.CompletePickingAsync(created.id, Request("second", first.row_version), TestContext.User()));

        Assert.Equal("STATUS_NOT_ALLOWED", exception.ErrorCode);
    }

    [Fact]
    public async Task CompletePickingAsync_checks_warehouse_permission_before_reading_inventory()
    {
        await using var db = TestContext.CreateDatabase();
        var source = new MutableSourceReader(
            TestContext.Task(101, "CW-101", 320118, TestContext.Item(1001, "SKU-1", 1)));
        var createService = TestContext.CreateService(db, source);
        var created = await createService.CreateAsync(new CreateDispatchOrderRequest
        {
            warehouse_id = 320118,
            source_task_ids = [101]
        }, TestContext.User());
        var service = new DispatchWorkflowService(db, source.Contract, DenyingWarehouseAccess.Create());

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.CompletePickingAsync(created.id, Request("denied", 0), TestContext.User()));

        Assert.Empty(await db.GetDbSet<DispatchpicklistEntity>().ToListAsync());
    }

    [Fact]
    public async Task CompletePickingAsync_removes_an_order_from_pending_when_all_source_tasks_are_cancelled()
    {
        await using var db = TestContext.CreateDatabase();
        var original = TestContext.Task(101, "CW-101", 320118, TestContext.Item(1001, "SKU-1", 1));
        var source = new MutableSourceReader(original);
        var service = TestContext.CreateService(db, source);
        var created = await service.CreateAsync(new CreateDispatchOrderRequest
        {
            warehouse_id = 320118,
            source_task_ids = [101]
        }, TestContext.User());
        source.Set(original with { SourceVersion = "cancelled-v2", IsCancelled = true });

        var result = await service.CompletePickingAsync(
            created.id, Request("cancelled", 0), TestContext.User());

        Assert.Equal("SOURCE_CANCELLED", result.status);
        Assert.Equal(DispatchOrderStatus.SourceCancelled,
            (await db.GetDbSet<DispatchOrderEntity>().SingleAsync()).status);
        Assert.False((await db.GetDbSet<DispatchPackingTaskEntity>().SingleAsync()).is_active);
        Assert.Empty(await db.GetDbSet<DispatchpicklistEntity>().ToListAsync());
    }

    [Theory]
    [MemberData(nameof(CommandErrorCases))]
    public async Task CompletePicking_endpoint_maps_command_errors_to_conflict(
        DispatchWorkflowCommandException exception,
        string expectedCode)
    {
        var controller = new DispatchWorkflowController(
            CompletePickingThrowingWorkflow.Create(exception),
            ThrowingQuery.Create(exception));

        var response = await controller.CompletePickingAsync(
            1, Request("request", 0), CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(response.Result);
        Assert.Equal(409, objectResult.StatusCode);
        var body = Assert.IsType<ResultModel<CompletePickingResult>>(objectResult.Value);
        Assert.Equal(expectedCode, body.ErrorMessage);
    }

    [Fact]
    public void CompletePicking_endpoint_has_explicit_authorization()
    {
        var method = typeof(DispatchWorkflowController).GetMethod(
            nameof(DispatchWorkflowController.CompletePickingAsync));

        Assert.NotNull(method!.GetCustomAttribute<AuthorizeAttribute>());
    }

    public static TheoryData<DispatchWorkflowCommandException, string> CommandErrorCases => new()
    {
        { DispatchWorkflowCommandException.SourceChanged(), "SOURCE_CHANGED" },
        { DispatchWorkflowCommandException.StockShortage("short"), "STOCK_SHORTAGE" },
        { DispatchWorkflowCommandException.ConcurrencyConflict(), "CONCURRENCY_CONFLICT" },
        { DispatchWorkflowCommandException.StatusNotAllowed(), "STATUS_NOT_ALLOWED" }
    };

    private static CompletePickingRequest Request(string id, long rowVersion) => new()
    {
        request_id = id,
        row_version = rowVersion
    };

    private static MySqlException CreateMySqlException(int number, string sqlState)
    {
        var constructor = typeof(MySqlException).GetConstructor(
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            [typeof(uint), typeof(string), typeof(string)],
            modifiers: null)!;
        return (MySqlException)constructor.Invoke([(uint)number, sqlState, "database error"]);
    }

    private static async Task SeedInventoryAsync(
        ModernWMS.Core.DBContext.SqlDBContext db,
        long erpWarehouseId,
        int skuId,
        int quantity)
    {
        db.GetDbSet<WarehouseEntity>().Add(new WarehouseEntity
        {
            id = 1,
            erp_warehouse_id = erpWarehouseId,
            warehouse_name = "深圳自建仓",
            is_valid = true,
            tenant_id = 88
        });
        db.GetDbSet<GoodslocationEntity>().Add(new GoodslocationEntity
        {
            id = 11,
            warehouse_id = 1,
            location_name = "A-01",
            warehouse_area_property = 1,
            is_valid = true,
            tenant_id = 88
        });
        db.GetDbSet<StockEntity>().Add(new StockEntity
        {
            id = 21,
            sku_id = skuId,
            goods_location_id = 11,
            goods_owner_id = 31,
            qty = quantity,
            tenant_id = 88
        });
        await db.SaveChangesAsync();
    }
}

internal sealed class DenyingWarehouseAccess
{
    public static ModernWMS.WMS.IServices.IWarehouseAccessService Create() =>
        DispatchProxy.Create<ModernWMS.WMS.IServices.IWarehouseAccessService, DenyingWarehouseAccessProxy>();
}

public class DenyingWarehouseAccessProxy : DispatchProxy
{
    protected override object? Invoke(System.Reflection.MethodInfo? targetMethod, object?[]? args) =>
        targetMethod?.Name == nameof(ModernWMS.WMS.IServices.IWarehouseAccessService.EnsureAllowedAsync)
            ? Task.FromException(new UnauthorizedAccessException("warehouse denied"))
            : throw new NotSupportedException(targetMethod?.Name);
}

internal sealed class CompletePickingThrowingWorkflow
{
    public static IDispatchWorkflowService Create(Exception exception)
    {
        var contract = DispatchProxy.Create<IDispatchWorkflowService, CompletePickingThrowingWorkflowProxy>();
        ((CompletePickingThrowingWorkflowProxy)(object)contract).Exception = exception;
        return contract;
    }
}

public class CompletePickingThrowingWorkflowProxy : DispatchProxy
{
    internal Exception Exception { get; set; } = null!;

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
        targetMethod?.Name == nameof(IDispatchWorkflowService.CompletePickingAsync)
            ? Task.FromException<CompletePickingResult>(Exception)
            : throw new NotSupportedException(targetMethod?.Name);
}

internal sealed class SaveConflictSqlDbContext(DbContextOptions options) : ModernWMS.Core.DBContext.SqlDBContext(options)
{
    public Func<Task>? BeforeConflict { get; set; }
    public Exception ConflictException { get; set; } = new DbUpdateException("simulated unique-key loser");

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        if (BeforeConflict != null)
        {
            var callback = BeforeConflict;
            BeforeConflict = null;
            await callback();
            throw ConflictException;
        }

        return await base.SaveChangesAsync(cancellationToken);
    }
}
