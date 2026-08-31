using System.Data;
using Dapper;
using ModernWMS.Core.JWT;
using ModernWMS.Core.Utility;
using ModernWMS.WMS.Entities.Models;
using ModernWMS.WMS.Entities.ViewModels.DispatchWorkflow;
using ModernWMS.WMS.IServices.StockAllocation;

namespace ModernWMS.WMS.Services.DispatchWorkflow;

/// <summary>逐级撤回未出库发货单。</summary>
public partial class DispatchWorkflowService
{
    /// <summary>将已拣货、已称重或待出库发货单回退到上一个业务环节。</summary>
    public async Task<WeighingCommandResult> RollbackPreviousStageAsync(
        int orderId, WeighingOrderCommandRequest request, CurrentUser user, CancellationToken ct = default)
    {
        ValidateOrderCommand(orderId, request.request_id, request.row_version);
        var requestId = request.request_id.Trim();
        await using var connection = await _connectionFactory.OpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        try
        {
            var previous = await FindOperationAsync(connection, transaction, orderId,
                DispatchWorkflowOperation.RollbackPreviousStage, requestId, ct);
            if (previous?.result_status == DispatchWorkflowOperationResultStatus.Succeeded)
            {
                await transaction.CommitAsync(ct);
                return WeighingResultFromLedger(previous, requestId);
            }

            var order = await LoadOrderForUpdateAsync(connection, transaction, orderId, ct);
            await _warehouseAccessService.EnsureAllowedAsync(order.warehouse_id, user);
            if (order.row_version != request.row_version)
                throw DispatchWorkflowCommandException.ConcurrencyConflict();

            var targetStatus = order.status switch
            {
                DispatchOrderStatus.Picked => DispatchOrderStatus.PendingPick,
                DispatchOrderStatus.Weighing => DispatchOrderStatus.Picked,
                DispatchOrderStatus.PendingOutbound => DispatchOrderStatus.Weighing,
                _ => throw DispatchWorkflowCommandException.StatusNotAllowedForStageRollback()
            };
            var now = DateTime.Now;
            switch (order.status)
            {
                case DispatchOrderStatus.Picked:
                    await RollbackPickedToPendingPickAsync(connection, transaction, order, now, ct);
                    break;
                case DispatchOrderStatus.Weighing:
                    await RollbackWeighingToPickedAsync(connection, transaction, order, user, requestId, now, ct);
                    break;
                case DispatchOrderStatus.PendingOutbound:
                    await RollbackPendingOutboundToWeighingAsync(connection, transaction, order, now, ct);
                    break;
            }

            var clearSourceGuard = targetStatus == DispatchOrderStatus.PendingPick;
            var updated = await connection.ExecuteAsync(new CommandDefinition("""
                UPDATE `wms_dispatch_order`
                   SET `status`=@targetStatus,
                       `source_change_pending`=IF(@clearSourceGuard,0,`source_change_pending`),
                       `pending_source_version`=IF(@clearSourceGuard,'',`pending_source_version`),
                       `source_change_snapshot`=IF(@clearSourceGuard,'',`source_change_snapshot`),
                       `last_update_time`=@now,`row_version`=`row_version`+1
                 WHERE `id`=@orderId AND `row_version`=@expectedVersion;
                """, new
            {
                targetStatus, clearSourceGuard, now, orderId, expectedVersion = order.row_version
            }, transaction, cancellationToken: ct));
            if (updated != 1) throw DispatchWorkflowCommandException.ConcurrencyConflict();

            var resultVersion = order.row_version + 1;
            await InsertOperationAsync(connection, transaction, orderId,
                DispatchWorkflowOperation.RollbackPreviousStage, requestId, targetStatus,
                resultVersion, user, now, ct);
            await transaction.CommitAsync(ct);
            return new WeighingCommandResult
            {
                order_id = orderId,
                request_id = requestId,
                status = ToApiStatus(targetStatus),
                row_version = resultVersion
            };
        }
        catch (Exception exception) when (IsDatabaseConcurrency(exception))
        {
            await transaction.RollbackAsync(CancellationToken.None);
            var winner = await FindOperationAsync(connection, null, orderId,
                DispatchWorkflowOperation.RollbackPreviousStage, requestId, CancellationToken.None);
            if (winner?.result_status == DispatchWorkflowOperationResultStatus.Succeeded)
                return WeighingResultFromLedger(winner, requestId);
            throw DispatchWorkflowCommandException.ConcurrencyConflict();
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private static async Task RollbackPendingOutboundToWeighingAsync(
        IDbConnection connection, IDbTransaction transaction, DispatchOrderEntity order,
        DateTime now, CancellationToken ct)
    {
        var taskIds = await connection.QueryAsync<int>(new CommandDefinition("""
            SELECT `id` FROM `wms_dispatch_packing_task`
             WHERE `dispatch_order_id`=@orderId AND `is_active`=1 ORDER BY `id` FOR UPDATE;
            """, new { orderId = order.id }, transaction, cancellationToken: ct));
        if (!taskIds.Any())
            throw DispatchWorkflowCommandException.StatusNotAllowedForStageRollback("没有可回退的装箱任务");

        await connection.ExecuteAsync(new CommandDefinition("""
            UPDATE `wms_dispatch_packing_task`
               SET `status`=@status,`packing_plan_status`='PACKING_CONFIRMED',
                   `actual_confirmed_at`=NULL,`actual_confirmed_by`=NULL,
                   `actual_confirmed_by_name`='',`last_update_time`=@now,
                   `row_version`=`row_version`+1
             WHERE `dispatch_order_id`=@orderId AND `is_active`=1;
            UPDATE `wms_dispatchlist`
               SET `dispatch_status`=3,`carrier_warehouse_id`=NULL,
                   `carrier`='',`carrier_unit`='',`last_update_time`=@now
             WHERE `dispatch_order_id`=@orderId;
            """, new { status = DispatchOrderStatus.Weighing, now, orderId = order.id },
            transaction, cancellationToken: ct));
    }

    private async Task RollbackWeighingToPickedAsync(
        IDbConnection connection, IDbTransaction transaction, DispatchOrderEntity order,
        CurrentUser user, string requestId, DateTime now, CancellationToken ct)
    {
        var selections = await LoadTransferredSelectionsAsync(connection, transaction, order.id, ct);
        var items = await LoadActiveRollbackItemsAsync(connection, transaction, order.id, ct);
        EnsureSelectionCoverage(items, selections);
        var currentPicks = await LoadRollbackPicksAsync(connection, transaction, order.id, ct);
        if (currentPicks.Any(x => x.is_update_stock))
            throw DispatchWorkflowCommandException.StatusNotAllowedForStageRollback("已出库库存不能回退");
        if (currentPicks.Any(x => x.erp_stock_id is null or <= 0
            || x.reservation_id is null or <= 0 || x.reservation_item_id is null or <= 0))
            throw DispatchWorkflowCommandException.StockConflict("当前拣货明细缺少ERP库存预占凭据");

        var mutation = RequirePackingStockMutationService();
        var prelocks = currentPicks.Select(pick => new PackingStockPrelockRequest(
                DispatchStockMutationContext(user, order.warehouse_id, "DISPATCH_RELEASE", order.id,
                    pick.id, pick.erp_stock_id!.Value, pick.picked_qty,
                    $"ROLLBACK_WEIGHING:{requestId}", pick.reservation_id, pick.reservation_item_id),
                pick.erp_stock_id.Value, "UNLOCK"))
            .Concat(selections.Select(selection => new PackingStockPrelockRequest(
                DispatchStockMutationContext(user, order.warehouse_id, "DISPATCH_RESERVE", order.id,
                    selection.selection_id, selection.erp_stock_id, selection.qty,
                    $"ROLLBACK_WEIGHING:{requestId}", selection.reservation_id,
                    selection.reservation_item_id), selection.erp_stock_id, "LOCK")))
            .ToArray();
        if (prelocks.Length > 0)
            await mutation.PrelockAsync(connection, transaction, [order.warehouse_id], prelocks, ct);

        foreach (var pick in currentPicks.OrderBy(x => x.erp_stock_id).ThenBy(x => x.id))
            await mutation.ReleaseAsync(connection, transaction,
                DispatchStockMutationContext(user, order.warehouse_id, "DISPATCH_RELEASE", order.id,
                    pick.id, pick.erp_stock_id!.Value, pick.picked_qty,
                    $"ROLLBACK_WEIGHING:{requestId}", pick.reservation_id, pick.reservation_item_id),
                pick.erp_stock_id.Value, pick.picked_qty, ct);

        foreach (var selection in selections.OrderBy(x => x.erp_stock_id).ThenBy(x => x.selection_id))
        {
            var reservation = await mutation.ReserveAsync(connection, transaction,
                DispatchStockMutationContext(user, order.warehouse_id, "DISPATCH_RESERVE", order.id,
                    selection.selection_id, selection.erp_stock_id, selection.qty,
                    $"ROLLBACK_WEIGHING:{requestId}", selection.reservation_id,
                    selection.reservation_item_id), selection.erp_stock_id, selection.qty, ct);
            if (reservation.ReservationId != selection.reservation_id
                || reservation.ReservationItemId != selection.reservation_item_id)
                throw DispatchWorkflowCommandException.StockConflict("ERP库存预占身份恢复失败");
        }

        await InvalidateWeighingDataAsync(connection, transaction, order.id, now, ct);
        await connection.ExecuteAsync(new CommandDefinition("""
            DELETE pick FROM `wms_dispatchpicklist` pick
            JOIN `wms_dispatchlist` detail ON detail.`id`=pick.`dispatchlist_id`
            WHERE detail.`dispatch_order_id`=@orderId;
            DELETE FROM `wms_dispatchlist` WHERE `dispatch_order_id`=@orderId;
            """, new { orderId = order.id }, transaction, cancellationToken: ct));

        var detailIds = new Dictionary<int, int>();
        foreach (var item in items.OrderBy(x => x.task_id).ThenBy(x => x.item_id))
        {
            var detailId = await connection.ExecuteScalarAsync<int>(new CommandDefinition("""
                INSERT INTO `wms_dispatchlist` (`dispatch_order_id`,`packing_task_id`,`packing_task_item_id`,
                  `dispatch_no`,`dispatch_status`,`sku_id`,`qty`,`weight`,`volume`,`creator`,`create_time`,
                  `damage_qty`,`lock_qty`,`picked_qty`,`intrasit_qty`,`package_qty`,`weighing_qty`,`actual_qty`,
                  `sign_qty`,`package_no`,`package_person`,`package_time`,`weighing_no`,`weighing_person`,
                  `weighing_weight`,`weighing_length`,`weighing_width`,`weighing_height`,`weighing_volume`,
                  `waybill_no`,`carrier`,`carrier_unit`,`freightfee`,`last_update_time`,`pick_checker_id`,`pick_checker`)
                VALUES (@orderId,@taskId,@itemId,@dispatchNo,3,0,@qty,0,0,@name,@now,
                  0,@qty,@qty,0,0,0,0,0,'','',@minDate,'','',0,0,0,0,0,'','','',0,@now,@userId,@name);
                SELECT LAST_INSERT_ID();
                """, new
            {
                orderId = order.id, taskId = item.task_id, itemId = item.item_id,
                dispatchNo = order.dispatch_no, qty = item.required_qty,
                name = user.user_name ?? string.Empty, now, minDate = UtilConvert.MinDate,
                userId = user.user_id
            }, transaction, cancellationToken: ct));
            detailIds[item.item_id] = detailId;
        }

        foreach (var selection in selections.OrderBy(x => x.erp_stock_id).ThenBy(x => x.selection_id))
            await connection.ExecuteAsync(new CommandDefinition("""
                INSERT INTO `wms_dispatchpicklist` (`dispatchlist_id`,`packing_task_item_id`,`stock_id`,
                  `erp_stock_id`,`stock_allocation_id`,`reservation_id`,`reservation_item_id`,
                  `goods_owner_id`,`goods_location_id`,`sku_id`,`pick_qty`,`picked_qty`,`is_update_stock`,
                  `last_update_time`,`series_number`,`picker_id`,`picker`,`expiry_date`,`price`,`putaway_date`)
                VALUES (@detailId,@itemId,NULL,@erpStockId,NULL,@reservationId,@reservationItemId,
                  NULL,NULL,NULL,@qty,@qty,0,@now,'',@userId,@name,@minDate,0,@minDate);
                """, new
            {
                detailId = detailIds[selection.item_id], itemId = selection.item_id,
                erpStockId = selection.erp_stock_id, reservationId = selection.reservation_id,
                reservationItemId = selection.reservation_item_id, selection.qty, now,
                userId = user.user_id, name = user.user_name ?? string.Empty,
                minDate = UtilConvert.MinDate
            }, transaction, cancellationToken: ct));

        await ResetTasksAfterWeighingAsync(connection, transaction, order.id,
            DispatchOrderStatus.Picked, now, ct);
    }

    private static async Task RollbackPickedToPendingPickAsync(
        IDbConnection connection, IDbTransaction transaction, DispatchOrderEntity order,
        DateTime now, CancellationToken ct)
    {
        var selections = await LoadTransferredSelectionsAsync(connection, transaction, order.id, ct);
        var items = await LoadActiveRollbackItemsAsync(connection, transaction, order.id, ct);
        EnsureSelectionCoverage(items, selections);
        var picks = await LoadRollbackPicksAsync(connection, transaction, order.id, ct);
        if (!RollbackAllocationsMatch(selections, picks))
            throw DispatchWorkflowCommandException.StockConflict("拣货明细与装箱任务库存预占不一致，不能安全回退");

        await InvalidateWeighingDataAsync(connection, transaction, order.id, now, ct);
        await connection.ExecuteAsync(new CommandDefinition("""
            DELETE pick FROM `wms_dispatchpicklist` pick
            JOIN `wms_dispatchlist` detail ON detail.`id`=pick.`dispatchlist_id`
            WHERE detail.`dispatch_order_id`=@orderId;
            DELETE FROM `wms_dispatchlist` WHERE `dispatch_order_id`=@orderId;
            UPDATE `wms_packing_task_stock_selection` selection
            JOIN `wms_dispatch_packing_task` task
              ON task.`source_task_id`=selection.`sellfox_task_id`
             SET selection.`status`='ACTIVE',selection.`operation_source`='DISPATCH_ROLLBACK',
                 selection.`last_update_time`=@now,selection.`row_version`=selection.`row_version`+1
             WHERE task.`dispatch_order_id`=@orderId AND task.`is_active`=1
               AND selection.`status`='TRANSFERRED';
            """, new { orderId = order.id, now }, transaction, cancellationToken: ct));
        await ResetTasksAfterWeighingAsync(connection, transaction, order.id,
            DispatchOrderStatus.PendingPick, now, ct);
    }

    private static async Task<List<RollbackSelectionRow>> LoadTransferredSelectionsAsync(
        IDbConnection connection, IDbTransaction transaction, int orderId, CancellationToken ct) =>
        (await connection.QueryAsync<RollbackSelectionRow>(new CommandDefinition("""
            SELECT selection.`id` selection_id,item.`id` item_id,task.`id` task_id,
                   selection.`erp_stock_id`,selection.`reservation_id`,
                   selection.`reservation_item_id`,selection.`qty`
              FROM `wms_packing_task_stock_selection` selection
              JOIN `wms_dispatch_packing_task` task
                ON task.`source_task_id`=selection.`sellfox_task_id`
              JOIN `wms_dispatch_packing_task_item` item
                ON item.`packing_task_id`=task.`id`
               AND item.`source_item_id`=selection.`sellfox_item_id`
             WHERE task.`dispatch_order_id`=@orderId AND task.`is_active`=1 AND item.`is_active`=1
               AND selection.`status`='TRANSFERRED' AND selection.`erp_stock_id` IS NOT NULL
             ORDER BY selection.`erp_stock_id`,selection.`id` FOR UPDATE;
            """, new { orderId }, transaction, cancellationToken: ct))).AsList();

    private static async Task<List<RollbackItemRow>> LoadActiveRollbackItemsAsync(
        IDbConnection connection, IDbTransaction transaction, int orderId, CancellationToken ct) =>
        (await connection.QueryAsync<RollbackItemRow>(new CommandDefinition("""
            SELECT item.`id` item_id,task.`id` task_id,item.`required_qty`
              FROM `wms_dispatch_packing_task_item` item
              JOIN `wms_dispatch_packing_task` task ON task.`id`=item.`packing_task_id`
             WHERE task.`dispatch_order_id`=@orderId AND task.`is_active`=1 AND item.`is_active`=1
             ORDER BY task.`id`,item.`id` FOR UPDATE;
            """, new { orderId }, transaction, cancellationToken: ct))).AsList();

    private static async Task<List<RollbackPickRow>> LoadRollbackPicksAsync(
        IDbConnection connection, IDbTransaction transaction, int orderId, CancellationToken ct) =>
        (await connection.QueryAsync<RollbackPickRow>(new CommandDefinition("""
            SELECT pick.`id`,pick.`packing_task_item_id` item_id,pick.`erp_stock_id`,
                   pick.`reservation_id`,pick.`reservation_item_id`,pick.`picked_qty`,pick.`is_update_stock`
              FROM `wms_dispatchpicklist` pick
              JOIN `wms_dispatchlist` detail ON detail.`id`=pick.`dispatchlist_id`
             WHERE detail.`dispatch_order_id`=@orderId
             ORDER BY pick.`erp_stock_id`,pick.`id` FOR UPDATE;
            """, new { orderId }, transaction, cancellationToken: ct))).AsList();

    private static void EnsureSelectionCoverage(
        IReadOnlyCollection<RollbackItemRow> items, IReadOnlyCollection<RollbackSelectionRow> selections)
    {
        if (items.Count == 0 || selections.Count == 0
            || selections.Any(x => x.erp_stock_id <= 0 || x.reservation_id is null or <= 0
                || x.reservation_item_id is null or <= 0 || x.qty <= 0)
            || items.Any(item => item.required_qty is null or <= 0
                || selections.Where(x => x.item_id == item.item_id).Sum(x => x.qty) != item.required_qty))
            throw DispatchWorkflowCommandException.StockConflict("装箱任务库存预占不完整，不能安全回退");
    }

    private static bool RollbackAllocationsMatch(
        IReadOnlyCollection<RollbackSelectionRow> selections, IReadOnlyCollection<RollbackPickRow> picks)
    {
        if (picks.Any(x => x.is_update_stock || x.item_id is null || x.erp_stock_id is null
            || x.reservation_id is null || x.reservation_item_id is null || x.picked_qty <= 0)) return false;
        var expected = selections.GroupBy(x => new RollbackAllocationKey(
                x.item_id, x.erp_stock_id, x.reservation_id!.Value, x.reservation_item_id!.Value))
            .ToDictionary(x => x.Key, x => x.Sum(y => y.qty));
        var actual = picks.GroupBy(x => new RollbackAllocationKey(
                x.item_id!.Value, x.erp_stock_id!.Value, x.reservation_id!.Value,
                x.reservation_item_id!.Value))
            .ToDictionary(x => x.Key, x => x.Sum(y => y.picked_qty));
        return expected.Count == actual.Count
            && expected.All(pair => actual.TryGetValue(pair.Key, out var qty) && qty == pair.Value);
    }

    private static Task InvalidateWeighingDataAsync(
        IDbConnection connection, IDbTransaction transaction, int orderId, DateTime now, CancellationToken ct) =>
        connection.ExecuteAsync(new CommandDefinition("""
            UPDATE `wms_weighing_box_item` item
            JOIN `wms_weighing_box` box ON box.`id`=item.`weighing_box_id`
            JOIN `wms_dispatch_packing_task` task ON task.`id`=box.`packing_task_id`
               SET item.`dispatchpicklist_id`=NULL,item.`last_update_time`=@now,
                   item.`row_version`=item.`row_version`+1
             WHERE task.`dispatch_order_id`=@orderId AND task.`is_active`=1;
            UPDATE `wms_weighing_box` box
            JOIN `wms_dispatch_packing_task` task ON task.`id`=box.`packing_task_id`
               SET box.`is_invalidated`=1,box.`invalidated_at`=@now,
                   box.`last_update_time`=@now,box.`row_version`=box.`row_version`+1
             WHERE task.`dispatch_order_id`=@orderId AND task.`is_active`=1
               AND box.`is_invalidated`=0;
            """, new { orderId, now }, transaction, cancellationToken: ct));

    private static Task ResetTasksAfterWeighingAsync(
        IDbConnection connection, IDbTransaction transaction, int orderId,
        DispatchOrderStatus status, DateTime now, CancellationToken ct) =>
        connection.ExecuteAsync(new CommandDefinition("""
            UPDATE `wms_dispatch_packing_task`
               SET `status`=@status,`expected_box_count`=0,`measured_box_count`=0,
                   `packing_plan_status`='DRAFT',`actual_confirmed_at`=NULL,
                   `actual_confirmed_by`=NULL,`actual_confirmed_by_name`='',
                   `last_update_time`=@now,`row_version`=`row_version`+1
             WHERE `dispatch_order_id`=@orderId AND `is_active`=1;
            UPDATE `wms_dispatch_packing_task_item` item
            JOIN `wms_dispatch_packing_task` task ON task.`id`=item.`packing_task_id`
               SET item.`actual_packed_task_qty`=NULL,item.`actual_packed_required_qty`=NULL,
                   item.`last_update_time`=@now,item.`row_version`=item.`row_version`+1
             WHERE task.`dispatch_order_id`=@orderId AND task.`is_active`=1 AND item.`is_active`=1;
            """, new { status, now, orderId }, transaction, cancellationToken: ct));

    private sealed class RollbackSelectionRow
    {
        public int selection_id { get; init; }
        public int item_id { get; init; }
        public int task_id { get; init; }
        public long erp_stock_id { get; init; }
        public long? reservation_id { get; init; }
        public long? reservation_item_id { get; init; }
        public int qty { get; init; }
    }

    private sealed class RollbackItemRow
    {
        public int item_id { get; init; }
        public int task_id { get; init; }
        public int? required_qty { get; init; }
    }

    private sealed class RollbackPickRow
    {
        public int id { get; init; }
        public int? item_id { get; init; }
        public long? erp_stock_id { get; init; }
        public long? reservation_id { get; init; }
        public long? reservation_item_id { get; init; }
        public int picked_qty { get; init; }
        public bool is_update_stock { get; init; }
    }

    private readonly record struct RollbackAllocationKey(
        int ItemId, long ErpStockId, long ReservationId, long ReservationItemId);
}

public sealed partial class DispatchWorkflowCommandException
{
    /// <summary>当前业务环节不允许逐级回退。</summary>
    public static DispatchWorkflowCommandException StatusNotAllowedForStageRollback(
        string detail = "只有已拣货、已称重或待出库状态可以回退到上一环节") =>
        new("STATUS_NOT_ALLOWED", detail);
}
