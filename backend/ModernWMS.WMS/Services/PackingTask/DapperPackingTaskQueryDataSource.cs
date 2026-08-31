using System.Data;
using System.Security.Cryptography;
using System.Text;
using Dapper;
using ModernWMS.Core.Database;
using ModernWMS.Core.DBContext.Entities;
using ModernWMS.Core.JWT;
using ModernWMS.WMS.Entities.ViewModels.PackingTask;
using ModernWMS.WMS.IServices.PackingTask;
using ModernWMS.WMS.IServices.StockAllocation;

namespace ModernWMS.WMS.Services;

/// <summary>Direct <c>trk_stock</c> persistence boundary for Sellfox packing tasks.</summary>
internal sealed class DapperPackingTaskQueryDataSource(
    IMySqlConnectionFactory connectionFactory,
    IPackingStockMutationService stockMutationService,
    ILegacyPackingSelectionReleaseAdapter legacyReleaseAdapter) : IPackingTaskQueryDataSource
{
    private readonly IMySqlConnectionFactory _connectionFactory = connectionFactory
        ?? throw new ArgumentNullException(nameof(connectionFactory));
    private readonly IPackingStockMutationService _stockMutationService = stockMutationService
        ?? throw new ArgumentNullException(nameof(stockMutationService));
    private readonly ILegacyPackingSelectionReleaseAdapter _legacyReleaseAdapter = legacyReleaseAdapter
        ?? throw new ArgumentNullException(nameof(legacyReleaseAdapter));

    private const string GroupMemberNamesSql = """
        WITH RECURSIVE dept_tree AS (
            SELECT d.`id`,0 depth FROM `system_dept` d
             WHERE d.`id`=@GroupId AND d.`deleted`=0 AND d.`status`=0 AND d.`dept`='operator'
            UNION ALL
            SELECT child.`id`,tree.depth+1 FROM `system_dept` child
            JOIN dept_tree tree ON child.`parent_id`=tree.`id`
             WHERE child.`deleted`=0 AND child.`status`=0 AND tree.depth<20
        )
        SELECT DISTINCT users.`nickname` FROM `system_users` users
        JOIN dept_tree tree ON users.`dept_id`=tree.`id`
         WHERE users.`deleted`=0 AND users.`status`=0
           AND users.`nickname` IS NOT NULL AND users.`nickname`<>'';
        """;

    public async Task<PackingTaskPageData> LoadPageAsync(PackingTaskPageRequest request)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync();
        var parameters = new DynamicParameters();
        parameters.Add("HasKeyword", string.IsNullOrWhiteSpace(request.Keyword) ? 0 : 1);
        parameters.Add("Keyword", request.Keyword.Trim());
        parameters.Add("WarehouseId", request.WarehouseId);
        parameters.Add("Offset", request.Offset);
        parameters.Add("PageSize", request.PageSize);
        var extraWhere = string.Empty;
        if (request.GroupId > 0)
        {
            var names = (await connection.QueryAsync<string>(
                GroupMemberNamesSql, new { request.GroupId })).AsList();
            if (names.Count == 0) return new PackingTaskPageData([], [],
                new Dictionary<long, PackingTaskStockAvailability>(), 0);
            parameters.Add("GroupMemberNames", names);
            extraWhere += " AND task.`create_name` IN @GroupMemberNames";
        }
        if (request.MemberId > 0)
        {
            var name = await connection.QuerySingleOrDefaultAsync<string>("""
                SELECT `nickname` FROM `system_users`
                 WHERE `id`=@MemberId AND `deleted`=0 AND `status`=0 LIMIT 1;
                """, new { request.MemberId });
            if (string.IsNullOrWhiteSpace(name)) return new PackingTaskPageData([], [],
                new Dictionary<long, PackingTaskStockAvailability>(), 0);
            parameters.Add("MemberName", name);
            extraWhere += " AND task.`create_name`=@MemberName";
        }

        var where = $"""
            FROM `ruiyi_sellfox_packing_task` task
            WHERE task.`source_deleted`=0 AND task.`source_canceled`=0
              AND (@WarehouseId IS NULL OR task.`warehouse_id`=@WarehouseId)
              AND NOT EXISTS (
                SELECT 1 FROM `wms_dispatch_packing_task` active_task
                 WHERE active_task.`active_source_task_id`=task.`sellfox_task_id`)
              AND (@HasKeyword=0 OR LOCATE(@Keyword,task.`packing_task_sn`)>0 OR EXISTS (
                SELECT 1 FROM `ruiyi_sellfox_packing_task_item` search_item
                 WHERE search_item.`sellfox_task_id`=task.`sellfox_task_id`
                   AND search_item.`source_deleted`=0
                   AND (LOCATE(@Keyword,search_item.`commodity_name`)>0
                     OR LOCATE(@Keyword,search_item.`commodity_sku`)>0
                     OR LOCATE(@Keyword,search_item.`sku`)>0
                     OR LOCATE(@Keyword,search_item.`fn_sku`)>0)))
            {extraWhere}
            """;
        var totals = await connection.QuerySingleAsync<int>($"SELECT COUNT(*) {where}", parameters);
        var tasks = (await connection.QueryAsync<ErpPackingTaskEntity>($"""
            SELECT task.`id`,task.`sellfox_task_id`,task.`packing_task_sn`,task.`warehouse_id`,
                   task.`warehouse_name`,task.`complete_num`,task.`task_num`,task.`create_name`,
                   task.`source_create_time`,task.`item_count`,task.`shop_name`,task.`marketplace_name`
            {where}
            ORDER BY task.`source_create_time` DESC,task.`id` DESC
            LIMIT @PageSize OFFSET @Offset;
            """, parameters)).AsList();
        if (tasks.Count == 0) return new PackingTaskPageData([], [],
            new Dictionary<long, PackingTaskStockAvailability>(), totals);

        foreach (var creatorName in tasks.Select(task => task.create_name).Distinct(StringComparer.OrdinalIgnoreCase))
            _ = await ResolveOwnerIdAsync(connection, null, creatorName);
        if (tasks.Any(task => task.warehouse_id is null or <= 0))
            throw new InvalidOperationException("装箱任务缺少ERP仓库，库存查询已拒绝");

        var taskIds = tasks.Select(task => task.sellfox_task_id).ToArray();
        var items = (await connection.QueryAsync<ErpPackingTaskItemEntity>("""
            SELECT `id`,`sellfox_item_id`,`sellfox_task_id`,`commodity_id`,`commodity_sku`,
                   `commodity_name`,`main_image`,`fn_sku`,`sku`,`msku`,`task_num`,
                   `quantity_shipped`,`stock_available`
              FROM `ruiyi_sellfox_packing_task_item`
             WHERE `source_deleted`=0 AND `sellfox_task_id` IN @TaskIds
             ORDER BY `id`;
            """, new { TaskIds = taskIds })).AsList();
        var rows = (await connection.QueryAsync<AvailabilityRow>("""
            SELECT item.`id` ItemId,
                   SUBSTRING_INDEX(COALESCE(item.`commodity_sku`,''),'-',1) SkuCode,
                   COALESCE(SUM(stock.`total_qty`),0) StockQty,
                   COALESCE(SUM(stock.`available_qty`),0) AvailableQty,
                   COALESCE(MAX(selection.`locked_qty`),0) LockedQty
              FROM `ruiyi_sellfox_packing_task_item` item
              JOIN `ruiyi_sellfox_packing_task` task
                ON task.`sellfox_task_id`=item.`sellfox_task_id`
              JOIN `system_users` creator ON creator.`nickname`=task.`create_name`
                AND creator.`deleted`=0 AND creator.`status`=0
              LEFT JOIN `trk_stock` stock ON stock.`warehouse_id`=task.`warehouse_id`
                AND stock.`order_user_id`=creator.`id` AND stock.`deleted`=b'0'
                AND (stock.`commodity_id`=item.`commodity_id`
                  OR SUBSTRING_INDEX(COALESCE(stock.`commodity_sku`,''),'-',1)
                   =SUBSTRING_INDEX(COALESCE(item.`commodity_sku`,''),'-',1))
              LEFT JOIN (
                SELECT `sellfox_item_id`,SUM(`qty`) locked_qty
                  FROM `wms_packing_task_stock_selection`
                 WHERE `sellfox_task_id` IN @TaskIds AND `status`='ACTIVE'
                 GROUP BY `sellfox_item_id`) selection
                ON selection.`sellfox_item_id`=item.`sellfox_item_id`
             WHERE item.`source_deleted`=0 AND item.`sellfox_task_id` IN @TaskIds
             GROUP BY item.`id`,SUBSTRING_INDEX(COALESCE(item.`commodity_sku`,''),'-',1);
            """, new { TaskIds = taskIds })).AsList();
        return new PackingTaskPageData(tasks, items, rows.ToDictionary(
            row => row.ItemId,
            row => new PackingTaskStockAvailability(
                row.SkuCode, checked((int)row.StockQty), checked((int)row.LockedQty),
                checked((int)row.AvailableQty))), totals);
    }

    public async Task<PackingTaskSelectableData?> LoadSelectableStockAsync(
        PackingTaskStockPageRequest request,
        CurrentUser currentUser)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync();
        var context = await LoadTaskContextAsync(
            connection, null, request.sellfox_task_id, request.sellfox_item_id, false);
        if (context == null) return null;
        var ownerId = await ResolveOwnerIdAsync(connection, null, context.CreateName);
        var rows = (await connection.QueryAsync<SelectableRow>("""
            SELECT stock.`id` ErpStockId,stock.`commodity_id` CommodityId,
                   COALESCE(stock.`commodity_sku`,'') SkuCode,
                   COALESCE(stock.`commodity_name`,'') CommodityName,
                   COALESCE((SELECT commodity.`img_url` FROM `erp_commodity` commodity
                     WHERE commodity.`id`=CAST(stock.`commodity_id` AS CHAR)
                       AND commodity.`img_url`<>'' LIMIT 1),'') MainImage,
                   stock.`warehouse_id` WarehouseId,stock.`order_user_id` OrderUserId,
                   stock.`available_qty` AvailableQty,stock.`occupied_qty` OccupiedQty,
                   stock.`total_qty` TotalQty,COALESCE(selection.`selected_qty`,0) SelectedQty
              FROM `trk_stock` stock
              LEFT JOIN (
                SELECT `erp_stock_id`,SUM(`qty`) selected_qty
                  FROM `wms_packing_task_stock_selection`
                 WHERE `sellfox_task_id`=@TaskId AND `sellfox_item_id`=@ItemId
                   AND `status`='ACTIVE' AND `erp_stock_id` IS NOT NULL
                 GROUP BY `erp_stock_id`) selection ON selection.`erp_stock_id`=stock.`id`
             WHERE stock.`warehouse_id`=@WarehouseId AND stock.`order_user_id`=@OwnerId
               AND stock.`deleted`=b'0'
               AND (@HasKeyword=0 OR stock.`commodity_sku` LIKE @Keyword
                 OR stock.`commodity_name` LIKE @Keyword)
             ORDER BY stock.`id`;
            """, new
        {
            TaskId = request.sellfox_task_id,
            ItemId = request.sellfox_item_id,
            WarehouseId = context.WarehouseId,
            OwnerId = ownerId,
            HasKeyword = string.IsNullOrWhiteSpace(request.keyword) ? 0 : 1,
            Keyword = $"%{request.keyword.Trim()}%"
        })).AsList();
        var baseSku = BaseSku(context.CommoditySku);
        var result = rows.Select(row => new SelectableStockViewModel
        {
            erp_stock_id = row.ErpStockId,
            commodity_id = row.CommodityId,
            sku_code = row.SkuCode,
            commodity_name = row.CommodityName,
            main_image = row.MainImage,
            warehouse_id = row.WarehouseId,
            warehouse_name = context.WarehouseName,
            order_user_id = row.OrderUserId,
            order_user_name = context.CreateName,
            available_qty = row.AvailableQty,
            occupied_qty = row.OccupiedQty,
            total_qty = row.TotalQty,
            matched = context.CommodityId is > 0 && row.CommodityId == context.CommodityId
                || baseSku.Length > 0 && string.Equals(BaseSku(row.SkuCode), baseSku,
                    StringComparison.OrdinalIgnoreCase),
            selected = row.SelectedQty > 0,
            selected_qty = row.SelectedQty
        }).ToList();
        return new PackingTaskSelectableData(result, context.WarehouseId, context.WarehouseName);
    }

    public async Task<PackingTaskStockSaveResult> SaveSelectionAsync(
        PackingTaskStockSelectRequest request,
        CurrentUser currentUser)
    {
        if (request.erp_stock_id <= 0)
            return new PackingTaskStockSaveResult(false, "必须提交有效的ERP库存ID");
        await using var connection = await _connectionFactory.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted);
        try
        {
            var task = await LoadTaskContextAsync(
                connection, transaction, request.sellfox_task_id, request.sellfox_item_id, true);
            if (task == null) return await RollbackAsync(transaction, "装箱任务明细不存在");
            var ownerId = await ResolveOwnerIdAsync(connection, transaction, task.CreateName);
            if (task.TaskQty is null or <= 0 || request.variant <= 0)
                return await RollbackAsync(transaction, "变体数量必须大于0");
            var targetQtyLong = checked((long)task.TaskQty.Value * request.variant);
            if (targetQtyLong > int.MaxValue)
                return await RollbackAsync(transaction, "锁定数量超过装箱选择表容量");
            var targetQty = checked((int)targetQtyLong);

            var activeRows = (await connection.QueryAsync<SelectionRow>(new CommandDefinition(
                """
                SELECT `id`,`erp_stock_id` ErpStockId,`stock_allocation_id` StockAllocationId,
                       `reservation_id` ReservationId,`reservation_item_id` ReservationItemId,
                       `qty` Qty,`row_version` RowVersion
                  FROM `wms_packing_task_stock_selection`
                 WHERE `sellfox_task_id`=@TaskId AND `sellfox_item_id`=@ItemId
                   AND `status`='ACTIVE' ORDER BY `id` FOR UPDATE;
                """,
                new { TaskId = request.sellfox_task_id, ItemId = request.sellfox_item_id },
                transaction))).AsList();
            if (activeRows.Count > 1)
                return await RollbackAsync(transaction, "装箱明细存在多条活动库存绑定，请先完成历史清理");
            var existing = activeRows.SingleOrDefault();
            var mutationRequestId = Guid.NewGuid().ToString("N");
            var actions = BuildActions(request, currentUser, task, existing, targetQty, mutationRequestId);
            if (actions.Count > 0)
            {
                await _stockMutationService.PrelockAsync(
                    connection, transaction, [task.WarehouseId],
                    actions.Select(action => new PackingStockPrelockRequest(
                        action.Context, action.StockId, action.EventType)).ToArray());
            }
            var stockIds = actions.Select(action => action.StockId)
                .Append(request.erp_stock_id).Distinct().Order().ToArray();
            var stocks = (await connection.QueryAsync<StockBoundaryRow>(new CommandDefinition(
                """
                SELECT `id` Id,`warehouse_id` WarehouseId,`order_user_id` OrderUserId,
                       COALESCE(`commodity_sku`,'') SkuCode
                  FROM `trk_stock`
                 WHERE `id` IN @StockIds AND `deleted`=b'0'
                 ORDER BY `id` FOR UPDATE;
                """, new { StockIds = stockIds }, transaction))).AsList();
            if (stocks.Count != stockIds.Length)
                return await RollbackAsync(transaction, "ERP库存不存在或已删除");
            if (stocks.Any(stock => stock.WarehouseId != task.WarehouseId
                                    || stock.OrderUserId != ownerId))
                return await RollbackAsync(transaction, "只能选择任务创建人在同一ERP仓库的库存");

            PackingStockMutationResult? targetMutation = null;
            foreach (var action in actions)
            {
                var result = action.EventType switch
                {
                    "LOCK" => await _stockMutationService.ReserveAsync(
                        connection, transaction, action.Context, action.StockId, action.Quantity),
                    "UNLOCK" => await _stockMutationService.ReleaseAsync(
                        connection, transaction, action.Context, action.StockId, action.Quantity),
                    _ => throw new InvalidOperationException("未知装箱绑定库存动作")
                };
                if (action.StockId == request.erp_stock_id && action.EventType == "LOCK")
                    targetMutation = result;
                if (action.EventType == "UNLOCK" && action.LegacyAllocationId is > 0)
                {
                    if (action.ReservationItemId is not > 0)
                        throw new InvalidOperationException("历史位置分配绑定缺少共享预占明细");
                    await _legacyReleaseAdapter.SettleReleaseAsync(
                        connection, transaction, action.StockId,
                        action.LegacyAllocationId.Value, action.ReservationItemId.Value,
                        action.Quantity, currentUser.user_name ?? string.Empty);
                }
            }

            var targetStock = stocks.Single(stock => stock.Id == request.erp_stock_id);
            var now = DateTime.Now;
            if (existing == null || existing.ErpStockId != request.erp_stock_id
                                 || existing.StockAllocationId != null)
            {
                if (existing != null)
                {
                    await CancelSelectionAsync(connection, transaction, existing.Id, currentUser,
                        "更换ERP库存绑定", "WMS_REBIND", now);
                }
                await connection.ExecuteAsync(new CommandDefinition(
                    """
                    INSERT INTO `wms_packing_task_stock_selection`
                      (`sellfox_task_id`,`sellfox_item_id`,`wms_sku_id`,`stock_id`,`erp_stock_id`,
                       `stock_allocation_id`,`reservation_id`,`reservation_item_id`,`qty`,
                       `goods_location_id`,`goods_owner_id`,`sku_code`,`selected_by`,`selected_by_name`,
                       `create_time`,`last_update_time`,`status`,`operation_source`)
                    VALUES
                      (@TaskId,@ItemId,NULL,NULL,@ErpStockId,NULL,@ReservationId,@ReservationItemId,@Qty,
                       NULL,NULL,@SkuCode,@SelectedBy,@SelectedByName,@Now,@Now,'ACTIVE','MODERN_WMS');
                    """, new
                {
                    TaskId = request.sellfox_task_id,
                    ItemId = request.sellfox_item_id,
                    ErpStockId = request.erp_stock_id,
                    ReservationId = targetMutation?.ReservationId,
                    ReservationItemId = targetMutation?.ReservationItemId,
                    Qty = targetQty,
                    targetStock.SkuCode,
                    SelectedBy = currentUser.user_id,
                    SelectedByName = currentUser.user_name ?? string.Empty,
                    Now = now
                }, transaction));
            }
            else
            {
                await connection.ExecuteAsync(new CommandDefinition(
                    """
                    UPDATE `wms_packing_task_stock_selection`
                       SET `qty`=@Qty,
                           `reservation_id`=COALESCE(@ReservationId,`reservation_id`),
                           `reservation_item_id`=COALESCE(@ReservationItemId,`reservation_item_id`),
                           `selected_by`=@SelectedBy,`selected_by_name`=@SelectedByName,
                           `last_update_time`=@Now,`row_version`=`row_version`+1,
                           `operation_source`='MODERN_WMS'
                     WHERE `id`=@Id AND `row_version`=@RowVersion AND `status`='ACTIVE';
                    """, new
                {
                    existing.Id,
                    existing.RowVersion,
                    Qty = targetQty,
                    ReservationId = targetMutation?.ReservationId,
                    ReservationItemId = targetMutation?.ReservationItemId,
                    SelectedBy = currentUser.user_id,
                    SelectedByName = currentUser.user_name ?? string.Empty,
                    Now = now
                }, transaction));
            }
            await WriteActionLogAsync(connection, transaction, currentUser,
                $"装箱任务{task.TaskNo}绑定ERP库存{request.erp_stock_id}，锁定数量{targetQty}", now);
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
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted);
        try
        {
            var task = await LoadTaskContextAsync(
                connection, transaction, request.sellfox_task_id, request.sellfox_item_id, true);
            if (task == null) return await RollbackAsync(transaction, "装箱任务明细不存在");
            var ownerId = await ResolveOwnerIdAsync(connection, transaction, task.CreateName);
            var selection = await connection.QuerySingleOrDefaultAsync<SelectionRow>(new CommandDefinition(
                """
                SELECT `id`,`erp_stock_id` ErpStockId,`stock_allocation_id` StockAllocationId,
                       `reservation_id` ReservationId,`reservation_item_id` ReservationItemId,
                       `qty` Qty,`row_version` RowVersion
                  FROM `wms_packing_task_stock_selection`
                 WHERE `sellfox_task_id`=@TaskId AND `sellfox_item_id`=@ItemId
                   AND `erp_stock_id`=@ErpStockId AND `status`='ACTIVE'
                 ORDER BY `id` LIMIT 1 FOR UPDATE;
                """, new
            {
                TaskId = request.sellfox_task_id,
                ItemId = request.sellfox_item_id,
                ErpStockId = request.erp_stock_id
            }, transaction));
            if (selection == null) return await RollbackAsync(transaction, "该ERP库存未在选择中");
            if (selection.ErpStockId is not > 0 || selection.ReservationId is null
                || selection.ReservationItemId is null || selection.Qty <= 0)
                return await RollbackAsync(transaction, "库存绑定缺少共享预占身份，禁止无主释放");

            var context = BuildContext(
                currentUser, request.sellfox_task_id, request.sellfox_item_id,
                task.WarehouseId, selection.ErpStockId.Value, "RELEASE",
                selection.Qty, selection.RowVersion + 1,
                selection.ReservationId, selection.ReservationItemId,
                Guid.NewGuid().ToString("N"));
            await _stockMutationService.PrelockAsync(connection, transaction, [task.WarehouseId],
                [new PackingStockPrelockRequest(context, selection.ErpStockId.Value, "UNLOCK")]);
            var boundary = await connection.QuerySingleOrDefaultAsync<StockBoundaryRow>(new CommandDefinition(
                """
                SELECT `id` Id,`warehouse_id` WarehouseId,`order_user_id` OrderUserId,
                       COALESCE(`commodity_sku`,'') SkuCode
                  FROM `trk_stock` WHERE `id`=@StockId AND `deleted`=b'0' FOR UPDATE;
                """, new { StockId = selection.ErpStockId.Value }, transaction));
            if (boundary == null || boundary.WarehouseId != task.WarehouseId
                                 || boundary.OrderUserId != ownerId)
                return await RollbackAsync(transaction, "绑定库存不再属于任务创建人或任务仓库");
            await _stockMutationService.ReleaseAsync(
                connection, transaction, context, selection.ErpStockId.Value, selection.Qty);
            if (selection.StockAllocationId is > 0)
            {
                await _legacyReleaseAdapter.SettleReleaseAsync(
                    connection, transaction, selection.ErpStockId.Value,
                    selection.StockAllocationId.Value, selection.ReservationItemId.Value,
                    selection.Qty, currentUser.user_name ?? string.Empty);
            }
            var now = DateTime.Now;
            await CancelSelectionAsync(connection, transaction, selection.Id, currentUser,
                "用户取消装箱任务库存选择", "WMS_MANUAL_CANCEL", now);
            await WriteActionLogAsync(connection, transaction, currentUser,
                $"装箱任务{task.TaskNo}取消ERP库存{selection.ErpStockId}绑定并释放{selection.Qty}", now);
            await transaction.CommitAsync();
            return new PackingTaskStockSaveResult(true, "已取消选择，锁定库存已释放");
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    private static List<MutationAction> BuildActions(
        PackingTaskStockSelectRequest request,
        CurrentUser currentUser,
        TaskContext task,
        SelectionRow? existing,
        int targetQty,
        string mutationRequestId)
    {
        var actions = new List<MutationAction>();
        if (existing == null)
        {
            actions.Add(Action("LOCK", request.erp_stock_id, targetQty, null, null, 1, null));
            return actions;
        }
        if (existing.ErpStockId is not > 0)
            throw new InvalidOperationException("历史库存绑定缺少ERP库存身份");
        if (existing.StockAllocationId is > 0)
        {
            actions.Add(Action("UNLOCK", existing.ErpStockId.Value, existing.Qty,
                existing.ReservationId, existing.ReservationItemId, existing.RowVersion + 1,
                existing.StockAllocationId));
            actions.Add(Action("LOCK", request.erp_stock_id, targetQty,
                null, null, existing.RowVersion + 2, null));
            return actions;
        }
        if (existing.ErpStockId == request.erp_stock_id)
        {
            var delta = targetQty - existing.Qty;
            if (delta > 0) actions.Add(Action("LOCK", request.erp_stock_id, delta,
                existing.ReservationId, existing.ReservationItemId, existing.RowVersion + 1, null));
            if (delta < 0) actions.Add(Action("UNLOCK", request.erp_stock_id, -delta,
                existing.ReservationId, existing.ReservationItemId, existing.RowVersion + 1, null));
            return actions;
        }
        actions.Add(Action("UNLOCK", existing.ErpStockId.Value, existing.Qty,
            existing.ReservationId, existing.ReservationItemId, existing.RowVersion + 1, null));
        actions.Add(Action("LOCK", request.erp_stock_id, targetQty,
            null, null, existing.RowVersion + 2, null));
        return actions;

        MutationAction Action(
            string eventType, long stockId, long quantity, long? reservationId,
            long? reservationItemId, long sequence, long? legacyAllocationId) => new(
            eventType,
            stockId,
            quantity,
            BuildContext(currentUser, request.sellfox_task_id, request.sellfox_item_id,
                task.WarehouseId, stockId, eventType == "LOCK" ? "RESERVE" : "RELEASE",
                quantity, sequence, reservationId, reservationItemId, mutationRequestId),
            legacyAllocationId,
            reservationItemId);
    }

    private static StockMutationContext BuildContext(
        CurrentUser currentUser,
        long taskId,
        long itemId,
        long warehouseId,
        long stockId,
        string action,
        long quantity,
        long sequence,
        long? reservationId,
        long? reservationItemId,
        string mutationRequestId)
    {
        var identity = $"{action}:{taskId}:{itemId}:{stockId}:{quantity}:{sequence}:{mutationRequestId}";
        var operationKey = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(identity))).ToLowerInvariant();
        var operatorName = string.IsNullOrWhiteSpace(currentUser.user_name)
            ? $"用户{currentUser.user_id}" : currentUser.user_name.Trim();
        if (operatorName.Length > 64) operatorName = operatorName[..64];
        return new StockMutationContext(
            warehouseId,
            operationKey,
            action == "RESERVE" ? "PACKING_LOCK" : "PACKING_RELEASE",
            taskId,
            itemId,
            currentUser.user_id,
            operatorName,
            $"装箱任务库存{action}",
            new StockReservationMutationContext(
                "WMS_RESERVATION_V1", operationKey, "MODERN_WMS", "PACKING_TASK",
                taskId, null, null, null, "PACKING_TASK_ITEM", itemId,
                $"PACKING:{taskId}:{itemId}:{stockId}", reservationId, reservationItemId));
    }

    private static async Task<TaskContext?> LoadTaskContextAsync(
        IDbConnection connection,
        IDbTransaction? transaction,
        long taskId,
        long itemId,
        bool lockRow)
    {
        var suffix = lockRow ? " FOR UPDATE" : string.Empty;
        return await connection.QuerySingleOrDefaultAsync<TaskContext>(
            $"""
            SELECT task.`packing_task_sn` TaskNo,task.`create_name` CreateName,
                   task.`warehouse_id` WarehouseId,COALESCE(task.`warehouse_name`,'') WarehouseName,
                   item.`commodity_id` CommodityId,COALESCE(item.`commodity_sku`,'') CommoditySku,
                   item.`task_num` TaskQty
              FROM `ruiyi_sellfox_packing_task_item` item
              JOIN `ruiyi_sellfox_packing_task` task
                ON task.`sellfox_task_id`=item.`sellfox_task_id`
               AND task.`source_deleted`=0 AND task.`source_canceled`=0
             WHERE item.`sellfox_task_id`=@TaskId AND item.`sellfox_item_id`=@ItemId
               AND item.`source_deleted`=0 LIMIT 1{suffix};
            """, new { TaskId = taskId, ItemId = itemId }, transaction);
    }

    private static async Task<long> ResolveOwnerIdAsync(
        IDbConnection connection,
        IDbTransaction? transaction,
        string? creatorName)
    {
        var candidates = (await connection.QueryAsync<PackingTaskOwnerCandidate>(new CommandDefinition(
            """
            SELECT `id` UserId,`nickname` Name FROM `system_users`
             WHERE `deleted`=0 AND `status`=0 AND `nickname`=@CreatorName
             ORDER BY `id` LIMIT 2;
            """, new { CreatorName = creatorName?.Trim() ?? string.Empty }, transaction))).AsList();
        return PackingTaskOwnerPolicy.Resolve(creatorName, candidates);
    }

    private static async Task CancelSelectionAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        int selectionId,
        CurrentUser currentUser,
        string reason,
        string source,
        DateTime now)
    {
        var affected = await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE `wms_packing_task_stock_selection`
               SET `status`='CANCELLED',`cancelled_by`=@CancelledBy,
                   `cancelled_by_name`=@CancelledByName,`cancelled_at`=@Now,
                   `cancel_reason`=@Reason,`operation_source`=@Source,
                   `last_update_time`=@Now,`row_version`=`row_version`+1
             WHERE `id`=@SelectionId AND `status`='ACTIVE';
            """, new
        {
            SelectionId = selectionId,
            CancelledBy = currentUser.user_id,
            CancelledByName = currentUser.user_name ?? string.Empty,
            Now = now,
            Reason = reason,
            Source = source
        }, transaction));
        if (affected != 1) throw new InvalidOperationException("库存绑定已变化，请刷新后重试");
    }

    private static Task WriteActionLogAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        CurrentUser currentUser,
        string content,
        DateTime now) => connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO `wms_action_log` (`vue_path`,`user_name`,`action_content`,`action_time`)
            VALUES ('deliveryManagement/deliveryManagement',@UserName,@Content,@Now);
            """, new
        {
            UserName = currentUser.user_name ?? string.Empty,
            Content = content,
            Now = now
        }, transaction));

    private static async Task<PackingTaskStockSaveResult> RollbackAsync(
        System.Data.Common.DbTransaction transaction,
        string message)
    {
        await transaction.RollbackAsync();
        return new PackingTaskStockSaveResult(false, message);
    }

    private static string BaseSku(string? sku)
    {
        var value = sku?.Trim() ?? string.Empty;
        var dash = value.IndexOf('-');
        return dash > 0 ? value[..dash] : value;
    }

    private sealed record MutationAction(
        string EventType,
        long StockId,
        long Quantity,
        StockMutationContext Context,
        long? LegacyAllocationId,
        long? ReservationItemId);

    private sealed class AvailabilityRow
    {
        public long ItemId { get; init; }
        public string SkuCode { get; init; } = string.Empty;
        public long StockQty { get; init; }
        public long AvailableQty { get; init; }
        public long LockedQty { get; init; }
    }

    private sealed class TaskContext
    {
        public string TaskNo { get; init; } = string.Empty;
        public string CreateName { get; init; } = string.Empty;
        public long WarehouseId { get; init; }
        public string WarehouseName { get; init; } = string.Empty;
        public long? CommodityId { get; init; }
        public string CommoditySku { get; init; } = string.Empty;
        public int? TaskQty { get; init; }
    }

    private sealed class SelectableRow
    {
        public long ErpStockId { get; init; }
        public long? CommodityId { get; init; }
        public string SkuCode { get; init; } = string.Empty;
        public string CommodityName { get; init; } = string.Empty;
        public string MainImage { get; init; } = string.Empty;
        public long WarehouseId { get; init; }
        public long OrderUserId { get; init; }
        public long AvailableQty { get; init; }
        public long OccupiedQty { get; init; }
        public long TotalQty { get; init; }
        public long SelectedQty { get; init; }
    }

    private sealed class SelectionRow
    {
        public int Id { get; init; }
        public long? ErpStockId { get; init; }
        public long? StockAllocationId { get; init; }
        public long? ReservationId { get; init; }
        public long? ReservationItemId { get; init; }
        public int Qty { get; init; }
        public long RowVersion { get; init; }
    }

    private sealed class StockBoundaryRow
    {
        public long Id { get; init; }
        public long WarehouseId { get; init; }
        public long OrderUserId { get; init; }
        public string SkuCode { get; init; } = string.Empty;
    }
}
