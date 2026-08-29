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
        source.AvailabilityByItemId[11] = new PackingTaskStockAvailability("SKU-BASE", 100);
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
        source.AvailabilityByItemId[11] = new PackingTaskStockAvailability("SKU-BASE", 100, 30);

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
    public async Task SelectableStock_returns_same_creator_same_warehouse_and_prioritizes_matching_product()
    {
        var source = new InMemoryPackingTaskQueryDataSource
        {
            SelectableData = new PackingTaskSelectableData(
            [
                Stock(9002, 320118, 77, matched: false),
                Stock(9001, 320118, 77, matched: true)
            ],
            320118,
            "深圳自建仓")
        };

        var (rows, totals) = await CreateService(source).SelectableStockPageAsync(
            new PackingTaskStockPageRequest { sellfox_task_id = 101, sellfox_item_id = 1001 },
            CurrentUserContext());

        Assert.Equal(2, totals);
        Assert.Equal([9001L, 9002L], rows.Select(row => row.erp_stock_id).ToArray());
        Assert.All(rows, row =>
        {
            Assert.Equal(320118, row.warehouse_id);
            Assert.Equal(77, row.order_user_id);
        });
    }

    [Fact]
    public void Packing_stock_api_contract_exposes_only_erp_stock_identity_and_no_location_filters()
    {
        var pageProperties = typeof(PackingTaskStockPageRequest).GetProperties()
            .Select(property => property.Name).Order().ToArray();
        var selectionProperties = typeof(PackingTaskStockSelectRequest).GetProperties()
            .Select(property => property.Name).Order().ToArray();

        Assert.Equal(
            ["keyword", "page_index", "page_size", "sellfox_item_id", "sellfox_task_id"],
            pageProperties);
        Assert.Equal(
            ["erp_stock_id", "sellfox_item_id", "sellfox_task_id", "variant"],
            selectionProperties);
        Assert.DoesNotContain(typeof(SelectableStockViewModel).GetProperties(), property =>
            property.Name.Contains("allocation", StringComparison.OrdinalIgnoreCase)
            || property.Name.Contains("location", StringComparison.OrdinalIgnoreCase)
            || property.Name is "stock_id" or "goods_owner_id" or "wms_sku_id");
    }

    [Fact]
    public void Task_creator_resolution_requires_one_active_system_user()
    {
        Assert.Equal(77, PackingTaskOwnerPolicy.Resolve("李伟", [new PackingTaskOwnerCandidate(77, "李伟")]));
        Assert.Throws<InvalidOperationException>(() => PackingTaskOwnerPolicy.Resolve("李伟", []));
        Assert.Throws<InvalidOperationException>(() => PackingTaskOwnerPolicy.Resolve(
            "李伟", [new PackingTaskOwnerCandidate(77, "李伟"), new PackingTaskOwnerCandidate(88, "李伟")]));
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
                erp_stock_id = 12
            },
            CurrentUserContext());

        Assert.True(flag);
        Assert.Equal("已取消选择，锁定库存已释放", message);
    }

    [Fact]
    public void Packing_selection_production_sql_never_physically_deletes_and_every_reference_is_active_scoped()
    {
        var commands = PackingSelectionProductionSql();

        Assert.NotEmpty(commands);
        Assert.DoesNotContain(commands, sql => Regex.IsMatch(
            sql, @"DELETE\s+FROM\s+`?wms_packing_task_stock_selection`?", RegexOptions.IgnoreCase));
        Assert.All(commands, sql =>
        {
            var normalized = sql.Replace("`", string.Empty, StringComparison.Ordinal);
            if (Regex.IsMatch(normalized,
                @"INSERT\s+INTO\s+wms_packing_task_stock_selection", RegexOptions.IgnoreCase))
            {
                Assert.Contains("status", normalized, StringComparison.OrdinalIgnoreCase);
                Assert.Contains("'ACTIVE'", normalized, StringComparison.Ordinal);
                return;
            }
            var tableReferences = Regex.Matches(
                normalized, @"\bwms_packing_task_stock_selection\b", RegexOptions.IgnoreCase).Count;
            var activeGuards = Regex.Matches(
                normalized, @"(?:\b\w+\.)?\bstatus\s*=\s*'ACTIVE'", RegexOptions.IgnoreCase).Count;
            Assert.True(activeGuards >= tableReferences,
                $"Every packing selection table reference must be ACTIVE-scoped. SQL: {sql}");
        });
    }

    [Fact]
    public void Packing_selection_cancel_and_rollback_sql_preserves_rows_with_actor_reason_and_version()
    {
        var commands = PackingSelectionProductionSql();
        var manualCancels = commands.Where(sql =>
            sql.Contains("SET `status`='CANCELLED'", StringComparison.Ordinal)
            && sql.Contains("`cancel_reason`=@Reason", StringComparison.Ordinal)).ToList();
        var rollback = Assert.Single(commands, sql => sql.Contains("DISPATCH_ROLLBACK", StringComparison.Ordinal));

        Assert.NotEmpty(manualCancels);
        Assert.All(manualCancels, sql =>
            AssertCancellationTransition(sql, "@Reason"));
        AssertCancellationTransition(rollback, "待拣货回退释放库存选择");
    }

    [Fact]
    public void Packing_selection_completed_picking_sql_transfers_instead_of_deleting()
    {
        var transfer = Assert.Single(PackingSelectionProductionSql(),
            sql => sql.Contains("DISPATCH_PICKING", StringComparison.Ordinal));

        Assert.Contains("SET `status`='TRANSFERRED'", transfer, StringComparison.Ordinal);
        Assert.Contains("`last_update_time`=@now", transfer, StringComparison.Ordinal);
        Assert.Contains("`row_version`=`row_version`+1", transfer, StringComparison.Ordinal);
        Assert.Contains("AND `status`='ACTIVE'", transfer, StringComparison.Ordinal);
        Assert.DoesNotContain("DELETE", transfer, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Packing_selection_save_sql_ignores_history_and_inserts_a_new_active_cycle()
    {
        var commands = PackingSelectionProductionSql();
        var existingLookups = commands.Where(sql =>
            sql.Contains("FOR UPDATE", StringComparison.OrdinalIgnoreCase)
            && sql.Contains("sellfox_item_id", StringComparison.OrdinalIgnoreCase)
            && sql.Contains("status`='ACTIVE", StringComparison.OrdinalIgnoreCase))
            .ToList();
        var inserts = commands.Where(sql => Regex.IsMatch(sql,
            @"INSERT\s+INTO\s+`?wms_packing_task_stock_selection`?", RegexOptions.IgnoreCase)).ToList();

        Assert.NotEmpty(existingLookups);
        Assert.All(existingLookups, sql => Assert.Matches(
            @"`?status`?\s*=\s*'ACTIVE'", sql));
        Assert.NotEmpty(inserts);
        Assert.All(inserts, sql =>
        {
            Assert.Contains("status", sql, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("'ACTIVE'", sql, StringComparison.Ordinal);
            Assert.DoesNotContain("ON DUPLICATE KEY", sql, StringComparison.OrdinalIgnoreCase);
        });
    }

    [Fact]
    public void Packing_stock_mutations_write_business_and_balance_audit_logs()
    {
        var literals=typeof(PackingTaskQueryService).Assembly.GetTypes()
            .Where(type=>type.Namespace?.StartsWith("ModernWMS.WMS.Services",StringComparison.Ordinal)==true)
            .SelectMany(type=>type.GetMethods(BindingFlags.Public|BindingFlags.NonPublic
                                              |BindingFlags.Static|BindingFlags.Instance))
            .SelectMany(ReadStringLiterals)
            .ToArray();

        Assert.Contains(literals,sql=>
            sql.Contains("INSERT INTO `wms_action_log`",StringComparison.OrdinalIgnoreCase)
            &&sql.Contains("`user_name`",StringComparison.OrdinalIgnoreCase)
            &&sql.Contains("`action_content`",StringComparison.OrdinalIgnoreCase));
        Assert.Contains(literals,sql=>
            sql.Contains("INSERT INTO `trk_stock_record`",StringComparison.OrdinalIgnoreCase)
            &&sql.Contains("`operation_key`",StringComparison.OrdinalIgnoreCase)
            &&sql.Contains("`available_change_qty`",StringComparison.OrdinalIgnoreCase)
            &&sql.Contains("`occupied_change_qty`",StringComparison.OrdinalIgnoreCase)
            &&sql.Contains("`total_change_qty`",StringComparison.OrdinalIgnoreCase)
            &&sql.Contains("`reservation_item_id`",StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Packing_selection_cutover_guard_counts_only_active_legacy_locks()
    {
        var script = File.ReadAllText(FindRepositoryFile("flyway", "manual", "erp_stock_allocation_cutover.sql"));
        const string guardMessage = "仍有旧WMS装箱锁定，必须先完成或撤销";
        var guardEnd = script.IndexOf(guardMessage, StringComparison.Ordinal);
        Assert.True(guardEnd > 0, "Cutover packing-selection guard was not found.");
        var guardStart = script.LastIndexOf("SELECT COUNT(*) INTO v_count", guardEnd, StringComparison.Ordinal);
        Assert.True(guardStart >= 0, "Cutover packing-selection count query was not found.");
        var guardSql = script[guardStart..guardEnd];

        Assert.Matches(@"selection\.`status`\s*=\s*'ACTIVE'", guardSql);
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

    private static CurrentUser CurrentUserContext() => new();

    private static SelectableStockViewModel Stock(
        long erpStockId, int warehouseId, int creatorId, bool matched) => new()
        {
            erp_stock_id = erpStockId,
            warehouse_id = warehouseId,
            order_user_id = creatorId,
            matched = matched,
            sku_code = $"SKU-{erpStockId}"
        };

    private static void AssertCancellationTransition(string sql, string reason)
    {
        Assert.Contains("SET `status`='CANCELLED'", sql, StringComparison.Ordinal);
        Assert.Contains("`cancelled_by`=", sql, StringComparison.Ordinal);
        Assert.Contains("`cancelled_by_name`=", sql, StringComparison.Ordinal);
        Assert.Contains("`cancelled_at`=", sql, StringComparison.Ordinal);
        if (reason.StartsWith('@'))
            Assert.Contains($"`cancel_reason`={reason}", sql, StringComparison.Ordinal);
        else
            Assert.Contains($"`cancel_reason`='{reason}'", sql, StringComparison.Ordinal);
        Assert.Contains("`row_version`=`row_version`+1", sql, StringComparison.Ordinal);
        Assert.Matches(@"`status`\s*=\s*'ACTIVE'", sql);
        Assert.DoesNotContain("DELETE", sql, StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<string> PackingSelectionProductionSql() =>
        typeof(PackingTaskQueryService).Assembly.GetTypes()
            .Where(type => type.Namespace?.StartsWith("ModernWMS.WMS.Services", StringComparison.Ordinal) == true)
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
        public PackingTaskSelectableData? SelectableData { get; init; }

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
            System.Threading.Tasks.Task.FromResult(SelectableData);

        public Task<PackingTaskStockSaveResult> SaveSelectionAsync(
            PackingTaskStockSelectRequest request, CurrentUser currentUser) =>
            System.Threading.Tasks.Task.FromResult(new PackingTaskStockSaveResult(false, "not used"));

        public Task<PackingTaskStockSaveResult> DeleteSelectionAsync(
            PackingTaskStockSelectRequest request, CurrentUser currentUser) =>
            System.Threading.Tasks.Task.FromResult(new PackingTaskStockSaveResult(true, "已取消选择，锁定库存已释放"));
    }
}
