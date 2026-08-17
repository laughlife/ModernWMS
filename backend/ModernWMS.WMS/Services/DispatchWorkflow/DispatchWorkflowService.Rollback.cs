using System.Data;
using Dapper;
using ModernWMS.Core.JWT;
using ModernWMS.WMS.Entities.Models;
using ModernWMS.WMS.Entities.ViewModels.DispatchWorkflow;
using MySqlConnector;

namespace ModernWMS.WMS.Services.DispatchWorkflow;

public partial class DispatchWorkflowService
{
    /// <summary>
    /// 回退一张待拣货发货单：订单置为人工取消，并把装箱任务释放回装箱任务列表，可重新选择建单。
    /// 待拣货阶段尚未生成拣货明细与库存占用，因此数据回退只需释放任务并更新订单状态；
    /// 状态角标由 counts 查询按状态实时统计，回退后待拣货减一、装箱任务随之恢复。
    /// </summary>
    public async Task<RollbackPendingPickResult> RollbackPendingPickAsync(int orderId, RollbackPendingPickRequest request,
        CurrentUser user, CancellationToken ct = default)
    {
        ValidateRollbackRequest(orderId, request);
        var requestId = request.request_id.Trim();
        await using var c = await _connectionFactory.OpenConnectionAsync(ct);
        await using var tx = await c.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        try
        {
            var previous = await FindOperationAsync(c, tx, orderId, DispatchWorkflowOperation.RollbackPendingPick, requestId, ct);
            if (previous?.result_status == DispatchWorkflowOperationResultStatus.Succeeded)
            {
                await tx.CommitAsync(ct);
                return RollbackFromLedger(previous, requestId);
            }
            var order = await LoadOrderForUpdateAsync(c, tx, orderId, ct);
            await _warehouseAccessService.EnsureAllowedAsync(order.warehouse_id, user);
            if (order.status != DispatchOrderStatus.PendingPick)
                throw DispatchWorkflowCommandException.StatusNotAllowedForRollback();
            if (order.row_version != request.row_version)
                throw DispatchWorkflowCommandException.ConcurrencyConflict();
            var tasks = (await c.QueryAsync<DispatchPackingTaskEntity>(new CommandDefinition("""
                SELECT * FROM `wms_dispatch_packing_task` WHERE `dispatch_order_id`=@orderId AND `is_active`=1 ORDER BY `id` FOR UPDATE;
                """, new { orderId }, tx, cancellationToken: ct))).AsList();
            if (tasks.Count == 0)
                throw DispatchWorkflowCommandException.StatusNotAllowedForRollback("the order has no active packing task to roll back");
            var now = DateTime.Now;
            foreach (var task in tasks)
            {
                // 释放任务：与 CancelTaskAsync 同一不变量，active_source_task_id 置空后任务回到装箱任务列表。
                await c.ExecuteAsync(new CommandDefinition("""
                    UPDATE `wms_dispatch_packing_task_item` SET `is_active`=0,`last_update_time`=@now,`row_version`=`row_version`+1
                    WHERE `packing_task_id`=@id;
                    UPDATE `wms_dispatch_packing_task` SET `is_active`=0,`active_source_task_id`=NULL,`status`=@status,
                      `last_update_time`=@now,`row_version`=`row_version`+1 WHERE `id`=@id;
                    """, new { now, id = task.id, status = DispatchOrderStatus.ManualCancelled }, tx, cancellationToken: ct));
            }
            var resultVersion = order.row_version + 1;
            var updated = await c.ExecuteAsync(new CommandDefinition("""
                UPDATE `wms_dispatch_order` SET `status`=@status,`last_update_time`=@now,`row_version`=`row_version`+1
                WHERE `id`=@id AND `row_version`=@expected;
                """, new { status = DispatchOrderStatus.ManualCancelled, now, id = orderId, expected = order.row_version },
                tx, cancellationToken: ct));
            if (updated != 1) throw DispatchWorkflowCommandException.ConcurrencyConflict();
            await InsertOperationAsync(c, tx, orderId, DispatchWorkflowOperation.RollbackPendingPick, requestId,
                DispatchOrderStatus.ManualCancelled, resultVersion, user, now, ct);
            await tx.CommitAsync(ct);
            return new RollbackPendingPickResult
            {
                order_id = orderId,
                request_id = requestId,
                status = ToApiStatus(DispatchOrderStatus.ManualCancelled),
                row_version = resultVersion
            };
        }
        catch (Exception exception) when (IsDatabaseConcurrency(exception))
        {
            await tx.RollbackAsync(CancellationToken.None);
            var winner = await FindOperationAsync(c, null, orderId, DispatchWorkflowOperation.RollbackPendingPick,
                requestId, CancellationToken.None);
            if (winner?.result_status == DispatchWorkflowOperationResultStatus.Succeeded)
                return RollbackFromLedger(winner, requestId);
            throw DispatchWorkflowCommandException.ConcurrencyConflict();
        }
        catch
        {
            await tx.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private static void ValidateRollbackRequest(int orderId, RollbackPendingPickRequest request)
    {
        if (orderId <= 0 || string.IsNullOrWhiteSpace(request.request_id)
            || request.request_id.Trim().Length > 64 || request.row_version < 0)
            throw new ArgumentException("order id, request_id and row_version are required", nameof(request));
    }

    private static RollbackPendingPickResult RollbackFromLedger(DispatchWorkflowOperationEntity operation, string requestId)
    {
        if (operation.result_order_status == null || operation.result_row_version == null)
            throw DispatchWorkflowCommandException.ConcurrencyConflict();
        return new RollbackPendingPickResult
        {
            order_id = operation.dispatch_order_id,
            request_id = requestId,
            status = ToApiStatus(operation.result_order_status.Value),
            row_version = operation.result_row_version.Value
        };
    }
}

public sealed partial class DispatchWorkflowCommandException
{
    public static DispatchWorkflowCommandException StatusNotAllowedForRollback(
        string detail = "only a pending-pick order can be rolled back") =>
        new("STATUS_NOT_ALLOWED", detail);
}
