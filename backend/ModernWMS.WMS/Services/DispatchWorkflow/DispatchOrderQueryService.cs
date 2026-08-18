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
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        var parameters = new DynamicParameters();
        parameters.Add("warehouse_id", request.warehouse_id);
        parameters.Add("tenantId", currentUser.tenant_id);
        parameters.Add("status", status);
        parameters.Add("keyword", $"%{EscapeLike(keyword)}%");
        parameters.Add("hasKeyword", keyword.Length > 0);
        parameters.Add("pageSize", pageSize);
        parameters.Add("offset", (pageIndex - 1) * pageSize);

        var creatorWhere = string.Empty;
        if (request.group_id is > 0)
        {
            var groupMemberNames = (await connection.QueryAsync<string>(new CommandDefinition("""
                WITH RECURSIVE dept_tree AS (
                    SELECT d.`id`, 0 AS depth FROM `system_dept` d
                    WHERE d.`id`=@groupId AND d.`deleted`=0 AND d.`status`=0 AND d.`dept`='operator'
                    UNION ALL
                    SELECT c.`id`, t.depth + 1 FROM `system_dept` c
                    JOIN dept_tree t ON c.`parent_id`=t.`id`
                    WHERE c.`deleted`=0 AND c.`status`=0 AND t.depth < 20
                )
                SELECT DISTINCT u.`nickname` FROM `system_users` u
                JOIN dept_tree t ON u.`dept_id`=t.`id`
                WHERE u.`deleted`=0 AND u.`status`=0 AND u.`nickname` IS NOT NULL AND u.`nickname`<>'';
                """, new { groupId = request.group_id.Value }, cancellationToken: cancellationToken))).AsList();
            if (groupMemberNames.Count == 0) return new DispatchOrderPageResult([], 0);
            parameters.Add("groupMemberNames", groupMemberNames);
            creatorWhere += " AND source.`create_name` IN @groupMemberNames";
        }

        if (request.member_id is > 0)
        {
            var memberName = await connection.QuerySingleOrDefaultAsync<string>(new CommandDefinition("""
                SELECT `nickname` FROM `system_users`
                WHERE `id`=@memberId AND `deleted`=0 AND `status`=0 LIMIT 1;
                """, new { memberId = request.member_id.Value }, cancellationToken: cancellationToken));
            if (string.IsNullOrWhiteSpace(memberName)) return new DispatchOrderPageResult([], 0);
            parameters.Add("memberName", memberName);
            creatorWhere += " AND source.`create_name`=@memberName";
        }

        var creatorFilter = creatorWhere.Length == 0 ? string.Empty : $"""
              AND EXISTS(
                SELECT 1 FROM `wms_dispatch_packing_task` creator_task
                JOIN `ruiyi_sellfox_packing_task` source
                  ON source.`sellfox_task_id`=creator_task.`source_task_id`
                WHERE creator_task.`dispatch_order_id`=o.`id` AND creator_task.`is_active`=1
                  {creatorWhere})
            """;
        var orderWhere = $"""
            FROM `wms_dispatch_order` o
            WHERE o.`warehouse_id`=@warehouse_id AND o.`tenant_id`=@tenantId
              AND (@status IS NULL OR o.`status`=@status)
              AND (@hasKeyword=0 OR o.`dispatch_no` LIKE @keyword ESCAPE '!'
                OR EXISTS(
                  SELECT 1 FROM `wms_dispatch_packing_task` search_task
                  LEFT JOIN `wms_dispatch_packing_task_item` search_item
                    ON search_item.`packing_task_id`=search_task.`id` AND search_item.`is_active`=1
                  WHERE search_task.`dispatch_order_id`=o.`id` AND search_task.`is_active`=1
                    AND (search_task.`source_task_no` LIKE @keyword ESCAPE '!'
                      OR search_item.`commodity_name` LIKE @keyword ESCAPE '!'
                      OR search_item.`commodity_sku` LIKE @keyword ESCAPE '!'
                      OR search_item.`fn_sku` LIKE @keyword ESCAPE '!'
                      OR search_item.`msku` LIKE @keyword ESCAPE '!')))
              {creatorFilter}
            """;
        using var result = await connection.QueryMultipleAsync(new CommandDefinition($"""
            SELECT COUNT(*)
            {orderWhere};
            SELECT o.*
            {orderWhere}
            ORDER BY o.`create_time` DESC,o.`id` DESC LIMIT @pageSize OFFSET @offset;
            """, parameters, cancellationToken: cancellationToken));
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
