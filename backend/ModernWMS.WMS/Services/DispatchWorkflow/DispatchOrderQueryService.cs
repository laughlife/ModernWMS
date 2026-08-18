using Dapper;
using ModernWMS.Core.Database;
using ModernWMS.Core.JWT;
using ModernWMS.WMS.Entities.Models;
using ModernWMS.WMS.Entities.ViewModels.DispatchWorkflow;
using ModernWMS.WMS.IServices;
using ModernWMS.WMS.IServices.DispatchWorkflow;

namespace ModernWMS.WMS.Services.DispatchWorkflow;

public sealed class DispatchOrderQueryService : IDispatchOrderQueryService
{
    private readonly IMySqlConnectionFactory _connectionFactory;
    private readonly IWarehouseAccessService _warehouseAccessService;
    private readonly IDispatchWorkflowService _workflowService;

    public DispatchOrderQueryService(IMySqlConnectionFactory connectionFactory,
        IWarehouseAccessService warehouseAccessService, IDispatchWorkflowService workflowService)
    {
        _connectionFactory = connectionFactory;
        _warehouseAccessService = warehouseAccessService;
        _workflowService = workflowService;
    }

    public async Task<DispatchOrderPageResult> PageAsync(DispatchOrderPageRequest request, CurrentUser currentUser,
        CancellationToken cancellationToken = default)
    {
        await _warehouseAccessService.EnsureAllowedAsync(request.warehouse_id, currentUser);
        if (string.IsNullOrWhiteSpace(request.status)
            || DispatchWorkflowService.ParseApiStatus(request.status) == DispatchOrderStatus.PendingPick)
            await ReconcilePendingOrdersAsync(request.warehouse_id, currentUser, cancellationToken);

        var status = string.IsNullOrWhiteSpace(request.status)
            ? (DispatchOrderStatus?)null : DispatchWorkflowService.ParseApiStatus(request.status);
        var keyword = request.keyword.Trim();
        var pageIndex = Math.Max(request.pageIndex, 1);
        var pageSize = Math.Clamp(request.pageSize, 1, 200);
        var p = new { request.warehouse_id, tenantId = currentUser.tenant_id, status, keyword = $"%{EscapeLike(keyword)}%", hasKeyword = keyword.Length > 0,
            pageSize, offset = (pageIndex - 1) * pageSize };
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        using var result = await connection.QueryMultipleAsync(new CommandDefinition("""
            SELECT COUNT(*) FROM `wms_dispatch_order` o
            WHERE o.`warehouse_id`=@warehouse_id AND o.`tenant_id`=@tenantId AND (@status IS NULL OR o.`status`=@status)
              AND (@hasKeyword=0 OR o.`dispatch_no` LIKE @keyword ESCAPE '!'
                OR EXISTS(SELECT 1 FROM `wms_dispatch_packing_task` t WHERE t.`dispatch_order_id`=o.`id`
                  AND t.`is_active`=1 AND t.`source_task_no` LIKE @keyword ESCAPE '!'));
            SELECT o.* FROM `wms_dispatch_order` o
            WHERE o.`warehouse_id`=@warehouse_id AND o.`tenant_id`=@tenantId AND (@status IS NULL OR o.`status`=@status)
              AND (@hasKeyword=0 OR o.`dispatch_no` LIKE @keyword ESCAPE '!'
                OR EXISTS(SELECT 1 FROM `wms_dispatch_packing_task` t WHERE t.`dispatch_order_id`=o.`id`
                  AND t.`is_active`=1 AND t.`source_task_no` LIKE @keyword ESCAPE '!'))
            ORDER BY o.`create_time` DESC,o.`id` DESC LIMIT @pageSize OFFSET @offset;
            """, p, cancellationToken: cancellationToken));
        var totals = await result.ReadSingleAsync<int>();
        var orders = (await result.ReadAsync<DispatchOrderEntity>()).AsList();
        if (orders.Count > 0)
        {
            var ids = orders.Select(x => x.id).ToArray();
            var tasks = (await connection.QueryAsync<DispatchPackingTaskEntity>(new CommandDefinition("""
                SELECT * FROM `wms_dispatch_packing_task` WHERE `dispatch_order_id` IN @ids AND `is_active`=1;
                """, new { ids }, cancellationToken: cancellationToken))).AsList();
            var events = (await connection.QueryAsync<DispatchSourceChangeEventEntity>(new CommandDefinition("""
                SELECT * FROM `wms_dispatch_source_change_event` WHERE `dispatch_order_id` IN @ids AND `decision`=@anomaly;
                """, new { ids, anomaly = DispatchSourceChangeDecision.OutboundAnomaly }, cancellationToken: cancellationToken))).AsList();
            foreach (var order in orders)
            {
                order.packing_tasks = tasks.Where(x => x.dispatch_order_id == order.id).ToList();
                order.source_change_events = events.Where(x => x.dispatch_order_id == order.id).ToList();
            }
        }
        return new DispatchOrderPageResult(orders.Select(ToSummary).ToList(), totals);
    }

    public async Task<DispatchOrderStatusCounts> CountsAsync(long warehouseId, CurrentUser currentUser,
        CancellationToken cancellationToken = default)
    {
        await _warehouseAccessService.EnsureAllowedAsync(warehouseId, currentUser);
        await ReconcilePendingOrdersAsync(warehouseId, currentUser, cancellationToken);
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        var raw = await connection.QueryAsync<StatusCountRow>(new CommandDefinition("""
            SELECT `status`,COUNT(*) `count` FROM `wms_dispatch_order`
            WHERE `warehouse_id`=@warehouseId AND `tenant_id`=@tenantId GROUP BY `status`;
            """, new { warehouseId, tenantId = currentUser.tenant_id }, cancellationToken: cancellationToken));
        var counts = Enum.GetValues<DispatchOrderStatus>().ToDictionary(DispatchWorkflowService.ToApiStatus, _ => 0);
        foreach (var item in raw) counts[DispatchWorkflowService.ToApiStatus(item.status)] = item.count;
        return new DispatchOrderStatusCounts(counts);
    }

    public async Task<DispatchOrderDetailViewModel> GetAsync(int orderId, CurrentUser currentUser,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        var order = await DispatchWorkflowService.LoadOrderAggregateAsync(connection, orderId, cancellationToken);
        await _warehouseAccessService.EnsureAllowedAsync(order.warehouse_id, currentUser);
        return order.status == DispatchOrderStatus.PendingPick
            ? await _workflowService.ReconcileAsync(orderId, currentUser, cancellationToken)
            : DispatchWorkflowService.ToDetail(order);
    }

    private async Task ReconcilePendingOrdersAsync(long warehouseId, CurrentUser currentUser, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        var ids = await connection.QueryAsync<int>(new CommandDefinition("""
            SELECT `id` FROM `wms_dispatch_order`
            WHERE `warehouse_id`=@warehouseId AND `tenant_id`=@tenantId AND `status`=@status ORDER BY `id`;
            """, new { warehouseId, tenantId = currentUser.tenant_id, status = DispatchOrderStatus.PendingPick }, cancellationToken: cancellationToken));
        foreach (var id in ids) await _workflowService.ReconcileAsync(id, currentUser, cancellationToken);
    }

    private static DispatchOrderSummaryViewModel ToSummary(DispatchOrderEntity order)
    {
        var anomaly = DispatchWorkflowService.LatestOutboundAnomaly(order);
        return new DispatchOrderSummaryViewModel
        {
            id=order.id,dispatch_no=order.dispatch_no,warehouse_id=order.warehouse_id,status=DispatchWorkflowService.ToApiStatus(order.status),
            packing_task_nos=order.packing_tasks.Where(x=>x.is_active).OrderBy(x=>x.source_task_no).Select(x=>x.source_task_no).ToList(),
            creator=order.creator,create_time=order.create_time,last_update_time=order.last_update_time,
            source_change_pending=order.source_change_pending,pending_source_version=order.pending_source_version,
            source_change_snapshot=order.source_change_snapshot,accepted_source_version=order.accepted_source_version,
            signed_qty=order.signed_qty,damaged_qty=order.damaged_qty,signed_at=order.signed_at,signed_by_name=order.signed_by_name,
            notification_status=order.notification_status.ToString().ToUpperInvariant(),notification_last_error=order.notification_last_error,
            outbound_source_anomaly=anomaly!=null,outbound_source_anomaly_snapshot=anomaly?.diff_snapshot??"",row_version=order.row_version
        };
    }

    private static string EscapeLike(string value) => value.Replace("!","!!").Replace("%","!%").Replace("_","!_");
    private sealed class StatusCountRow { public DispatchOrderStatus status { get; set; } public int count { get; set; } }
}
