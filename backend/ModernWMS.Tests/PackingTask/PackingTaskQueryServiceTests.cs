using Microsoft.Extensions.Configuration;
using ModernWMS.Core.Database;
using ModernWMS.Core.DBContext.Entities;
using ModernWMS.Core.DynamicSearch;
using ModernWMS.Core.JWT;
using ModernWMS.Core.Models;
using ModernWMS.WMS.Entities.Models.PackingTask;
using ModernWMS.WMS.Entities.ViewModels.PackingTask;
using ModernWMS.WMS.Services;

namespace ModernWMS.Tests.PackingTask;

public class PackingTaskQueryServiceTests
{
    [Fact]
    public void Constructor_uses_Dapper_connection_factory_and_has_no_EF_DbContext_dependency()
    {
        var parameterTypes = typeof(PackingTaskQueryService).GetConstructors()
            .SelectMany(t => t.GetParameters())
            .Select(t => t.ParameterType)
            .ToArray();

        Assert.Contains(typeof(IMySqlConnectionFactory), parameterTypes);
        Assert.DoesNotContain(parameterTypes, t => t.Name.EndsWith("DbContext", StringComparison.Ordinal));
    }

    [Fact]
    public async Task PageAsync_returns_feature_disabled_without_reading_tasks()
    {
        var source = new InMemoryPackingTaskQueryDataSource();
        var service = CreateService(source, enabled: false);

        var result = await service.PageAsync(new PageSearch(), CurrentTenant());

        Assert.False(result.IsSuccess);
        Assert.Equal(0, result.Totals);
        Assert.Empty(result.Data);
        Assert.Empty(source.PageRequests);
    }

    [Fact]
    public async Task PageAsync_returns_tasks_from_all_warehouses_without_tenant_binding()
    {
        var source = new InMemoryPackingTaskQueryDataSource();
        source.Tasks.AddRange([
            Task(1, 101, "SHENZHEN", 320118, DateTime.UtcNow),
            Task(2, 102, "OTHER-WAREHOUSE", 9, DateTime.UtcNow)
        ]);

        var result = await CreateService(source).PageAsync(new PageSearch(), CurrentTenant());

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Totals);
        Assert.Equal([101L, 102L], result.Data.Select(t => t.sellfox_task_id).Order().ToArray());
    }

    [Fact]
    public async Task PageAsync_filters_orders_and_preserves_nullable_item_quantities()
    {
        var source = new InMemoryPackingTaskQueryDataSource();
        var commonTime = new DateTime(2026, 8, 15, 8, 0, 0);
        source.Tasks.AddRange([
            Task(1, 101, "PACK-101", 320118, commonTime),
            Task(2, 102, "PACK-102", 320118, commonTime),
            Task(3, 103, "OTHER-WAREHOUSE", 9, commonTime.AddDays(1)),
            Task(4, 104, "CANCELED", 320118, commonTime.AddDays(1), canceled: true),
            Task(5, 105, "DELETED", 320118, commonTime.AddDays(1), deleted: true),
            Task(6, 106, "NULL-TIME", 320118, null)
        ]);
        source.Items.AddRange([
            new ErpPackingTaskItemEntity
            {
                id = 11, sellfox_item_id = 1001, sellfox_task_id = 102,
                commodity_name = null, commodity_sku = "SKU-102", fn_sku = null,
                msku = "MSKU-102", task_num = null, quantity_shipped = 0, stock_available = null
            },
            new ErpPackingTaskItemEntity
            {
                id = 12, sellfox_item_id = 1002, sellfox_task_id = 102,
                commodity_name = "soft deleted", source_deleted = true
            }
        ]);

        var result = await CreateService(source).PageAsync(new PageSearch(), CurrentTenant());

        Assert.True(result.IsSuccess);
        Assert.Equal(4, result.Totals);
        Assert.Equal([103L, 102L, 101L, 106L], result.Data.Select(t => t.sellfox_task_id).ToArray());
        var item = Assert.Single(result.Data[1].item_list);
        Assert.Null(item.commodity_name);
        Assert.Null(item.fn_sku);
        Assert.Null(item.task_num);
        Assert.Equal(0, item.quantity_shipped);
        Assert.Null(item.stock_available);
        Assert.Empty(result.Data[2].item_list);
    }

    [Fact]
    public async Task PageAsync_searches_only_task_and_product_identifiers()
    {
        var source = new InMemoryPackingTaskQueryDataSource();
        source.Tasks.AddRange([
            Task(1, 101, "PACK-101", 320118, DateTime.UtcNow),
            Task(2, 102, "PACK-102", 320118, DateTime.UtcNow)
        ]);
        source.Items.Add(new ErpPackingTaskItemEntity
        {
            id = 11, sellfox_item_id = 1001, sellfox_task_id = 102, fn_sku = "FNSKU-HIT"
        });
        var page = new PageSearch
        {
            searchObjects = [new SearchObject { Name = "keyword", Text = "FNSKU-HIT" }]
        };

        var result = await CreateService(source).PageAsync(page, CurrentTenant());

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Totals);
        Assert.Equal(102, Assert.Single(result.Data).sellfox_task_id);
        Assert.Equal("FNSKU-HIT", Assert.Single(source.PageRequests).Keyword);
    }

    [Fact]
    public async Task PageAsync_filters_by_authorized_warehouse_and_excludes_tasks_in_active_orders()
    {
        var source = new InMemoryPackingTaskQueryDataSource();
        source.Tasks.AddRange([
            Task(1, 101, "ACTIVE", 320118, DateTime.UtcNow),
            Task(2, 102, "AVAILABLE", 320118, DateTime.UtcNow),
            Task(3, 103, "OTHER", 9, DateTime.UtcNow)
        ]);
        source.ActiveSourceTaskIds.Add(101);
        var access = new ModernWMS.Tests.DispatchWorkflow.RecordingWarehouseAccess();
        var service = CreateService(source, access: access.Contract);
        var page = new PageSearch
        {
            searchObjects = [new SearchObject { Name = "warehouse_id", Text = "320118" }]
        };

        var result = await service.PageAsync(page, CurrentTenant());

        Assert.Equal(102, Assert.Single(result.Data).sellfox_task_id);
        Assert.Contains(320118, access.CheckedWarehouseIds);
        Assert.Equal(320118, Assert.Single(source.PageRequests).WarehouseId);
    }

    [Fact]
    public async Task PageAsync_uses_role_authorized_default_instead_of_hardcoded_admin_warehouse()
    {
        var source = new InMemoryPackingTaskQueryDataSource();
        source.Tasks.AddRange([
            Task(1, 101, "ADMIN-DEFAULT", 320118, DateTime.UtcNow),
            Task(2, 102, "ROLE-DEFAULT", 9, DateTime.UtcNow)
        ]);
        var access = new ModernWMS.Tests.DispatchWorkflow.RecordingWarehouseAccess { DefaultWarehouseId = 9 };

        var result = await CreateService(source, access: access.Contract)
            .PageAsync(new PageSearch(), CurrentTenant());

        Assert.Equal(102, Assert.Single(result.Data).sellfox_task_id);
    }

    [Fact]
    public async Task PageAsync_matches_stock_availability_by_base_sku_ignoring_variant_suffix()
    {
        var source = new InMemoryPackingTaskQueryDataSource();
        source.Tasks.Add(Task(1, 101, "PACK-101", 320118, DateTime.UtcNow));
        source.Items.Add(new ErpPackingTaskItemEntity
        {
            id = 11, sellfox_item_id = 1001, sellfox_task_id = 101, commodity_id = 501
        });
        source.AvailabilityByItemId[11] = new PackingTaskStockAvailability("SKU-BASE", 100);
        var access = new ModernWMS.Tests.DispatchWorkflow.RecordingWarehouseAccess();

        var result = await CreateService(source, access: access.Contract)
            .PageAsync(new PageSearch(), CurrentTenant());

        var item = Assert.Single(Assert.Single(result.Data).item_list);
        Assert.Equal("SKU-BASE", item.stock_sku_code);
        Assert.Equal(100, item.stock_available_qty);
    }

    [Fact]
    public async Task PageAsync_exposes_locked_quantity_from_stock_selection()
    {
        var source = new InMemoryPackingTaskQueryDataSource();
        source.Tasks.Add(Task(1, 101, "PACK-101", 320118, DateTime.UtcNow));
        source.Items.Add(new ErpPackingTaskItemEntity
        {
            id = 11, sellfox_item_id = 1001, sellfox_task_id = 101, commodity_id = 501
        });
        source.AvailabilityByItemId[11] = new PackingTaskStockAvailability("SKU-BASE", 100, 30);

        var result = await CreateService(source).PageAsync(new PageSearch(), CurrentTenant());

        var item = Assert.Single(Assert.Single(result.Data).item_list);
        Assert.Equal(100, item.stock_available_qty);
        Assert.Equal(30, item.locked_qty);
    }

    [Fact]
    public async Task PageAsync_clamps_page_values_before_querying_data_source()
    {
        var source = new InMemoryPackingTaskQueryDataSource();

        await CreateService(source).PageAsync(new PageSearch { pageIndex = -2, pageSize = 500 }, CurrentTenant());

        var request = Assert.Single(source.PageRequests);
        Assert.Equal(0, request.Offset);
        Assert.Equal(200, request.PageSize);
    }

    [Fact]
    public async Task DeleteStockSelectionAsync_releases_the_locked_selection()
    {
        var source = new InMemoryPackingTaskQueryDataSource();
        var service = CreateService(source);

        var (flag, message) = await service.DeleteStockSelectionAsync(
            new PackingTaskStockSelectRequest
            {
                sellfox_task_id = 101,
                sellfox_item_id = 1001,
                stock_id = 12,
                qty = 0
            },
            CurrentTenant());

        Assert.True(flag);
        Assert.Equal("已取消选择，锁定库存已释放", message);
    }

    [Fact]
    public void Stock_selection_manual_cancel_preserves_binding_and_records_actor_and_reason()
    {
        var cancelledAt = new DateTime(2026, 8, 26, 10, 30, 0, DateTimeKind.Utc);
        var selection = Selection(status: PackingTaskStockSelectionEntity.ActiveStatus);

        selection.Cancel(
            actorId: 42,
            actorName: "审核员",
            reason: "用户取消装箱任务库存选择",
            operationSource: "WMS_MANUAL_CANCEL",
            cancelledAt);

        Assert.Equal(7, selection.id);
        Assert.Equal(7001, selection.reservation_id);
        Assert.Equal(7002, selection.reservation_item_id);
        Assert.Equal(PackingTaskStockSelectionEntity.CancelledStatus, selection.status);
        Assert.Equal(42, selection.cancelled_by);
        Assert.Equal("审核员", selection.cancelled_by_name);
        Assert.Equal(cancelledAt, selection.cancelled_at);
        Assert.Equal("用户取消装箱任务库存选择", selection.cancel_reason);
        Assert.Equal("WMS_MANUAL_CANCEL", selection.operation_source);
        Assert.Equal(4, selection.row_version);
        Assert.False(selection.IsActive);
    }

    [Fact]
    public void Stock_selection_pending_pick_rollback_preserves_binding_and_records_actor_and_reason()
    {
        var cancelledAt = new DateTime(2026, 8, 26, 11, 0, 0, DateTimeKind.Utc);
        var selection = Selection(status: PackingTaskStockSelectionEntity.ActiveStatus);

        selection.Cancel(
            actorId: 51,
            actorName: "回退操作员",
            reason: "待拣货回退释放库存选择",
            operationSource: "DISPATCH_ROLLBACK",
            cancelledAt);

        Assert.Equal(7, selection.id);
        Assert.Equal(12, selection.stock_id);
        Assert.Equal(7001, selection.reservation_id);
        Assert.Equal(PackingTaskStockSelectionEntity.CancelledStatus, selection.status);
        Assert.Equal(51, selection.cancelled_by);
        Assert.Equal("回退操作员", selection.cancelled_by_name);
        Assert.Equal("待拣货回退释放库存选择", selection.cancel_reason);
        Assert.Equal(cancelledAt, selection.cancelled_at);
        Assert.Equal("DISPATCH_ROLLBACK", selection.operation_source);
    }

    [Fact]
    public void Stock_selection_completed_picking_preserves_binding_as_transferred()
    {
        var transferredAt = new DateTime(2026, 8, 26, 12, 0, 0, DateTimeKind.Utc);
        var selection = Selection(status: PackingTaskStockSelectionEntity.ActiveStatus);

        selection.Transfer("DISPATCH_PICKING", transferredAt);

        Assert.Equal(7, selection.id);
        Assert.Equal(7001, selection.reservation_id);
        Assert.Equal(7002, selection.reservation_item_id);
        Assert.Equal(PackingTaskStockSelectionEntity.TransferredStatus, selection.status);
        Assert.Equal("DISPATCH_PICKING", selection.operation_source);
        Assert.Equal(transferredAt, selection.last_update_time);
        Assert.Equal(4, selection.row_version);
        Assert.Null(selection.cancelled_at);
        Assert.False(selection.IsActive);
    }

    [Fact]
    public void Stock_selection_active_view_ignores_historical_rows_and_allows_rebinding()
    {
        var cancelled = Selection(status: PackingTaskStockSelectionEntity.CancelledStatus);
        var transferred = Selection(status: PackingTaskStockSelectionEntity.TransferredStatus);
        transferred.id = 8;
        var rebound = Selection(status: PackingTaskStockSelectionEntity.ActiveStatus);
        rebound.id = 9;
        rebound.reservation_id = 8001;
        rebound.reservation_item_id = 8002;

        var active = new[] { cancelled, transferred, rebound }.Where(row => row.IsActive).ToList();

        Assert.Same(rebound, Assert.Single(active));
        Assert.Equal(9, rebound.id);
        Assert.Equal(PackingTaskStockSelectionEntity.ActiveStatus, rebound.status);
        Assert.Equal(8001, rebound.reservation_id);
    }

    private static PackingTaskQueryService CreateService(
        IPackingTaskQueryDataSource source,
        bool enabled = true,
        ModernWMS.WMS.IServices.IWarehouseAccessService? access = null)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Features:PackingTaskFirstStep"] = enabled.ToString()
            })
            .Build();
        return new PackingTaskQueryService(source, configuration, access);
    }

    private static ErpPackingTaskEntity Task(
        long id, long sellfoxTaskId, string taskNo, long warehouseId,
        DateTime? sourceCreateTime, bool canceled = false, bool deleted = false) => new()
        {
            id = id,
            sellfox_task_id = sellfoxTaskId,
            packing_task_sn = taskNo,
            warehouse_id = warehouseId,
            source_create_time = sourceCreateTime,
            source_canceled = canceled,
            source_deleted = deleted
        };

    private static CurrentUser CurrentTenant() => new() { tenant_id = 1 };

    private static PackingTaskStockSelectionEntity Selection(string status) => new()
    {
        id = 7,
        tenant_id = 1,
        sellfox_task_id = 101,
        sellfox_item_id = 1001,
        stock_id = 12,
        reservation_id = 7001,
        reservation_item_id = 7002,
        qty = 6,
        status = status,
        row_version = 3,
        operation_source = "MODERN_WMS"
    };

    private sealed class InMemoryPackingTaskQueryDataSource : IPackingTaskQueryDataSource
    {
        public List<ErpPackingTaskEntity> Tasks { get; } = [];
        public List<ErpPackingTaskItemEntity> Items { get; } = [];
        public HashSet<long> ActiveSourceTaskIds { get; } = [];
        public Dictionary<long, PackingTaskStockAvailability> AvailabilityByItemId { get; } = [];
        public List<PackingTaskPageRequest> PageRequests { get; } = [];

        public Task<PackingTaskPageData> LoadPageAsync(PackingTaskPageRequest request)
        {
            PageRequests.Add(request);
            var query = Tasks.Where(t => !t.source_deleted && !t.source_canceled);
            if (request.WarehouseId != null)
            {
                query = query.Where(t => t.warehouse_id == request.WarehouseId);
            }
            query = query.Where(t => !ActiveSourceTaskIds.Contains(t.sellfox_task_id));
            if (!string.IsNullOrWhiteSpace(request.Keyword))
            {
                query = query.Where(t => t.packing_task_sn.Contains(request.Keyword)
                    || Items.Any(i => !i.source_deleted && i.sellfox_task_id == t.sellfox_task_id
                        && new[] { i.commodity_name, i.commodity_sku, i.sku, i.fn_sku, i.msku }
                            .Any(value => value?.Contains(request.Keyword) == true)));
            }

            var totals = query.Count();
            var tasks = query.OrderByDescending(t => t.source_create_time)
                .ThenByDescending(t => t.id)
                .Skip(request.Offset)
                .Take(request.PageSize)
                .ToList();
            var ids = tasks.Select(t => t.sellfox_task_id).ToHashSet();
            var items = Items.Where(t => !t.source_deleted && ids.Contains(t.sellfox_task_id))
                .OrderBy(t => t.id)
                .ToList();
            return System.Threading.Tasks.Task.FromResult(
                new PackingTaskPageData(tasks, items, AvailabilityByItemId, totals));
        }

        public Task<PackingTaskSelectableData?> LoadSelectableStockAsync(
            PackingTaskStockPageRequest request, CurrentUser currentUser) =>
            System.Threading.Tasks.Task.FromResult<PackingTaskSelectableData?>(null);

        public Task<PackingTaskStockSaveResult> SaveSelectionAsync(
            PackingTaskStockSelectRequest request, CurrentUser currentUser) =>
            System.Threading.Tasks.Task.FromResult(new PackingTaskStockSaveResult(false, "not used"));

        public Task<PackingTaskStockSaveResult> DeleteSelectionAsync(
            PackingTaskStockSelectRequest request, CurrentUser currentUser) =>
            System.Threading.Tasks.Task.FromResult(new PackingTaskStockSaveResult(true, "已取消选择，锁定库存已释放"));
    }
}
