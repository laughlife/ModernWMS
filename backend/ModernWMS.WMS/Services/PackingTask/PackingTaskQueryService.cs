using System.Data;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Dapper;
using Microsoft.Extensions.Configuration;
using ModernWMS.Core.Database;
using ModernWMS.Core.DBContext.Entities;
using ModernWMS.Core.JWT;
using ModernWMS.Core.Models;
using ModernWMS.WMS.Entities.ViewModels;
using ModernWMS.WMS.Entities.ViewModels.PackingTask;
using ModernWMS.WMS.IServices;
using ModernWMS.WMS.IServices.StockAllocation;
using ModernWMS.WMS.Services.PackingTask;

namespace ModernWMS.WMS.Services;

internal sealed record PackingTaskPageRequest(
    string Keyword,
    long? WarehouseId,
    long GroupId,
    long MemberId,
    int Offset,
    int PageSize);

internal sealed record PackingTaskStockAvailability(
    string SkuCode,
    int StockQty,
    int LockedQty = 0,
    int? AvailableQty = null);

internal sealed record PackingTaskPageData(
    List<ErpPackingTaskEntity> Tasks,
    List<ErpPackingTaskItemEntity> Items,
    IReadOnlyDictionary<long, PackingTaskStockAvailability> AvailabilityByItemId,
    int Totals);

public sealed record PackingTaskSelectableResult(bool IsSuccess, string ErrorMessage,
    List<SelectableStockViewModel> Data, int Totals);

// Compatibility carriers for private legacy code during the staged cutover. They are not part of the service boundary.
internal sealed record PackingTaskSelectableData(List<SelectableStockViewModel> Rows, int WarehouseId,
    string WarehouseName);
internal sealed record PackingTaskStockSaveResult(bool IsSuccess, string Message);

/// <summary>Testable query boundary implemented with Dapper in production.</summary>
internal interface IPackingTaskQueryDataSource
{
    Task<PackingTaskPageData> LoadPageAsync(PackingTaskPageRequest request);
}

/// <summary>Reads formal packing-task snapshots without creating dispatch business facts.</summary>
public class PackingTaskQueryService : IPackingTaskQueryService
{
    private readonly IPackingTaskQueryDataSource _dataSource;
    private readonly IConfiguration _configuration;
    private readonly IWarehouseAccessService? _warehouseAccessService;
    private readonly IErpPackingStockClient _erpPackingStockClient;
    private static readonly ConcurrentDictionary<string, SkuMismatchChallenge> SkuMismatchChallenges = new();
    private static readonly TimeSpan SkuMismatchMinimumReminder = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan SkuMismatchChallengeLifetime = TimeSpan.FromMinutes(5);

    /// <summary>Initializes the packing-task query service.</summary>
    public PackingTaskQueryService(
        IMySqlConnectionFactory connectionFactory,
        IConfiguration configuration,
        IWarehouseAccessService warehouseAccessService,
        IErpPackingStockClient erpPackingStockClient)
        : this(new DapperPackingTaskQueryDataSource(connectionFactory), configuration, warehouseAccessService,
            erpPackingStockClient)
    {
    }

    internal PackingTaskQueryService(
        IPackingTaskQueryDataSource dataSource,
        IConfiguration configuration,
        IWarehouseAccessService? warehouseAccessService = null,
        IErpPackingStockClient? erpPackingStockClient = null)
    {
        _dataSource = dataSource ?? throw new ArgumentNullException(nameof(dataSource));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _warehouseAccessService = warehouseAccessService;
        _erpPackingStockClient = erpPackingStockClient ?? new UnavailableErpPackingStockClient();
    }

    /// <summary>Gets a page of packing tasks.</summary>
    public async Task<PackingTaskQueryResult> PageAsync(PageSearch pageSearch, CurrentUser currentUser)
    {
        if (!_configuration.GetValue("Features:PackingTaskFirstStep", false))
        {
            return Failure("装箱任务功能未启用");
        }

        var warehouseText = FindSearchText(pageSearch, "warehouse_id");
        long? warehouseId = long.TryParse(warehouseText, out var parsedWarehouseId) && parsedWarehouseId > 0
            ? parsedWarehouseId
            : null;
        if (_warehouseAccessService != null)
        {
            if (warehouseId == null)
            {
                warehouseId = (await _warehouseAccessService.GetAllowedAsync(currentUser)).default_warehouse_id;
                if (warehouseId == null)
                {
                    return new PackingTaskQueryResult(true, string.Empty, [], 0);
                }
            }
            else
            {
                await _warehouseAccessService.EnsureAllowedAsync(warehouseId.Value, currentUser);
            }
        }

        var pageIndex = Math.Max(pageSearch.pageIndex, 1);
        var pageSize = Math.Clamp(pageSearch.pageSize, 1, 200);
        var groupId = long.TryParse(FindSearchText(pageSearch, "group_id"), out var parsedGroupId) ? parsedGroupId : 0;
        var memberId = long.TryParse(FindSearchText(pageSearch, "member_id"), out var parsedMemberId) ? parsedMemberId : 0;
        var page = await _dataSource.LoadPageAsync(new PackingTaskPageRequest(
            FindSearchText(pageSearch, "keyword"),
            warehouseId,
            groupId,
            memberId,
            (pageIndex - 1) * pageSize,
            pageSize));
        var itemsByTask = page.Items.GroupBy(t => t.sellfox_task_id)
            .ToDictionary(t => t.Key, t => t.ToList());
        var data = page.Tasks.Select(task => new PackingTaskQueryViewModel
        {
            id = task.id,
            sellfox_task_id = task.sellfox_task_id,
            packing_task_sn = task.packing_task_sn,
            warehouse_id = task.warehouse_id,
            warehouse_name = task.warehouse_name,
            complete_num = task.complete_num,
            task_num = task.task_num,
            create_name = task.create_name,
            source_create_time = task.source_create_time,
            item_count = task.item_count,
            shop_name = task.shop_name,
            marketplace_name = task.marketplace_name,
            item_list = (itemsByTask.GetValueOrDefault(task.sellfox_task_id) ?? [])
                .Select(item => BuildItemViewModel(item, page.AvailabilityByItemId))
                .ToList()
        }).ToList();

        return new PackingTaskQueryResult(true, string.Empty, data, page.Totals);
    }

    /// <summary>Gets stock that can be selected for a packing task.</summary>
    public async Task<PackingTaskSelectableResult> SelectableStockPageAsync(
        PackingTaskStockPageRequest request,
        CurrentUser currentUser)
    {
        var actor = BuildActor(currentUser);
        var result = await _erpPackingStockClient.GetPlanAsync(
            new ErpPackingStockPlanQuery(request.sellfox_task_id, request.sellfox_item_id, actor.id, actor.name));
        if (!result.IsSuccess || result.Data == null)
        {
            return new PackingTaskSelectableResult(false, result.ErrorMessage, [], 0);
        }

        // ERP returns owner/SKU pools. WMS intentionally does not expose location or physical batch bindings.
        var ordered = result.Data.pools
            .Where(pool => pool.availableQty > 0 || pool.contributionQty > 0)
            .Where(pool => string.IsNullOrWhiteSpace(request.keyword)
                || pool.skuCode.Contains(request.keyword.Trim(), StringComparison.OrdinalIgnoreCase)
                || pool.goodsOwnerName.Contains(request.keyword.Trim(), StringComparison.OrdinalIgnoreCase))
            .Where(pool => string.IsNullOrWhiteSpace(request.owner)
                || pool.goodsOwnerName.Contains(request.owner.Trim(), StringComparison.OrdinalIgnoreCase))
            .Select(pool => new SelectableStockViewModel
            {
                stock_id = pool.stockId,
                sku_code = pool.skuCode,
                goods_owner_id = pool.goodsOwnerId,
                goods_owner_name = pool.goodsOwnerName,
                qty = ToInt(pool.availableQty),
                available_qty = ToInt(pool.availableQty),
                matched = pool.skuMatched,
                selected = pool.contributionQty > 0,
                selected_qty = ToInt(pool.contributionQty),
                row_version = result.Data.rowVersion,
                can_manage = result.Data.canManageAllContributions || result.Data.canManageOwnContribution,
                is_creator_stock = result.Data.canManageOwnContribution
            })
            .OrderByDescending(t => t.matched)
            .ThenByDescending(t => t.available_qty)
            .ThenBy(t => t.sku_code)
            .ToList();
        var totals = ordered.Count;
        var pageIndex = Math.Max(request.page_index, 1);
        var pageSize = Math.Clamp(request.page_size, 1, 200);
        return new PackingTaskSelectableResult(true, string.Empty,
            ordered.Skip((pageIndex - 1) * pageSize).Take(pageSize).ToList(), totals);
    }

    /// <summary>Selects stock for a packing task.</summary>
    public async Task<(bool flag, string message)> SelectStockAsync(
        PackingTaskStockSelectRequest request,
        CurrentUser currentUser)
    {
        if (request.stock_id <= 0 || request.goods_owner_id <= 0 || request.qty < 0 || request.row_version < 0
            || string.IsNullOrWhiteSpace(request.request_id))
        {
            return (false, "库存贡献缺少库存、货主、请求标识或版本号");
        }
        var actor = BuildActor(currentUser);
        var plan = await _erpPackingStockClient.GetPlanAsync(
            new ErpPackingStockPlanQuery(request.sellfox_task_id, request.sellfox_item_id, actor.id, actor.name));
        if (!plan.IsSuccess || plan.Data == null) return (false, plan.ErrorMessage);
        var pool = plan.Data.pools.FirstOrDefault(t => t.stockId == request.stock_id
            && t.goodsOwnerId == request.goods_owner_id);
        if (pool == null) return (false, "ERP 库存池已变更，请刷新后重试");
        if (!pool.skuMatched)
        {
            if (!request.sku_mismatch_confirmed || !TryConsumeSkuMismatchChallenge(request, currentUser))
                return (false, "SKU 不匹配须完成服务端 3 秒确认后才能继续");
        }
        var rowVersion = plan.Data.rowVersion;
        if (request.variant > 0)
        {
            var variant = await _erpPackingStockClient.UpdateVariantAsync(new ErpPackingStockVariantCommand(
                request.sellfox_task_id, request.sellfox_item_id, CommandRequestId(request.request_id, "variant"), rowVersion, actor.id,
                actor.name, request.variant));
            if (!variant.IsSuccess || variant.Data == null) return (false, variant.ErrorMessage);
            rowVersion = variant.Data.rowVersion;
        }
        var result = await _erpPackingStockClient.UpdateContributionAsync(new ErpPackingStockContributionCommand(
            request.sellfox_task_id, request.sellfox_item_id, CommandRequestId(request.request_id, "contribution"), rowVersion, actor.id, actor.name,
            request.stock_id, request.goods_owner_id, request.qty, request.sku_mismatch_confirmed));
        return result.IsSuccess ? (true, "库存贡献已由ERP更新") : (false, result.ErrorMessage);
    }

    /// <summary>Deletes a packing-task stock selection.</summary>
    public async Task<(bool flag, string message)> DeleteStockSelectionAsync(
        PackingTaskStockSelectRequest request,
        CurrentUser currentUser)
    {
        if (request.goods_owner_id <= 0 || request.row_version < 0 || string.IsNullOrWhiteSpace(request.request_id))
            return (false, "撤回贡献缺少货主、请求标识或版本号");
        var actor = BuildActor(currentUser);
        var plan = await _erpPackingStockClient.GetPlanAsync(
            new ErpPackingStockPlanQuery(request.sellfox_task_id, request.sellfox_item_id, actor.id, actor.name));
        if (!plan.IsSuccess || plan.Data == null) return (false, plan.ErrorMessage);
        var result = await _erpPackingStockClient.WithdrawParticipantAsync(
            new ErpPackingStockParticipantWithdrawCommand(request.sellfox_task_id, request.sellfox_item_id,
                request.request_id, plan.Data.rowVersion, actor.id, actor.name, request.goods_owner_id));
        return result.IsSuccess ? (true, "库存贡献已由ERP撤回") : (false, result.ErrorMessage);
    }

    /// <summary>Starts a server-timed, single-use acknowledgement for an unmatched SKU pool.</summary>
    public Task<string> BeginSkuMismatchChallengeAsync(
        PackingTaskSkuMismatchChallengeRequest request,
        CurrentUser currentUser)
    {
        if (request.sellfox_task_id <= 0 || request.sellfox_item_id <= 0 || request.stock_id <= 0
            || request.goods_owner_id <= 0 || request.qty < 0 || request.variant < 0
            || string.IsNullOrWhiteSpace(request.request_id) || request.request_id.Trim().Length > 64)
            throw new ArgumentException("装箱任务、商品、库存、贡献参数和请求标识必须有效", nameof(request));

        var now = DateTime.UtcNow;
        foreach (var expired in SkuMismatchChallenges.Where(t => t.Value.ExpiresAtUtc <= now).Select(t => t.Key))
            SkuMismatchChallenges.TryRemove(expired, out _);
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();
        SkuMismatchChallenges[token] = new SkuMismatchChallenge(
            currentUser.user_id, request.sellfox_task_id, request.sellfox_item_id, request.stock_id,
            request.goods_owner_id, request.qty, request.variant, request.request_id.Trim(),
            now.Add(SkuMismatchMinimumReminder), now.Add(SkuMismatchChallengeLifetime));
        return Task.FromResult(token);
    }

    private static bool TryConsumeSkuMismatchChallenge(PackingTaskStockSelectRequest request, CurrentUser currentUser)
    {
        if (string.IsNullOrWhiteSpace(request.sku_mismatch_challenge)
            || !SkuMismatchChallenges.TryGetValue(request.sku_mismatch_challenge, out var challenge))
            return false;
        var now = DateTime.UtcNow;
        if (challenge.UserId != currentUser.user_id || challenge.SellfoxTaskId != request.sellfox_task_id
            || challenge.SellfoxItemId != request.sellfox_item_id || challenge.StockId != request.stock_id
            || challenge.GoodsOwnerId != request.goods_owner_id || challenge.Quantity != request.qty
            || challenge.Variant != request.variant
            || !string.Equals(challenge.RequestId, request.request_id, StringComparison.Ordinal)
            || now < challenge.NotBeforeUtc || now > challenge.ExpiresAtUtc)
            return false;
        // The same frozen command may be retried; changed owner, quantity, variant or request id is rejected.
        return true;
    }

    private static (string id, string name) BuildActor(CurrentUser currentUser) =>
        (currentUser.user_id.ToString(System.Globalization.CultureInfo.InvariantCulture),
            string.IsNullOrWhiteSpace(currentUser.user_name) ? $"用户{currentUser.user_id}" : currentUser.user_name.Trim());

    private static int ToInt(long value) => value <= 0 ? 0 : value >= int.MaxValue ? int.MaxValue : (int)value;

    private static string CommandRequestId(string requestId, string operation)
    {
        const int maximumLength = 64;
        var suffix = $":{operation}";
        return requestId.Length + suffix.Length <= maximumLength
            ? requestId + suffix
            : requestId[..(maximumLength - suffix.Length)] + suffix;
    }

    private sealed record SkuMismatchChallenge(long UserId, long SellfoxTaskId, long SellfoxItemId,
        long StockId, int GoodsOwnerId, int Quantity, int Variant, string RequestId,
        DateTime NotBeforeUtc, DateTime ExpiresAtUtc);

    private static PackingTaskQueryItemViewModel BuildItemViewModel(
        ErpPackingTaskItemEntity item,
        IReadOnlyDictionary<long, PackingTaskStockAvailability> availabilityByItemId)
    {
        var availability = availabilityByItemId.GetValueOrDefault(item.id);
        return new PackingTaskQueryItemViewModel
        {
            id = item.id,
            sellfox_item_id = item.sellfox_item_id,
            commodity_id = item.commodity_id,
            commodity_sku = item.commodity_sku,
            commodity_name = item.commodity_name,
            main_image = item.main_image,
            fn_sku = item.fn_sku,
            sku = item.sku,
            msku = item.msku,
            task_num = item.task_num,
            quantity_shipped = item.quantity_shipped,
            stock_available = availability?.AvailableQty,
            stock_sku_code = availability?.SkuCode,
            stock_qty = availability?.StockQty,
            stock_available_qty = availability?.AvailableQty,
            locked_qty = availability?.LockedQty
        };
    }

    private static PackingTaskQueryResult Failure(string message) => new(false, message, [], 0);

    private static string FindSearchText(PageSearch pageSearch, string name) =>
        pageSearch.searchObjects.FirstOrDefault(t =>
            string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase))?.Text?.Trim() ?? string.Empty;

    private sealed class DapperPackingTaskQueryDataSource(
        IMySqlConnectionFactory connectionFactory,
        IStockAllocationMutationService? stockAllocationMutationService = null)
        : IPackingTaskQueryDataSource
    {
        private readonly IMySqlConnectionFactory _connectionFactory = connectionFactory
            ?? throw new ArgumentNullException(nameof(connectionFactory));
        private readonly IStockAllocationMutationService? _stockAllocationMutationService = stockAllocationMutationService;

        private const string GroupMemberNamesSql = """
            WITH RECURSIVE dept_tree AS (
                SELECT d.`id`, 0 AS depth
                FROM `system_dept` d
                WHERE d.`id`=@GroupId AND d.`deleted`=0 AND d.`status`=0 AND d.`dept`='operator'
                UNION ALL
                SELECT c.`id`, t.depth + 1
                FROM `system_dept` c
                JOIN dept_tree t ON c.`parent_id` = t.`id`
                WHERE c.`deleted`=0 AND c.`status`=0 AND t.depth < 20
            )
            SELECT DISTINCT u.`nickname` FROM `system_users` u
            JOIN dept_tree t ON u.`dept_id` = t.`id`
            WHERE u.`deleted`=0 AND u.`status`=0 AND u.`nickname` IS NOT NULL AND u.`nickname` <> '';
            """;

        public async Task<PackingTaskPageData> LoadPageAsync(PackingTaskPageRequest request)
        {
            var parameters = new DynamicParameters();
            parameters.Add("HasKeyword", string.IsNullOrEmpty(request.Keyword) ? 0 : 1);
            parameters.Add("Keyword", request.Keyword);
            parameters.Add("WarehouseId", request.WarehouseId);
            parameters.Add("GroupId", request.GroupId);
            parameters.Add("MemberId", request.MemberId);
            parameters.Add("Offset", request.Offset);
            parameters.Add("PageSize", request.PageSize);

            await using var connection = await _connectionFactory.OpenConnectionAsync();

            var extraWhere = string.Empty;
            if (request.GroupId > 0)
            {
                var groupNames = (await connection.QueryAsync<string>(
                    GroupMemberNamesSql, new { GroupId = request.GroupId })).AsList();
                if (groupNames.Count == 0)
                {
                    return new PackingTaskPageData([], [],
                        new Dictionary<long, PackingTaskStockAvailability>(), 0);
                }
                parameters.Add("GroupMemberNames", groupNames);
                extraWhere += " AND task.create_name IN @GroupMemberNames";
            }

            if (request.MemberId > 0)
            {
                var memberName = await connection.QuerySingleOrDefaultAsync<string>(
                    "SELECT nickname FROM system_users WHERE id=@MemberId AND deleted=0 AND status=0 LIMIT 1",
                    new { MemberId = request.MemberId });
                if (string.IsNullOrEmpty(memberName))
                {
                    return new PackingTaskPageData([], [],
                        new Dictionary<long, PackingTaskStockAvailability>(), 0);
                }
                parameters.Add("MemberName", memberName);
                extraWhere += " AND task.create_name = @MemberName";
            }

            var whereSql = $"""
                FROM ruiyi_sellfox_packing_task AS task
                WHERE task.source_deleted = 0
                  AND task.source_canceled = 0
                  AND (@WarehouseId IS NULL OR task.warehouse_id = @WarehouseId)
                  AND NOT EXISTS (
                    SELECT 1 FROM wms_dispatch_packing_task AS active_task
                    WHERE active_task.active_source_task_id = task.sellfox_task_id)
                  AND (@HasKeyword = 0
                    OR LOCATE(@Keyword, task.packing_task_sn) > 0
                    OR EXISTS (
                      SELECT 1 FROM ruiyi_sellfox_packing_task_item AS search_item
                      WHERE search_item.sellfox_task_id = task.sellfox_task_id
                        AND search_item.source_deleted = 0
                        AND (LOCATE(@Keyword, search_item.commodity_name) > 0
                          OR LOCATE(@Keyword, search_item.commodity_sku) > 0
                          OR LOCATE(@Keyword, search_item.sku) > 0
                          OR LOCATE(@Keyword, search_item.fn_sku) > 0)))
                {extraWhere}
                """;
            var totals = await connection.QuerySingleAsync<int>(
                $"SELECT COUNT(*) {whereSql}", parameters);
            var tasks = (await connection.QueryAsync<ErpPackingTaskEntity>($"""
                SELECT task.id, task.sellfox_task_id, task.packing_task_sn, task.warehouse_id,
                       task.warehouse_name, task.complete_num, task.task_num, task.create_name,
                       task.source_create_time, task.item_count, task.shop_name, task.marketplace_name
                {whereSql}
                ORDER BY task.source_create_time DESC, task.id DESC
                LIMIT @PageSize OFFSET @Offset
                """, parameters)).AsList();
            if (tasks.Count == 0)
            {
                return new PackingTaskPageData([], [],
                    new Dictionary<long, PackingTaskStockAvailability>(), totals);
            }

            var taskIds = tasks.Select(t => t.sellfox_task_id).ToArray();
            if (tasks.Any(t => t.warehouse_id is null or <= 0))
                throw new InvalidOperationException("装箱任务缺少ERP仓库，库存查询已拒绝");
            var items = (await connection.QueryAsync<ErpPackingTaskItemEntity>("""
                SELECT id, sellfox_item_id, sellfox_task_id, commodity_id, commodity_sku,
                       commodity_name, main_image, fn_sku, sku, msku, task_num,
                       quantity_shipped, stock_available
                FROM ruiyi_sellfox_packing_task_item
                WHERE source_deleted = 0 AND sellfox_task_id IN @TaskIds
                ORDER BY id
                """, new { TaskIds = taskIds })).AsList();
            var canonicalTaskIds = new HashSet<long>();
            foreach (var warehouseGroup in tasks.GroupBy(t => t.warehouse_id!.Value))
            {
                var runtime = await LoadRuntimeAsync(connection, null, warehouseGroup.Key);
                EnsureRuntimeReadable(runtime, warehouseGroup.Key);
                if (runtime.Mode == CanonicalMode)
                    foreach (var task in warehouseGroup) canonicalTaskIds.Add(task.sellfox_task_id);
            }
            var legacyTaskIds = taskIds.Where(id => !canonicalTaskIds.Contains(id)).ToArray();
            var availabilityRows = new List<AvailabilityRow>();
            if (legacyTaskIds.Length > 0)
                availabilityRows.AddRange((await connection.QueryAsync<AvailabilityRow>("""
                SELECT item.id AS ItemId,
                       SUBSTRING_INDEX(COALESCE(item.commodity_sku, ''), '-', 1) AS SkuCode,
                       COALESCE(SUM(CASE WHEN stock.is_freeze = 0 THEN stock.qty ELSE 0 END), 0) AS StockQty,
                       COALESCE(SUM(CASE WHEN stock.is_freeze = 0 THEN GREATEST(0, stock.qty
                         - COALESCE((SELECT SUM(p.pick_qty) FROM wms_dispatchpicklist p
                             INNER JOIN wms_dispatchlist d ON d.id = p.dispatchlist_id
                             WHERE d.dispatch_status > 1 AND d.dispatch_status < 6
                               AND p.stock_id = stock.id), 0)
                         - COALESCE((SELECT SUM(p.qty) FROM wms_stockprocessdetail p
                             WHERE p.is_update_stock = 0 AND p.sku_id = stock.sku_id
                               AND p.goods_location_id = stock.goods_location_id
                               AND p.goods_owner_id = stock.goods_owner_id), 0)
                         - COALESCE((SELECT SUM(m.qty) FROM wms_stockmove m
                             WHERE m.move_status = 0 AND m.sku_id = stock.sku_id
                               AND m.orig_goods_location_id = stock.goods_location_id
                               AND m.goods_owner_id = stock.goods_owner_id), 0)
                       ) ELSE 0 END), 0) AS AvailableQty,
                       0 AS LockedQty
                FROM ruiyi_sellfox_packing_task_item AS item
                INNER JOIN ruiyi_sellfox_packing_task AS task
                  ON task.sellfox_task_id = item.sellfox_task_id
                INNER JOIN wms_warehouse AS warehouse
                  ON warehouse.erp_warehouse_id = task.warehouse_id AND warehouse.is_valid = 1
                LEFT JOIN wms_sku AS sku
                  ON SUBSTRING_INDEX(sku.sku_code, '-', 1)
                   = SUBSTRING_INDEX(COALESCE(item.commodity_sku, ''), '-', 1)
                LEFT JOIN wms_stock AS stock
                  ON stock.sku_id = sku.id
                LEFT JOIN wms_goodslocation AS location
                  ON location.id = stock.goods_location_id AND location.warehouse_id = warehouse.id
                 AND location.is_valid = 1 AND location.warehouse_area_property <> 5
                LEFT JOIN wms_goodsowner AS owner
                  ON owner.id = stock.goods_owner_id
                WHERE item.source_deleted = 0 AND item.sellfox_task_id IN @TaskIds
                  AND (stock.id IS NULL OR (location.id IS NOT NULL
                    AND task.create_name <> ''
                    AND owner.goods_owner_name LIKE CONCAT('%', task.create_name, '%')))
                GROUP BY item.id, SUBSTRING_INDEX(COALESCE(item.commodity_sku, ''), '-', 1)
                """, new { TaskIds = legacyTaskIds })).AsList());
            if (canonicalTaskIds.Count > 0)
                availabilityRows.AddRange((await connection.QueryAsync<AvailabilityRow>("""
                    SELECT item.`id` ItemId,
                           SUBSTRING_INDEX(COALESCE(item.`commodity_sku`,''),'-',1) SkuCode,
                           COALESCE(SUM(CASE WHEN sku.`id` IS NOT NULL AND allocation.`location_state`='ACTIVE'
                             AND location.`id` IS NOT NULL
                             AND task.`create_name`<>''
                             AND owner.`goods_owner_name` LIKE CONCAT('%',task.`create_name`,'%')
                             THEN allocation.`allocated_qty` ELSE 0 END),0) StockQty,
                           COALESCE(SUM(CASE WHEN sku.`id` IS NOT NULL AND allocation.`location_state`='ACTIVE'
                             AND location.`id` IS NOT NULL
                             AND task.`create_name`<>''
                             AND owner.`goods_owner_name` LIKE CONCAT('%',task.`create_name`,'%')
                             THEN allocation.`allocated_qty`-allocation.`occupied_qty` ELSE 0 END),0) AvailableQty,
                           0 LockedQty
                      FROM `ruiyi_sellfox_packing_task_item` item
                      JOIN `ruiyi_sellfox_packing_task` task ON task.`sellfox_task_id`=item.`sellfox_task_id`
                      JOIN `wms_warehouse` warehouse ON warehouse.`erp_warehouse_id`=task.`warehouse_id`
                        AND warehouse.`is_valid`=1
                      LEFT JOIN `trk_stock` stock ON stock.`warehouse_id`=task.`warehouse_id`
                        AND stock.`deleted`=b'0'
                      LEFT JOIN `wms_erp_commodity_map` map ON map.`erp_commodity_id`=stock.`commodity_id` AND map.`wms_sku_id`>0
                      LEFT JOIN `wms_sku` sku ON sku.`id`=map.`wms_sku_id`
                        AND SUBSTRING_INDEX(sku.`sku_code`,'-',1)=SUBSTRING_INDEX(COALESCE(item.`commodity_sku`,''),'-',1)
                      LEFT JOIN `wms_erp_stock_allocation` allocation
                        ON allocation.`erp_stock_id`=stock.`id`
                      LEFT JOIN `wms_goodslocation` location ON location.`id`=allocation.`goods_location_id`
                        AND location.`warehouse_id`=warehouse.`id` AND location.`is_valid`=1
                        AND location.`warehouse_area_property`<>5
                      LEFT JOIN `wms_goodsowner` owner ON owner.`id`=allocation.`goods_owner_id`
                     WHERE item.`source_deleted`=0 AND item.`sellfox_task_id` IN @TaskIds
                     GROUP BY item.`id`,SUBSTRING_INDEX(COALESCE(item.`commodity_sku`,''),'-',1);
                    """,new{TaskIds=canonicalTaskIds.ToArray()})).AsList());
            var availability = availabilityRows.ToDictionary(
                t => t.ItemId,
                t => new PackingTaskStockAvailability(t.SkuCode, t.StockQty, t.LockedQty, t.AvailableQty));
            return new PackingTaskPageData(tasks, items, availability, totals);
        }

        // The former local-selection implementation is deliberately excluded from every build. Ruoyi owns the
        // selection/reservation lifecycle and callers now reach it only through IErpPackingStockClient above.
#if LEGACY_LOCAL_PACKING_SELECTION
        public async Task<PackingTaskSelectableData?> LoadSelectableStockAsync(
            PackingTaskStockPageRequest request,
            CurrentUser currentUser)
        {
            await using var connection = await _connectionFactory.OpenConnectionAsync();
            var context = await connection.QuerySingleOrDefaultAsync<SelectableContext>("""
                SELECT warehouse.id AS WarehouseId, warehouse.warehouse_name AS WarehouseName,
                       task.warehouse_id AS ErpWarehouseId,
                       task.create_name AS CreateName,
                       SUBSTRING_INDEX(COALESCE(item.commodity_sku, ''), '-', 1) AS BaseSkuCode
                FROM ruiyi_sellfox_packing_task_item AS item
                INNER JOIN ruiyi_sellfox_packing_task AS task
                  ON task.sellfox_task_id = item.sellfox_task_id
                 AND task.source_deleted = 0 AND task.source_canceled = 0
                INNER JOIN wms_warehouse AS warehouse
                  ON warehouse.erp_warehouse_id = task.warehouse_id AND warehouse.is_valid = 1
                WHERE item.sellfox_task_id = @TaskId AND item.sellfox_item_id = @ItemId
                  AND item.source_deleted = 0
                LIMIT 1
                """, new
            {
                TaskId = request.sellfox_task_id,
                ItemId = request.sellfox_item_id
            });
            if (context == null)
            {
                return null;
            }

            var runtime = await LoadRuntimeAsync(
                connection, null, context.ErpWarehouseId);
            EnsureRuntimeReadable(runtime, context.ErpWarehouseId);
            if (runtime.Mode == CanonicalMode)
            {
                return await LoadCanonicalSelectableStockAsync(
                    connection, request, currentUser, context);
            }

            var rows = (await connection.QueryAsync<SelectableRow>("""
                SELECT stock.id AS stock_id, stock.sku_id, sku.sku_code, spu.spu_code,
                       spu.spu_name AS commodity_name,
                       COALESCE((
                         SELECT commodity.img_url
                         FROM wms_erp_commodity_map AS image_map
                         INNER JOIN erp_commodity AS commodity
                           ON commodity.id = CAST(image_map.erp_commodity_id AS CHAR)
                         WHERE image_map.wms_sku_id = stock.sku_id

                           AND commodity.img_url <> ''
                         ORDER BY image_map.id
                         LIMIT 1), '') AS main_image,
                       stock.goods_location_id, location.location_name,
                       stock.goods_owner_id, COALESCE(owner.goods_owner_name, '') AS goods_owner_name,
                       stock.qty, stock.is_freeze, stock.series_number, stock.expiry_date,
                       COALESCE(selection.selected_qty, 0) AS selected_qty,
                       COALESCE(dispatch_lock.lock_qty, 0) + COALESCE(process_lock.lock_qty, 0)
                         + COALESCE(move_lock.lock_qty, 0) + COALESCE(packing_lock.lock_qty, 0) AS locked_qty
                FROM wms_stock AS stock
                INNER JOIN wms_sku AS sku ON sku.id = stock.sku_id
                INNER JOIN wms_spu AS spu ON spu.id = sku.spu_id
                INNER JOIN wms_goodslocation AS location
                  ON location.id = stock.goods_location_id
                 AND location.warehouse_id = @WarehouseId
                 AND location.is_valid = 1 AND location.warehouse_area_property <> 5
                LEFT JOIN wms_goodsowner AS owner ON owner.id = stock.goods_owner_id
                LEFT JOIN (
                  SELECT stock_id, SUM(qty) AS selected_qty
                  FROM wms_packing_task_stock_selection
                  WHERE sellfox_task_id = @TaskId AND sellfox_item_id = @ItemId
                    AND status = 'ACTIVE'
                  GROUP BY stock_id) AS selection ON selection.stock_id = stock.id
                LEFT JOIN (
                  SELECT stock_id, SUM(qty) AS lock_qty
                  FROM wms_packing_task_stock_selection
                  WHERE status = 'ACTIVE'
                  GROUP BY stock_id) AS packing_lock ON packing_lock.stock_id = stock.id
                LEFT JOIN (
                  SELECT pick.stock_id, SUM(pick.pick_qty) AS lock_qty
                  FROM wms_dispatchpicklist AS pick
                  INNER JOIN wms_dispatchlist AS detail ON detail.id = pick.dispatchlist_id
                  WHERE detail.dispatch_status > 1 AND detail.dispatch_status < 6
                  GROUP BY pick.stock_id) AS dispatch_lock
                  ON dispatch_lock.stock_id = stock.id
                LEFT JOIN (
                  SELECT sku_id, goods_location_id, goods_owner_id, SUM(qty) AS lock_qty
                  FROM wms_stockprocessdetail WHERE is_update_stock = 0
                  GROUP BY sku_id, goods_location_id, goods_owner_id) AS process_lock
                  ON process_lock.sku_id = stock.sku_id
                 AND process_lock.goods_location_id = stock.goods_location_id
                 AND process_lock.goods_owner_id = stock.goods_owner_id
                LEFT JOIN (
                  SELECT sku_id, orig_goods_location_id, goods_owner_id, SUM(qty) AS lock_qty
                  FROM wms_stockmove WHERE move_status = 0
                  GROUP BY sku_id, orig_goods_location_id, goods_owner_id) AS move_lock
                  ON move_lock.sku_id = stock.sku_id
                 AND move_lock.orig_goods_location_id = stock.goods_location_id
                 AND move_lock.goods_owner_id = stock.goods_owner_id
                WHERE (@SearchOthers = 0
                    AND (@CreateName = '' OR owner.goods_owner_name LIKE CONCAT('%', @CreateName, '%'))
                    OR @SearchOthers = 1
                    AND (@CreateName = '' OR owner.goods_owner_name IS NULL
                      OR owner.goods_owner_name NOT LIKE CONCAT('%', @CreateName, '%')))
                  AND (@HasKeyword = 0 OR sku.sku_code LIKE @KeywordPattern
                    OR spu.spu_code LIKE @KeywordPattern OR spu.spu_name LIKE @KeywordPattern)
                  AND (@HasLocation = 0 OR location.location_name LIKE @LocationPattern)
                  AND (@HasOwner = 0 OR owner.goods_owner_name LIKE @OwnerPattern)
                """, new
            {
                TaskId = request.sellfox_task_id,
                ItemId = request.sellfox_item_id,
                context.WarehouseId,
                CreateName = (context.CreateName ?? string.Empty).Trim(),
                SearchOthers = request.search_others ? 1 : 0,
                HasKeyword = string.IsNullOrWhiteSpace(request.keyword) ? 0 : 1,
                KeywordPattern = $"%{request.keyword.Trim()}%",
                HasLocation = string.IsNullOrWhiteSpace(request.location) ? 0 : 1,
                LocationPattern = $"%{request.location.Trim()}%",
                HasOwner = string.IsNullOrWhiteSpace(request.owner) ? 0 : 1,
                OwnerPattern = $"%{request.owner.Trim()}%"
            })).AsList();
            var baseSkuCode = (context.BaseSkuCode ?? string.Empty).Trim();
            var createName = (context.CreateName ?? string.Empty).Trim();
            var resultRows = new List<SelectableStockViewModel>();
            foreach (var row in rows)
            {
                var available = row.is_freeze ? 0 : Math.Max(0, row.qty - row.locked_qty);
                var selected = row.selected_qty > 0;
                if (!selected && available <= 0)
                {
                    continue;
                }

                resultRows.Add(new SelectableStockViewModel
                {
                    stock_id = row.stock_id,
                    sku_id = row.sku_id,
                    sku_code = row.sku_code,
                    spu_code = row.spu_code,
                    commodity_name = row.commodity_name,
                    main_image = row.main_image,
                    goods_location_id = row.goods_location_id,
                    location_name = row.location_name,
                    goods_owner_id = row.goods_owner_id,
                    goods_owner_name = row.goods_owner_name,
                    qty = row.qty,
                    available_qty = available,
                    series_number = row.series_number,
                    expiry_date = row.expiry_date,
                    matched = !string.IsNullOrEmpty(baseSkuCode)
                        && string.Equals(StripVariantSuffix(row.sku_code), baseSkuCode,
                            StringComparison.OrdinalIgnoreCase),
                    selected = selected,
                    selected_qty = row.selected_qty,
                    is_creator_stock = createName.Length > 0
                        && row.goods_owner_name.Contains(createName, StringComparison.OrdinalIgnoreCase)
                });
            }

            return new PackingTaskSelectableData(resultRows, context.WarehouseId, context.WarehouseName);
        }

        private const string CanonicalMode = "CANONICAL_ERP";
        private const string LegacyMode = "LEGACY_READ";

        private async Task<PackingTaskSelectableData> LoadCanonicalSelectableStockAsync(
            IDbConnection connection,
            PackingTaskStockPageRequest request,
            CurrentUser currentUser,
            SelectableContext context)
        {
            var rows = (await connection.QueryAsync<CanonicalSelectableRow>("""
                SELECT allocation.`id` AS stock_allocation_id,
                       allocation.`erp_stock_id`,map.`wms_sku_id` AS sku_id,
                       sku.`sku_code`,spu.`spu_code`,spu.`spu_name` AS commodity_name,
                       COALESCE((SELECT commodity.`img_url`
                           FROM `wms_erp_commodity_map` image_map
                           JOIN `erp_commodity` commodity ON commodity.`id`=CAST(image_map.`erp_commodity_id` AS CHAR)
                          WHERE image_map.`wms_sku_id`=map.`wms_sku_id`
                            AND commodity.`img_url`<>'' ORDER BY image_map.`id` LIMIT 1),'') AS main_image,
                       allocation.`goods_location_id`,COALESCE(location.`location_name`,'') AS `location_name`,
                       allocation.`goods_owner_id`,COALESCE(owner.`goods_owner_name`,'') AS goods_owner_name,
                       allocation.`allocated_qty` AS qty,
                       allocation.`allocated_qty`-allocation.`occupied_qty` AS free_qty,
                       allocation.`series_number`,allocation.`expiry_date`,
                       COALESCE(selection.`selected_qty`,0) AS selected_qty
                  FROM `wms_erp_stock_allocation` allocation
                  JOIN `trk_stock` stock ON stock.`id`=allocation.`erp_stock_id`
                    AND stock.`warehouse_id`=@ErpWarehouseId AND stock.`deleted`=b'0'
                  JOIN `wms_erp_commodity_map` map ON map.`erp_commodity_id`=stock.`commodity_id` AND map.`wms_sku_id`>0
                  JOIN `wms_sku` sku ON sku.`id`=map.`wms_sku_id`
                  JOIN `wms_spu` spu ON spu.`id`=sku.`spu_id`
                  LEFT JOIN `wms_warehousearea` area ON area.`id`=allocation.`warehouse_area_id`
                    AND area.`warehouse_id`=@WarehouseId  AND area.`is_valid`=1
                  LEFT JOIN `wms_goodslocation` location ON location.`id`=allocation.`goods_location_id`
                     AND location.`warehouse_id`=@WarehouseId AND location.`is_valid`=1
                  LEFT JOIN `wms_goodsowner` owner ON owner.`id`=allocation.`goods_owner_id`
                  LEFT JOIN (
                    SELECT `stock_allocation_id`,SUM(`qty`) selected_qty
                      FROM `wms_packing_task_stock_selection`
                     WHERE `sellfox_task_id`=@TaskId
                       AND `sellfox_item_id`=@ItemId AND `stock_allocation_id` IS NOT NULL
                       AND `status`='ACTIVE'
                     GROUP BY `stock_allocation_id`) selection
                    ON selection.`stock_allocation_id`=allocation.`id`
                 WHERE allocation.`location_state`='ACTIVE'
                   AND allocation.`allocated_qty`>=allocation.`occupied_qty`
                   AND (@SearchOthers=0
                     AND (@CreateName='' OR owner.`goods_owner_name` LIKE CONCAT('%',@CreateName,'%'))
                     OR @SearchOthers=1
                     AND (@CreateName='' OR owner.`goods_owner_name` IS NULL
                       OR owner.`goods_owner_name` NOT LIKE CONCAT('%',@CreateName,'%')))
                   AND (@HasKeyword=0 OR sku.`sku_code` LIKE @KeywordPattern
                     OR spu.`spu_code` LIKE @KeywordPattern OR spu.`spu_name` LIKE @KeywordPattern)
                   AND (allocation.`warehouse_area_id` IS NULL OR area.`id` IS NOT NULL)
                   AND (allocation.`goods_location_id` IS NULL OR location.`id` IS NOT NULL)
                   AND COALESCE(location.`warehouse_area_property`,area.`area_property`,0)<>5
                   AND (@HasLocation=0 OR COALESCE(location.`location_name`,'') LIKE @LocationPattern)
                   AND (@HasOwner=0 OR owner.`goods_owner_name` LIKE @OwnerPattern)
                 ORDER BY allocation.`id`;
                """, new
            {
                TaskId = request.sellfox_task_id,
                ItemId = request.sellfox_item_id,
                context.ErpWarehouseId,
                context.WarehouseId,
                CreateName = (context.CreateName ?? string.Empty).Trim(),
                SearchOthers = request.search_others ? 1 : 0,
                HasKeyword = string.IsNullOrWhiteSpace(request.keyword) ? 0 : 1,
                KeywordPattern = $"%{request.keyword.Trim()}%",
                HasLocation = string.IsNullOrWhiteSpace(request.location) ? 0 : 1,
                LocationPattern = $"%{request.location.Trim()}%",
                HasOwner = string.IsNullOrWhiteSpace(request.owner) ? 0 : 1,
                OwnerPattern = $"%{request.owner.Trim()}%"
            })).AsList();
            var baseSkuCode = (context.BaseSkuCode ?? string.Empty).Trim();
            var createName = (context.CreateName ?? string.Empty).Trim();
            var result = rows.Where(row => row.selected_qty > 0 || row.free_qty > 0).Select(row =>
                new SelectableStockViewModel
                {
                    stock_id = 0,
                    erp_stock_id = row.erp_stock_id,
                    stock_allocation_id = row.stock_allocation_id,
                    sku_id = row.sku_id,
                    sku_code = row.sku_code,
                    spu_code = row.spu_code,
                    commodity_name = row.commodity_name,
                    main_image = row.main_image,
                    goods_location_id = row.goods_location_id,
                    location_name = row.location_name,
                    goods_owner_id = row.goods_owner_id,
                    goods_owner_name = row.goods_owner_name,
                    qty = checked((int)row.qty),
                    available_qty = checked((int)row.free_qty),
                    series_number = row.series_number,
                    expiry_date = row.expiry_date,
                    matched = !string.IsNullOrEmpty(baseSkuCode)
                        && string.Equals(StripVariantSuffix(row.sku_code), baseSkuCode,
                            StringComparison.OrdinalIgnoreCase),
                    selected = row.selected_qty > 0,
                    selected_qty = checked((int)row.selected_qty),
                    is_creator_stock = createName.Length > 0
                        && row.goods_owner_name.Contains(createName, StringComparison.OrdinalIgnoreCase)
                }).ToList();
            return new PackingTaskSelectableData(result, context.WarehouseId, context.WarehouseName);
        }

        public async Task<PackingTaskStockSaveResult> SaveSelectionAsync(
            PackingTaskStockSelectRequest request,
            CurrentUser currentUser)
        {
            await using var connection = await _connectionFactory.OpenConnectionAsync();
            await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted);
            try
            {
                var taskContext = await connection.QuerySingleOrDefaultAsync<SelectionTaskContext>("""
                    SELECT task.packing_task_sn AS task_no, task.create_name AS create_name,
                           task.warehouse_id AS erp_warehouse_id,
                           item.commodity_name AS commodity_name, item.task_num AS task_num
                    FROM ruiyi_sellfox_packing_task_item AS item
                    INNER JOIN ruiyi_sellfox_packing_task AS task
                      ON task.sellfox_task_id = item.sellfox_task_id
                    WHERE item.sellfox_item_id = @ItemId AND item.sellfox_task_id = @TaskId
                      AND item.source_deleted = 0
                    LIMIT 1 FOR UPDATE
                    """, new { ItemId = request.sellfox_item_id, TaskId = request.sellfox_task_id }, transaction);
                if (taskContext == null)
                {
                    return await RollbackResultAsync(transaction, "装箱任务明细不存在");
                }

                var runtime = await LoadRuntimeAsync(connection, transaction,
                    taskContext.erp_warehouse_id);
                EnsureRuntimeWritable(runtime, taskContext.erp_warehouse_id);
                if (runtime.Mode == CanonicalMode)
                {
                    return await SaveCanonicalSelectionAsync(
                        connection, transaction, request, currentUser, taskContext);
                }

                var stock = await connection.QuerySingleOrDefaultAsync<SelectionStockRow>("""
                    SELECT stock.id, stock.sku_id, stock.goods_location_id, stock.goods_owner_id,
                           stock.qty, stock.is_freeze, COALESCE(sku.sku_code, '') AS sku_code,
                           COALESCE(owner.goods_owner_name, '') AS goods_owner_name
                    FROM wms_stock AS stock
                    LEFT JOIN wms_sku AS sku ON sku.id = stock.sku_id
                    LEFT JOIN wms_goodsowner AS owner ON owner.id = stock.goods_owner_id
                    WHERE stock.id = @StockId
                    FOR UPDATE
                    """, new { StockId = request.stock_id }, transaction);
                if (stock == null)
                {
                    return await RollbackResultAsync(transaction, "库存不存在");
                }
                if (stock.is_freeze)
                {
                    return await RollbackResultAsync(transaction, "该库存已冻结，不能选择");
                }

                var existingId = await connection.QuerySingleOrDefaultAsync<int?>("""
                    SELECT id FROM wms_packing_task_stock_selection
                    WHERE sellfox_task_id = @TaskId
                      AND sellfox_item_id = @ItemId AND stock_id = @StockId
                      AND status = 'ACTIVE'
                    ORDER BY id LIMIT 1 FOR UPDATE
                    """, new
                {
                    TaskId = request.sellfox_task_id,
                    ItemId = request.sellfox_item_id,
                    StockId = request.stock_id
                }, transaction);
                var ownLockedQty = existingId == null
                    ? 0
                    : await connection.ExecuteScalarAsync<int>("""
                        SELECT qty FROM wms_packing_task_stock_selection
                        WHERE id = @Id AND status = 'ACTIVE';
                        """, new { Id = existingId }, transaction);

                // 服务端重算可用量并直接锁定：qty - 在途/加工/移库/已选择锁定 - 本次已有锁定。
                var lockedTotal = await connection.ExecuteScalarAsync<int>("""
                    SELECT
                      COALESCE((SELECT SUM(p.`pick_qty`) FROM `wms_dispatchpicklist` p
                        JOIN `wms_dispatchlist` d ON d.`id` = p.`dispatchlist_id`
                        WHERE d.`dispatch_status` > 1 AND d.`dispatch_status` < 6
                          AND p.`stock_id` = @StockId), 0)
                      + COALESCE((SELECT SUM(p.`qty`) FROM `wms_stockprocessdetail` p
                        WHERE p.`is_update_stock` = 0 AND p.`sku_id` = @SkuId
                          AND p.`goods_location_id` = @LocationId AND p.`goods_owner_id` = @OwnerId), 0)
                      + COALESCE((SELECT SUM(m.`qty`) FROM `wms_stockmove` m
                        WHERE m.`move_status` = 0 AND m.`sku_id` = @SkuId
                          AND m.`orig_goods_location_id` = @LocationId AND m.`goods_owner_id` = @OwnerId), 0)
                      + COALESCE((SELECT SUM(qty) FROM `wms_packing_task_stock_selection`
                        WHERE stock_id = @StockId
                          AND status = 'ACTIVE'), 0)
                    """, new
                {
                    SkuId = stock.sku_id,
                    LocationId = stock.goods_location_id,
                    OwnerId = stock.goods_owner_id,
                    TaskId = request.sellfox_task_id,
                    ItemId = request.sellfox_item_id,
                    StockId = request.stock_id
                }, transaction);
                var availableQty = Math.Max(0, stock.qty - lockedTotal + ownLockedQty);
                if (availableQty <= 0)
                {
                    return await RollbackResultAsync(transaction, "该库存可用量不足，不能选择");
                }
                if (taskContext.task_num is null or <= 0 || request.variant <= 0)
                {
                    return await RollbackResultAsync(transaction, "变体数量必须大于0");
                }
                var calculatedQty = (long)taskContext.task_num.Value * request.variant;
                if (calculatedQty > int.MaxValue)
                {
                    return await RollbackResultAsync(transaction, "可用量不足");
                }
                var lockedQty = (int)calculatedQty;
                if (lockedQty > availableQty)
                {
                    return await RollbackResultAsync(transaction, "可用量不足");
                }

                var createName = (taskContext.create_name ?? string.Empty).Trim();
                var isCreatorStock = createName.Length > 0
                    && stock.goods_owner_name.Contains(createName, StringComparison.OrdinalIgnoreCase);

                var values = new
                {
                    Id = existingId,
                    TaskId = request.sellfox_task_id,
                    ItemId = request.sellfox_item_id,
                    WmsSkuId = stock.sku_id,
                    StockId = request.stock_id,
                    qty = lockedQty,
                    stock.goods_location_id,
                    stock.goods_owner_id,
                    SkuCode = stock.sku_code,
                    SelectedBy = currentUser.user_id,
                    SelectedByName = currentUser.user_name ?? string.Empty,
                    Now = DateTime.Now
                };
                if (existingId == null)
                {
                    await connection.ExecuteAsync("""
                        INSERT INTO wms_packing_task_stock_selection
                          (sellfox_task_id, sellfox_item_id, wms_sku_id, stock_id, qty,
                           goods_location_id, goods_owner_id, sku_code, selected_by, selected_by_name,
                           create_time, last_update_time, status, operation_source)
                        VALUES
                          (@TaskId, @ItemId, @WmsSkuId, @StockId, @qty,
                           @goods_location_id, @goods_owner_id, @SkuCode, @SelectedBy, @SelectedByName,
                           @Now, @Now, 'ACTIVE', 'MODERN_WMS')
                        """, values, transaction);
                }
                else
                {
                    await connection.ExecuteAsync("""
                        UPDATE wms_packing_task_stock_selection
                        SET wms_sku_id = @WmsSkuId, qty = @qty,
                            goods_location_id = @goods_location_id, goods_owner_id = @goods_owner_id,
                            sku_code = @SkuCode, selected_by = @SelectedBy,
                            selected_by_name = @SelectedByName, last_update_time = @Now,
                            operation_source = 'MODERN_WMS', row_version = row_version + 1
                        WHERE id = @Id AND status = 'ACTIVE'
                        """, values, transaction);
                }

                // 选择他人库存：理论允许，但必须记录日志。
                if (!isCreatorStock)
                {
                    await connection.ExecuteAsync("""
                        INSERT INTO `wms_action_log`
                            (`vue_path`, `user_name`, `action_content`, `action_time`)
                        VALUES
                            (@vue_path, @user_name, @action_content, @action_time)
                        """, new
                    {
                        vue_path = "deliveryManagement/deliveryManagement",
                        user_name = currentUser.user_name,
                        action_content = $"装箱任务{taskContext.task_no}选择他人库存：商品{taskContext.commodity_name} " +
                            $"SKU{stock.sku_code} 库存行{stock.id}（所属人{stock.goods_owner_name}）锁定数量{lockedQty}，创建人{createName}",
                        action_time = DateTime.Now,
                    }, transaction);
                }

                await transaction.CommitAsync();
                return new PackingTaskStockSaveResult(true, "库存选择成功");
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }

        public async Task<PackingTaskStockSaveResult> DeleteSelectionAsync(
            PackingTaskStockSelectRequest request,
            CurrentUser currentUser)
        {
            await using var connection = await _connectionFactory.OpenConnectionAsync();
            var snapshot = await connection.QuerySingleOrDefaultAsync<DeleteSelectionSnapshot>("""
                SELECT task.`warehouse_id` AS erp_warehouse_id,
                       selection.`id` AS selection_id,selection.`stock_id`,selection.`erp_stock_id`,
                       selection.`stock_allocation_id`,selection.`reservation_id`,
                       selection.`reservation_item_id`,selection.`qty`
                  FROM `ruiyi_sellfox_packing_task_item` item
                  JOIN `ruiyi_sellfox_packing_task` task
                    ON task.`sellfox_task_id`=item.`sellfox_task_id`
                  LEFT JOIN `wms_packing_task_stock_selection` selection
                    ON selection.`sellfox_task_id`=item.`sellfox_task_id`
                   AND selection.`sellfox_item_id`=item.`sellfox_item_id`
                   AND selection.`status`='ACTIVE'
                   AND ((@AllocationId IS NOT NULL AND @AllocationId>0
                         AND selection.`stock_allocation_id`=@AllocationId)
                     OR ((@AllocationId IS NULL OR @AllocationId<=0)
                         AND selection.`stock_allocation_id` IS NULL AND selection.`stock_id`=@StockId))
                 WHERE item.`sellfox_item_id`=@ItemId AND item.`sellfox_task_id`=@TaskId
                   AND item.`source_deleted`=0
                 ORDER BY selection.`id` LIMIT 1;
                """,new
            {
                TaskId=request.sellfox_task_id,
                ItemId=request.sellfox_item_id,
                AllocationId=request.stock_allocation_id,
                StockId=request.stock_id
            });
            if(snapshot==null)
                return new PackingTaskStockSaveResult(false,"装箱任务明细不存在");

            await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted);
            try
            {
                var taskContext=await connection.QuerySingleOrDefaultAsync<SelectionTaskContext>("""
                    SELECT task.packing_task_sn AS task_no,task.create_name AS create_name,
                           task.warehouse_id AS erp_warehouse_id,
                           item.commodity_name AS commodity_name,item.task_num AS task_num
                      FROM ruiyi_sellfox_packing_task_item AS item
                      JOIN ruiyi_sellfox_packing_task AS task
                        ON task.sellfox_task_id=item.sellfox_task_id
                     WHERE item.sellfox_item_id=@ItemId AND item.sellfox_task_id=@TaskId
                       AND item.source_deleted=0
                     LIMIT 1 FOR UPDATE;
                    """,new{ItemId=request.sellfox_item_id,TaskId=request.sellfox_task_id},transaction);
                if(taskContext==null)
                    return await RollbackResultAsync(transaction,"装箱任务明细不存在");
                if(taskContext.erp_warehouse_id!=snapshot.erp_warehouse_id)
                    return await RollbackResultAsync(transaction,"装箱任务仓库已变化，请刷新后重试");

                var runtime=await LoadRuntimeAsync(connection,transaction,
                    taskContext.erp_warehouse_id);
                EnsureRuntimeWritable(runtime,taskContext.erp_warehouse_id);
                if(runtime.Mode==CanonicalMode)
                {
                    if(request.stock_allocation_id is not > 0)
                        return await RollbackResultAsync(transaction,
                            "统一ERP库存模式必须提交 stock_allocation_id，已拒绝访问旧库存选择");
                    if(snapshot.selection_id is null)
                        return await RollbackResultAsync(transaction,"该库存未在选择中");
                    if(snapshot.erp_stock_id is not >0||snapshot.stock_allocation_id is not >0||snapshot.qty is not >0)
                        return await RollbackResultAsync(transaction,
                            "库存选择缺少ERP库存引用，已拒绝释放；请先修复历史绑定");
                    var locked=await LockDeleteSelectionAsync(connection,transaction,snapshot.selection_id.Value,
                        request);
                    if(!MatchesDeleteSnapshot(locked,snapshot))
                        return await RollbackResultAsync(transaction,"库存选择已变化，请刷新后重试");
                    var sequence = await NextPackingMutationSequenceAsync(connection, transaction,
                        request.sellfox_task_id, request.sellfox_item_id,
                        locked!.stock_allocation_id!.Value);
                    await _stockAllocationMutationService.ReleaseAsync(
                        connection, transaction,
                        BuildMutationContext(currentUser,request,taskContext.erp_warehouse_id,"PACKING_RELEASE",
                            "RELEASE",locked.qty,locked.stock_allocation_id.Value,sequence:sequence,
                            reservationId:locked.reservation_id,reservationItemId:locked.reservation_item_id),
                        locked.erp_stock_id!.Value,locked.stock_allocation_id.Value,locked.qty);
                    var affected=await connection.ExecuteAsync("""
                        UPDATE `wms_packing_task_stock_selection`
                           SET `status`='CANCELLED',`cancelled_by`=@CancelledBy,
                               `cancelled_by_name`=@CancelledByName,`cancelled_at`=@Now,
                               `cancel_reason`='用户取消装箱任务库存选择',
                               `operation_source`='WMS_MANUAL_CANCEL',
                               `last_update_time`=@Now,`row_version`=`row_version`+1
                         WHERE `id`=@Id  AND `erp_stock_id`=@ErpStockId
                           AND `stock_allocation_id`=@AllocationId AND `qty`=@Qty
                           AND `status`='ACTIVE';
                        """,new
                    {
                        Id=locked.id,ErpStockId=locked.erp_stock_id,
                        AllocationId=locked.stock_allocation_id,Qty=locked.qty,
                        CancelledBy=currentUser.user_id,
                        CancelledByName=currentUser.user_name??string.Empty,Now=DateTime.Now
                    },transaction);
                    if(affected!=1)
                        return await RollbackResultAsync(transaction,"库存选择已变化，请刷新后重试");
                    await transaction.CommitAsync();
                    return new PackingTaskStockSaveResult(true, "已取消选择，锁定库存已释放");
                }

                if(request.stock_allocation_id is >0||snapshot.stock_allocation_id is >0)
                    return await RollbackResultAsync(transaction,
                        "旧库存模式不允许删除ERP库位分配选择，已拒绝操作");
                if(snapshot.selection_id is null)
                    return await RollbackResultAsync(transaction,"该库存未在选择中");
                var legacyLocked=await LockDeleteSelectionAsync(connection,transaction,snapshot.selection_id.Value,
                    request);
                if(!MatchesDeleteSnapshot(legacyLocked,snapshot)||legacyLocked!.stock_allocation_id is not null)
                    return await RollbackResultAsync(transaction,"库存选择已变化，请刷新后重试");
                // 旧模式没有独立占用余额；取消选择后旧可用量查询不再扣除此活动行。
                var legacyAffected=await connection.ExecuteAsync("""
                    UPDATE `wms_packing_task_stock_selection`
                       SET `status`='CANCELLED',`cancelled_by`=@CancelledBy,
                           `cancelled_by_name`=@CancelledByName,`cancelled_at`=@Now,
                           `cancel_reason`='用户取消装箱任务库存选择',
                           `operation_source`='WMS_MANUAL_CANCEL',
                           `last_update_time`=@Now,`row_version`=`row_version`+1
                     WHERE `id`=@Id  AND `stock_allocation_id` IS NULL
                       AND `stock_id`=@StockId AND `qty`=@Qty AND `status`='ACTIVE';
                    """,new
                {
                    Id=legacyLocked.id,StockId=legacyLocked.stock_id,Qty=legacyLocked.qty,
                    CancelledBy=currentUser.user_id,
                    CancelledByName=currentUser.user_name??string.Empty,Now=DateTime.Now
                },transaction);
                if(legacyAffected!=1)
                    return await RollbackResultAsync(transaction,"库存选择已变化，请刷新后重试");
                await transaction.CommitAsync();
                return new PackingTaskStockSaveResult(true,"已取消选择，锁定库存已释放");
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }

        private static Task<DeleteSelectionLockedRow?> LockDeleteSelectionAsync(
            IDbConnection connection,IDbTransaction transaction,int selectionId,
            PackingTaskStockSelectRequest request)=>
            connection.QuerySingleOrDefaultAsync<DeleteSelectionLockedRow>("""
                SELECT `id`,`stock_id`,`erp_stock_id`,`stock_allocation_id`,`reservation_id`,
                       `reservation_item_id`,`qty`
                  FROM `wms_packing_task_stock_selection`
                 WHERE `id`=@SelectionId
                   AND `sellfox_task_id`=@TaskId AND `sellfox_item_id`=@ItemId
                   AND `status`='ACTIVE'
                 LIMIT 1 FOR UPDATE;
                """,new
            {
                SelectionId=selectionId,TaskId=request.sellfox_task_id,ItemId=request.sellfox_item_id
            },transaction);

        private static bool MatchesDeleteSnapshot(DeleteSelectionLockedRow? locked,DeleteSelectionSnapshot snapshot)=>
            locked!=null&&snapshot.selection_id==locked.id&&snapshot.stock_id==locked.stock_id
            &&snapshot.erp_stock_id==locked.erp_stock_id
            &&snapshot.stock_allocation_id==locked.stock_allocation_id
            &&snapshot.reservation_id==locked.reservation_id
            &&snapshot.reservation_item_id==locked.reservation_item_id&&snapshot.qty==locked.qty;

        private async Task<PackingTaskStockSaveResult> SaveCanonicalSelectionAsync(
            IDbConnection connection,
            System.Data.Common.DbTransaction transaction,
            PackingTaskStockSelectRequest request,
            CurrentUser currentUser,
            SelectionTaskContext taskContext)
        {
            if (request.erp_stock_id is not > 0 || request.stock_allocation_id is not > 0)
                return await RollbackResultAsync(transaction,
                    "统一ERP库存模式必须提交 erp_stock_id 和 stock_allocation_id");
            if (taskContext.task_num is null or <= 0 || request.variant <= 0)
                return await RollbackResultAsync(transaction, "变体数量必须大于0");
            var calculatedQty = checked((long)taskContext.task_num.Value * request.variant);
            if (calculatedQty > int.MaxValue)
                return await RollbackResultAsync(transaction, "可用量不足");

            var existing = await connection.QuerySingleOrDefaultAsync<CanonicalSelectionRow>("""
                SELECT `id`,`erp_stock_id`,`stock_allocation_id`,`reservation_id`,
                       `reservation_item_id`,`qty`
                  FROM `wms_packing_task_stock_selection`
                 WHERE `sellfox_task_id`=@TaskId
                   AND `sellfox_item_id`=@ItemId AND `stock_allocation_id`=@AllocationId
                   AND `status`='ACTIVE'
                 ORDER BY `id` LIMIT 1 FOR UPDATE;
                """, new
            {
                TaskId = request.sellfox_task_id,
                ItemId = request.sellfox_item_id,
                AllocationId = request.stock_allocation_id.Value
            }, transaction);
            var oldQty = existing?.qty ?? 0;
            var newQty = checked((int)calculatedQty);
            var delta = newQty - oldQty;
            var sequence = delta == 0 ? 0 : await NextPackingMutationSequenceAsync(
                connection, transaction, request.sellfox_task_id,
                request.sellfox_item_id, request.stock_allocation_id.Value);
            var mutationContext = delta == 0 ? null : BuildMutationContext(
                currentUser,request,taskContext.erp_warehouse_id,
                delta > 0 ? "PACKING_LOCK" : "PACKING_RELEASE",
                delta > 0 ? "RESERVE" : "RELEASE",Math.Abs((long)delta),
                request.stock_allocation_id.Value,oldQty,newQty,sequence,
                existing?.reservation_id,existing?.reservation_item_id);
            if (mutationContext != null)
                await _stockAllocationMutationService.PrelockReservationOwnersAsync(
                    connection,transaction,[taskContext.erp_warehouse_id],
                    [new StockReservationPrelockRequest(mutationContext,request.erp_stock_id.Value,
                        request.stock_allocation_id.Value,delta > 0 ? "LOCK" : "UNLOCK")]);

            var stock = await connection.QuerySingleOrDefaultAsync<CanonicalSelectionStockRow>("""
                SELECT allocation.`id` AS stock_allocation_id,allocation.`erp_stock_id`,
                       map.`wms_sku_id` AS sku_id,sku.`sku_code`,allocation.`goods_location_id`,
                       allocation.`goods_owner_id`,COALESCE(owner.`goods_owner_name`,'') goods_owner_name,
                       allocation.`allocated_qty`,allocation.`occupied_qty`,allocation.`location_state`
                  FROM `trk_stock` stock
                  JOIN `wms_erp_stock_allocation` allocation
                    ON allocation.`erp_stock_id`=stock.`id`
                  JOIN `wms_erp_commodity_map` map
                    ON map.`erp_commodity_id`=stock.`commodity_id` AND map.`wms_sku_id`>0
                  JOIN `wms_sku` sku ON sku.`id`=map.`wms_sku_id`
                  LEFT JOIN `wms_goodsowner` owner ON owner.`id`=allocation.`goods_owner_id`
                 WHERE stock.`id`=@ErpStockId AND stock.`warehouse_id`=@ErpWarehouseId
                   AND stock.`deleted`=b'0' AND allocation.`id`=@AllocationId
                 FOR UPDATE;
                """, new
            {
                ErpStockId = request.erp_stock_id.Value,
                taskContext.erp_warehouse_id,
                AllocationId = request.stock_allocation_id.Value
            }, transaction);
            if (stock == null)
                return await RollbackResultAsync(transaction, "ERP库存或库位分配不存在");
            if (!string.Equals(stock.location_state, "ACTIVE", StringComparison.Ordinal))
                return await RollbackResultAsync(transaction, "待确认库位或已停用库位不可选择");

            if (existing != null && existing.erp_stock_id != stock.erp_stock_id)
                return await RollbackResultAsync(transaction, "库存选择引用已变化，已拒绝更新");
            StockAllocationMutationResult? mutationResult = null;
            if (delta > 0)
            {
                mutationResult = await _stockAllocationMutationService.ReserveAsync(
                    connection, transaction,
                    mutationContext!,
                    stock.erp_stock_id, stock.stock_allocation_id, delta);
            }
            else if (delta < 0)
            {
                mutationResult = await _stockAllocationMutationService.ReleaseAsync(
                    connection, transaction,
                    mutationContext!,
                    stock.erp_stock_id, stock.stock_allocation_id, -delta);
            }

            var values = new
            {
                Id = existing?.id,
                TaskId = request.sellfox_task_id,
                ItemId = request.sellfox_item_id,
                WmsSkuId = stock.sku_id,
                ErpStockId = stock.erp_stock_id,
                AllocationId = stock.stock_allocation_id,
                ReservationId = mutationResult?.ReservationId ?? existing?.reservation_id,
                ReservationItemId = mutationResult?.ReservationItemId ?? existing?.reservation_item_id,
                Qty = newQty,
                stock.goods_location_id,
                stock.goods_owner_id,
                SkuCode = stock.sku_code,
                SelectedBy = currentUser.user_id,
                SelectedByName = currentUser.user_name ?? string.Empty,
                Now = DateTime.Now
            };
            if (existing == null)
            {
                await connection.ExecuteAsync("""
                    INSERT INTO `wms_packing_task_stock_selection`
                      (`sellfox_task_id`,`sellfox_item_id`,`wms_sku_id`,`stock_id`,
                       `erp_stock_id`,`stock_allocation_id`,`reservation_id`,`reservation_item_id`,
                       `qty`,`goods_location_id`,`goods_owner_id`,
                       `sku_code`,`selected_by`,`selected_by_name`,`create_time`,`last_update_time`,
                       `status`,`operation_source`)
                    VALUES (@TaskId,@ItemId,@WmsSkuId,0,@ErpStockId,@AllocationId,
                      @ReservationId,@ReservationItemId,@Qty,
                      @goods_location_id,@goods_owner_id,@SkuCode,@SelectedBy,@SelectedByName,@Now,@Now,
                      'ACTIVE','MODERN_WMS');
                    """, values, transaction);
            }
            else
            {
                await connection.ExecuteAsync("""
                    UPDATE `wms_packing_task_stock_selection`
                       SET `wms_sku_id`=@WmsSkuId,`stock_id`=0,`erp_stock_id`=@ErpStockId,
                           `stock_allocation_id`=@AllocationId,`qty`=@Qty,
                           `reservation_id`=@ReservationId,`reservation_item_id`=@ReservationItemId,
                           `goods_location_id`=@goods_location_id,`goods_owner_id`=@goods_owner_id,
                           `sku_code`=@SkuCode,`selected_by`=@SelectedBy,
                           `selected_by_name`=@SelectedByName,`last_update_time`=@Now,
                           `operation_source`='MODERN_WMS',`row_version`=`row_version`+1
                     WHERE `id`=@Id AND `status`='ACTIVE';
                    """, values, transaction);
            }
            await transaction.CommitAsync();
            return new PackingTaskStockSaveResult(true, "库存选择成功");
        }

        private static async Task<PackingTaskStockSaveResult> RollbackResultAsync(
            System.Data.Common.DbTransaction transaction,
            string message)
        {
            await transaction.RollbackAsync();
            return new PackingTaskStockSaveResult(false, message);
        }

        private static string StripVariantSuffix(string skuCode)
        {
            if (string.IsNullOrWhiteSpace(skuCode))
            {
                return skuCode;
            }

            // 匹配规则：忽略第一个 '-' 之后的所有内容，装箱任务 SKU 与库存 SKU 都沿用此规则。
            var dashIndex = skuCode.IndexOf('-');
            return dashIndex > 0 ? skuCode[..dashIndex] : skuCode;
        }

        private static async Task<InventoryRuntimeRow> LoadRuntimeAsync(
            IDbConnection connection,
            IDbTransaction? transaction,

            long erpWarehouseId)
        {
            var suffix = transaction == null ? string.Empty : " FOR UPDATE";
            return await connection.QuerySingleOrDefaultAsync<InventoryRuntimeRow>(
                $"""
                SELECT `mode` Mode,`maintenance_enabled` MaintenanceEnabled
                  FROM `wms_inventory_runtime_config`
                 WHERE `erp_warehouse_id`=@erpWarehouseId{suffix};
                """, new { erpWarehouseId }, transaction)
                ?? new InventoryRuntimeRow { Mode = LegacyMode };
        }

        private static void EnsureRuntimeReadable(InventoryRuntimeRow runtime, long erpWarehouseId)
        {
            if (runtime.MaintenanceEnabled)
                throw new InvalidOperationException($"ERP仓库 {erpWarehouseId} 正处于库存维护窗口，库存查询已暂停");
            if (runtime.Mode is not (LegacyMode or CanonicalMode))
                throw new InvalidOperationException($"ERP仓库 {erpWarehouseId} 的库存运行模式无效");
        }

        private static void EnsureRuntimeWritable(InventoryRuntimeRow runtime, long erpWarehouseId) =>
            EnsureRuntimeReadable(runtime, erpWarehouseId);

        private static StockMutationContext BuildMutationContext(
            CurrentUser currentUser,
            PackingTaskStockSelectRequest request,
            long erpWarehouseId,
            string bizType,
            string action,
            long quantity,
            long allocationId,
            long oldQty = 0,
            long newQty = 0,
            long sequence = 0,
            long? reservationId = null,
            long? reservationItemId = null)
        {
            var identity = $"{action}:{request.sellfox_task_id}:" +
                $"{request.sellfox_item_id}:{allocationId}:{quantity}:{oldQty}:{newQty}:{sequence}";
            var operationKey = Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(identity))).ToLowerInvariant();
            var operatorName = string.IsNullOrWhiteSpace(currentUser.user_name)
                ? $"用户{currentUser.user_id}" : currentUser.user_name.Trim();
            if (operatorName.Length > 64) operatorName = operatorName[..64];
            return new StockMutationContext(

                erpWarehouseId,
                operationKey,
                bizType,
                request.sellfox_task_id,
                request.sellfox_item_id,
                currentUser.user_id,
                operatorName,
                $"装箱任务库存选择{action}",
                new StockReservationMutationContext(
                    "WMS_RESERVATION_V1",
                    operationKey,
                    "MODERN_WMS",
                    "PACKING_TASK",
                    request.sellfox_task_id,
                    null,
                    null,
                    null,
                    "PACKING_TASK_ITEM",
                    request.sellfox_item_id,
                    $"PACKING:{request.sellfox_task_id}:{request.sellfox_item_id}:{allocationId}",
                    reservationId,
                    reservationItemId));
        }

        private static async Task<long> NextPackingMutationSequenceAsync(
            IDbConnection connection,IDbTransaction transaction,long taskId,long itemId,long allocationId) =>
            1 + await connection.ExecuteScalarAsync<long>("""
                SELECT COUNT(*) FROM `wms_erp_stock_allocation_log`
                 WHERE `biz_type` LIKE 'PACKING_%'
                   AND `biz_id`=@taskId AND `biz_item_id`=@itemId
                   AND `allocation_id`=@allocationId;
                """,new{taskId,itemId,allocationId},transaction);

#endif

        private const string CanonicalMode = "CANONICAL_ERP";
        private const string LegacyMode = "LEGACY_READ";

        private static async Task<InventoryRuntimeRow> LoadRuntimeAsync(IDbConnection connection,
            IDbTransaction? transaction, long erpWarehouseId)
        {
            var suffix = transaction == null ? string.Empty : " FOR UPDATE";
            return await connection.QuerySingleOrDefaultAsync<InventoryRuntimeRow>($"""
                SELECT `mode` Mode,`maintenance_enabled` MaintenanceEnabled
                FROM `wms_inventory_runtime_config` WHERE `erp_warehouse_id`=@erpWarehouseId{suffix};
                """, new { erpWarehouseId }, transaction)
                ?? new InventoryRuntimeRow { Mode = LegacyMode };
        }

        private static void EnsureRuntimeReadable(InventoryRuntimeRow runtime, long erpWarehouseId)
        {
            if (runtime.MaintenanceEnabled)
                throw new InvalidOperationException($"ERP仓库 {erpWarehouseId} 正处于库存维护窗口，库存查询已暂停");
            if (runtime.Mode is not (LegacyMode or CanonicalMode))
                throw new InvalidOperationException($"ERP仓库 {erpWarehouseId} 的库存运行模式无效");
        }

        private sealed class AvailabilityRow
        {
            public long ItemId { get; init; }
            public string SkuCode { get; init; } = string.Empty;
            public int StockQty { get; init; }
            public int AvailableQty { get; init; }
            public int LockedQty { get; init; }
        }

        private sealed class SelectableContext
        {
            public int WarehouseId { get; init; }
            public long ErpWarehouseId { get; init; }
            public string WarehouseName { get; init; } = string.Empty;
            public string CreateName { get; init; } = string.Empty;
            public string? BaseSkuCode { get; init; }
        }

        private sealed class SelectableRow
        {
            public int stock_id { get; init; }
            public int sku_id { get; init; }
            public string sku_code { get; init; } = string.Empty;
            public string spu_code { get; init; } = string.Empty;
            public string commodity_name { get; init; } = string.Empty;
            public string main_image { get; init; } = string.Empty;
            public int goods_location_id { get; init; }
            public string location_name { get; init; } = string.Empty;
            public int goods_owner_id { get; init; }
            public string goods_owner_name { get; init; } = string.Empty;
            public int qty { get; init; }
            public bool is_freeze { get; init; }
            public string series_number { get; init; } = string.Empty;
            public DateTime? expiry_date { get; init; }
            public int selected_qty { get; init; }
            public int locked_qty { get; init; }
        }

        private sealed class CanonicalSelectableRow
        {
            public long stock_allocation_id { get; init; }
            public long erp_stock_id { get; init; }
            public int sku_id { get; init; }
            public string sku_code { get; init; } = string.Empty;
            public string spu_code { get; init; } = string.Empty;
            public string commodity_name { get; init; } = string.Empty;
            public string main_image { get; init; } = string.Empty;
            public int? goods_location_id { get; init; }
            public string location_name { get; init; } = string.Empty;
            public int goods_owner_id { get; init; }
            public string goods_owner_name { get; init; } = string.Empty;
            public long qty { get; init; }
            public long free_qty { get; init; }
            public string series_number { get; init; } = string.Empty;
            public DateTime? expiry_date { get; init; }
            public long selected_qty { get; init; }
        }

        private sealed class CanonicalSelectionStockRow
        {
            public long stock_allocation_id { get; init; }
            public long erp_stock_id { get; init; }
            public int sku_id { get; init; }
            public string sku_code { get; init; } = string.Empty;
            public int? goods_location_id { get; init; }
            public int goods_owner_id { get; init; }
            public string goods_owner_name { get; init; } = string.Empty;
            public long allocated_qty { get; init; }
            public long occupied_qty { get; init; }
            public string location_state { get; init; } = string.Empty;
        }

        private sealed class CanonicalSelectionRow
        {
            public int id { get; init; }
            public long? erp_stock_id { get; init; }
            public long? stock_allocation_id { get; init; }
            public long? reservation_id { get; init; }
            public long? reservation_item_id { get; init; }
            public int qty { get; init; }
            public long erp_warehouse_id { get; init; }
        }

        private sealed class DeleteSelectionSnapshot
        {
            public int? selection_id { get; init; }
            public int? stock_id { get; init; }
            public long? erp_stock_id { get; init; }
            public long? stock_allocation_id { get; init; }
            public long? reservation_id { get; init; }
            public long? reservation_item_id { get; init; }
            public int? qty { get; init; }
            public long erp_warehouse_id { get; init; }
        }

        private sealed class DeleteSelectionLockedRow
        {
            public int id { get; init; }
            public int stock_id { get; init; }
            public long? erp_stock_id { get; init; }
            public long? stock_allocation_id { get; init; }
            public long? reservation_id { get; init; }
            public long? reservation_item_id { get; init; }
            public int qty { get; init; }
        }

        private sealed class InventoryRuntimeRow
        {
            public string Mode { get; init; } = LegacyMode;
            public bool MaintenanceEnabled { get; init; }
        }

        private sealed class SelectionStockRow
        {
            public int id { get; init; }
            public int sku_id { get; init; }
            public int goods_location_id { get; init; }
            public int goods_owner_id { get; init; }
            public int qty { get; init; }
            public bool is_freeze { get; init; }
            public string sku_code { get; init; } = string.Empty;
            public string goods_owner_name { get; init; } = string.Empty;
        }

        private sealed class SelectionTaskContext
        {
            public string task_no { get; init; } = string.Empty;
            public string? create_name { get; init; }
            public string? commodity_name { get; init; }
            public int? task_num { get; init; }
            public long erp_warehouse_id { get; init; }
        }

    }

    private sealed class UnavailableErpPackingStockClient : IErpPackingStockClient
    {
        private static Task<ErpPackingStockResult<ErpPackingStockPlan>> Fail() =>
            Task.FromResult(ErpPackingStockResult<ErpPackingStockPlan>.Failure("ERP 装箱库存客户端不可用，已拒绝本地写入"));

        public Task<ErpPackingStockResult<ErpPackingStockPlan>> GetPlanAsync(ErpPackingStockPlanQuery request, CancellationToken cancellationToken = default) => Fail();
        public Task<ErpPackingStockResult<ErpPackingStockPlan>> UpdateVariantAsync(ErpPackingStockVariantCommand request, CancellationToken cancellationToken = default) => Fail();
        public Task<ErpPackingStockResult<ErpPackingStockPlan>> UpdateContributionAsync(ErpPackingStockContributionCommand request, CancellationToken cancellationToken = default) => Fail();
        public Task<ErpPackingStockResult<ErpPackingStockPlan>> WithdrawParticipantAsync(ErpPackingStockParticipantWithdrawCommand request, CancellationToken cancellationToken = default) => Fail();
        public Task<ErpPackingStockResult<ErpPackingStockPlan>> RetryAsync(ErpPackingStockRetryCommand request, CancellationToken cancellationToken = default) => Fail();
        public Task<ErpPackingStockResult<bool>> ConsumeAsync(ErpPackingStockConsumeCommand request, CancellationToken cancellationToken = default) =>
            Task.FromResult(ErpPackingStockResult<bool>.Failure("ERP 装箱库存客户端不可用，已拒绝本地写入"));
    }
}
