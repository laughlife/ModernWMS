using System.Reflection;
using System.Reflection.Emit;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using ModernWMS.Core.Database;
using ModernWMS.Core.DBContext.Entities;
using ModernWMS.Core.DynamicSearch;
using ModernWMS.Core.JWT;
using ModernWMS.Core.Models;
using ModernWMS.WMS.Entities.ViewModels.PackingTask;
using ModernWMS.WMS.Services.PackingTask;
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
    public void Constructor_delegates_packing_stock_mutations_to_the_erp_client()
    {
        var parameterTypeNames = typeof(PackingTaskQueryService).GetConstructors()
            .SelectMany(t => t.GetParameters())
            .Select(t => t.ParameterType.Name)
            .ToArray();

        Assert.Contains("IErpPackingStockClient", parameterTypeNames);
        Assert.DoesNotContain("IStockAllocationMutationService", parameterTypeNames);
    }

    [Fact]
    public async Task PageAsync_returns_feature_disabled_without_reading_tasks()
    {
        var source = new InMemoryPackingTaskQueryDataSource();
        var service = CreateService(source, enabled: false);

        var result = await service.PageAsync(new PageSearch(), CurrentUserContext());

        Assert.False(result.IsSuccess);
        Assert.Equal(0, result.Totals);
        Assert.Empty(result.Data);
        Assert.Empty(source.PageRequests);
    }

    [Fact]
    public async Task PageAsync_returns_tasks_from_all_warehouses()
    {
        var source = new InMemoryPackingTaskQueryDataSource();
        source.Tasks.AddRange([
            Task(1, 101, "SHENZHEN", 320118, DateTime.UtcNow),
            Task(2, 102, "OTHER-WAREHOUSE", 9, DateTime.UtcNow)
        ]);

        var result = await CreateService(source).PageAsync(new PageSearch(), CurrentUserContext());

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

        var result = await CreateService(source).PageAsync(new PageSearch(), CurrentUserContext());

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

        var result = await CreateService(source).PageAsync(page, CurrentUserContext());

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

        var result = await service.PageAsync(page, CurrentUserContext());

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
            .PageAsync(new PageSearch(), CurrentUserContext());

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
        source.AvailabilityByItemId[11] = new PackingTaskStockAvailability("SKU-BASE", 100, AvailableQty: 100);
        var access = new ModernWMS.Tests.DispatchWorkflow.RecordingWarehouseAccess();

        var result = await CreateService(source, access: access.Contract)
            .PageAsync(new PageSearch(), CurrentUserContext());

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
        source.AvailabilityByItemId[11] = new PackingTaskStockAvailability("SKU-BASE", 100, 30, 100);

        var result = await CreateService(source).PageAsync(new PageSearch(), CurrentUserContext());

        var item = Assert.Single(Assert.Single(result.Data).item_list);
        Assert.Equal(100, item.stock_available_qty);
        Assert.Equal(30, item.locked_qty);
    }

    [Fact]
    public async Task PageAsync_clamps_page_values_before_querying_data_source()
    {
        var source = new InMemoryPackingTaskQueryDataSource();

        await CreateService(source).PageAsync(new PageSearch { pageIndex = -2, pageSize = 500 }, CurrentUserContext());

        var request = Assert.Single(source.PageRequests);
        Assert.Equal(0, request.Offset);
        Assert.Equal(200, request.PageSize);
    }

    [Fact]
    public async Task DeleteStockSelectionAsync_fails_closed_when_the_erp_client_is_unavailable()
    {
        var source = new InMemoryPackingTaskQueryDataSource();
        var service = CreateService(source);

        var (flag, message) = await service.DeleteStockSelectionAsync(
            new PackingTaskStockSelectRequest
            {
                sellfox_task_id = 101,
                sellfox_item_id = 1001,
                stock_id = 12,
                qty = 0,
                goods_owner_id = 8,
                row_version = 1,
                request_id = "withdraw-101"
            },
            CurrentUserContext());

        Assert.False(flag);
        Assert.Contains("ERP", message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SelectStockAsync_rejects_sku_mismatch_without_a_server_timed_challenge()
    {
        var erp = new RecordingErpPackingStockClient
        {
            Plan = MismatchPlan(rowVersion: 0)
        };
        var service = CreateService(new InMemoryPackingTaskQueryDataSource(), erp: erp);

        var (flag, message) = await service.SelectStockAsync(new PackingTaskStockSelectRequest
        {
            sellfox_task_id = 101,
            sellfox_item_id = 1001,
            stock_id = 12,
            goods_owner_id = 8,
            qty = 4,
            row_version = 0,
            request_id = "mismatch-without-challenge",
            sku_mismatch_confirmed = true
        }, CurrentUserContext());

        Assert.False(flag);
        Assert.Contains("3 秒", message, StringComparison.Ordinal);
        Assert.Empty(erp.ContributionCommands);
    }

    [Fact]
    public async Task SelectStockAsync_uses_the_erp_plan_row_version_instead_of_the_pool_or_client_version()
    {
        var erp = new RecordingErpPackingStockClient
        {
            Plan = new ErpPackingStockPlan
            {
                rowVersion = 41,
                pools = [new ErpPackingStockPool
                {
                    stockId = 12, goodsOwnerId = 8, skuMatched = true, availableQty = 20
                }]
            }
        };
        var service = CreateService(new InMemoryPackingTaskQueryDataSource(), erp: erp);

        var (flag, message) = await service.SelectStockAsync(new PackingTaskStockSelectRequest
        {
            sellfox_task_id = 101, sellfox_item_id = 1001, stock_id = 12, goods_owner_id = 8,
            qty = 4, row_version = 0, request_id = "plan-row-version", sku_mismatch_confirmed = false
        }, CurrentUserContext());

        Assert.True(flag, message);
        Assert.Equal(41, Assert.Single(erp.ContributionCommands).RowVersion);
    }

    [Fact]
    public void Packing_selection_service_compiled_code_contains_no_local_selection_or_reservation_sql()
    {
        var commands = PackingSelectionProductionSql();

        Assert.Empty(commands);
    }

    private static PackingTaskQueryService CreateService(
        IPackingTaskQueryDataSource source,
        bool enabled = true,
        ModernWMS.WMS.IServices.IWarehouseAccessService? access = null,
        IErpPackingStockClient? erp = null)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Features:PackingTaskFirstStep"] = enabled.ToString()
            })
            .Build();
        return new PackingTaskQueryService(source, configuration, access, erp);
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

    private static CurrentUser CurrentUserContext() => new();

    private static ErpPackingStockPlan MismatchPlan(long rowVersion) => new()
    {
        rowVersion = rowVersion,
        pools = [new ErpPackingStockPool
        {
            stockId = 12,
            goodsOwnerId = 8,
            skuMatched = false,
            availableQty = 20
        }]
    };

    private static void AssertCancellationTransition(string sql, string reason)
    {
        Assert.Contains("SET `status`='CANCELLED'", sql, StringComparison.Ordinal);
        Assert.Contains("`cancelled_by`=", sql, StringComparison.Ordinal);
        Assert.Contains("`cancelled_by_name`=", sql, StringComparison.Ordinal);
        Assert.Contains("`cancelled_at`=", sql, StringComparison.Ordinal);
        Assert.Contains($"`cancel_reason`='{reason}'", sql, StringComparison.Ordinal);
        Assert.Contains("`row_version`=`row_version`+1", sql, StringComparison.Ordinal);
        Assert.Matches(@"`status`\s*=\s*'ACTIVE'", sql);
        Assert.DoesNotContain("DELETE", sql, StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<string> PackingSelectionProductionSql() =>
        new[] { typeof(PackingTaskQueryService) }
            .Concat(typeof(PackingTaskQueryService).GetNestedTypes(BindingFlags.NonPublic))
            .SelectMany(type => type.GetMethods(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance))
            .SelectMany(ReadStringLiterals)
            .Where(value => value.Contains("wms_packing_task_stock_selection", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.Ordinal)
            .ToList();

    private static IEnumerable<string> ReadStringLiterals(MethodInfo method)
    {
        var body = method.GetMethodBody();
        var il = body?.GetILAsByteArray();
        if (il == null) yield break;

        for (var offset = 0; offset < il.Length;)
        {
            var value = il[offset++];
            var key = value == 0xfe
                ? (ushort)(0xfe00 | il[offset++])
                : value;
            if (!OpCodesByValue.TryGetValue(key, out var opCode)) yield break;
            if (opCode == OpCodes.Ldstr)
            {
                var token = BitConverter.ToInt32(il, offset);
                yield return method.Module.ResolveString(token);
            }
            offset += OperandSize(opCode.OperandType, il, offset);
        }
    }

    private static int OperandSize(OperandType operandType, byte[] il, int offset) => operandType switch
    {
        OperandType.InlineNone => 0,
        OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
        OperandType.InlineVar => 2,
        OperandType.InlineI or OperandType.InlineBrTarget or OperandType.InlineField
            or OperandType.InlineMethod or OperandType.InlineSig or OperandType.InlineString
            or OperandType.InlineTok or OperandType.InlineType or OperandType.ShortInlineR => 4,
        OperandType.InlineI8 or OperandType.InlineR => 8,
        OperandType.InlineSwitch => 4 + BitConverter.ToInt32(il, offset) * 4,
        _ => throw new InvalidOperationException($"Unsupported IL operand type: {operandType}")
    };

    private static readonly IReadOnlyDictionary<ushort, OpCode> OpCodesByValue =
        typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.FieldType == typeof(OpCode))
            .Select(field => (OpCode)field.GetValue(null)!)
            .ToDictionary(opCode => unchecked((ushort)opCode.Value));

    private static string FindRepositoryFile(params string[] relativeSegments)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
        {
            var candidate = Path.Combine([directory.FullName, .. relativeSegments]);
            if (File.Exists(candidate)) return candidate;
        }
        throw new FileNotFoundException($"Repository file not found: {Path.Combine(relativeSegments)}");
    }

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

    private sealed class RecordingErpPackingStockClient : IErpPackingStockClient
    {
        public ErpPackingStockPlan Plan { get; init; } = new();
        public List<ErpPackingStockContributionCommand> ContributionCommands { get; } = [];

        public Task<ErpPackingStockResult<ErpPackingStockPlan>> GetPlanAsync(
            ErpPackingStockPlanQuery request, CancellationToken cancellationToken = default) =>
            System.Threading.Tasks.Task.FromResult(ErpPackingStockResult<ErpPackingStockPlan>.Success(Plan));

        public Task<ErpPackingStockResult<ErpPackingStockPlan>> UpdateVariantAsync(
            ErpPackingStockVariantCommand request, CancellationToken cancellationToken = default) =>
            System.Threading.Tasks.Task.FromResult(ErpPackingStockResult<ErpPackingStockPlan>.Success(Plan));

        public Task<ErpPackingStockResult<ErpPackingStockPlan>> UpdateContributionAsync(
            ErpPackingStockContributionCommand request, CancellationToken cancellationToken = default)
        {
            ContributionCommands.Add(request);
            return System.Threading.Tasks.Task.FromResult(ErpPackingStockResult<ErpPackingStockPlan>.Success(Plan));
        }

        public Task<ErpPackingStockResult<ErpPackingStockPlan>> WithdrawParticipantAsync(
            ErpPackingStockParticipantWithdrawCommand request, CancellationToken cancellationToken = default) =>
            System.Threading.Tasks.Task.FromResult(ErpPackingStockResult<ErpPackingStockPlan>.Success(Plan));

        public Task<ErpPackingStockResult<ErpPackingStockPlan>> RetryAsync(
            ErpPackingStockRetryCommand request, CancellationToken cancellationToken = default) =>
            System.Threading.Tasks.Task.FromResult(ErpPackingStockResult<ErpPackingStockPlan>.Success(Plan));

        public Task<ErpPackingStockResult<bool>> ConsumeAsync(
            ErpPackingStockConsumeCommand request, CancellationToken cancellationToken = default) =>
            System.Threading.Tasks.Task.FromResult(ErpPackingStockResult<bool>.Success(true));
    }
}
