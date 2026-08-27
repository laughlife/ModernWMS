using System.Data;
using System.Text.Json;
using Dapper;
using ModernWMS.Core.JWT;
using ModernWMS.WMS.Entities.Models;
using ModernWMS.WMS.Entities.ViewModels.DispatchWorkflow;

namespace ModernWMS.WMS.Services.DispatchWorkflow;

/// <summary>
/// 表示 DispatchWorkflowService 类型。
/// </summary>
public partial class DispatchWorkflowService
{
    private const string CarrierSettingAuthority = "delivered-setCarrier";
    private const long ShenzhenSelfWarehouseId = 320118;
    private const string ShenzhenSelfWarehouseName = "有座山深圳仓";

    /// <summary>
    /// 执行 GetCarrierOptionsAsync 操作。
    /// </summary>
    public async Task<List<DispatchCarrierOptionViewModel>> GetCarrierOptionsAsync(
        CurrentUser currentUser, CancellationToken cancellationToken = default)
    {
        if (!await HasCarrierSettingAuthorityAsync(currentUser, cancellationToken))
            throw new UnauthorizedAccessException("没有待出库设置权限");

        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        return (await connection.QueryAsync<DispatchCarrierOptionViewModel>(new CommandDefinition("""
            SELECT `id`,COALESCE(`name`,'') `name`
            FROM `erp_warehouse`
            WHERE `deleted`=0 AND `attr`='国内仓库' AND `name` IS NOT NULL AND `name`<>''
              AND `id`<>@excludedId AND `name`<>@excludedName
            ORDER BY `name`,`id`;
            """, new { excludedId = ShenzhenSelfWarehouseId, excludedName = ShenzhenSelfWarehouseName },
            cancellationToken: cancellationToken))).AsList();
    }

    /// <summary>
    /// 执行 SetCarrierAsync 操作。
    /// </summary>
    public async Task<SetDispatchCarrierResult> SetCarrierAsync(
        SetDispatchCarrierRequest request, CurrentUser currentUser,
        CancellationToken cancellationToken = default)
    {
        var orderIds = request.order_ids.Where(x => x > 0).Distinct().OrderBy(x => x).ToArray();
        if (orderIds.Length == 0 || orderIds.Length > 200 || request.carrier_warehouse_id <= 0)
            throw new ArgumentException("请选择待出库拣货单和承运仓库", nameof(request));
        if (!await HasCarrierSettingAuthorityAsync(currentUser, cancellationToken))
            throw new UnauthorizedAccessException("没有待出库设置权限");

        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        try
        {
            var carrier = await connection.QuerySingleOrDefaultAsync<DispatchCarrierOptionViewModel>(
                new CommandDefinition("""
                    SELECT `id`,COALESCE(`name`,'') `name`
                    FROM `erp_warehouse`
                    WHERE `id`=@carrierId AND `deleted`=0 AND `attr`='国内仓库'
                      AND `name` IS NOT NULL AND `name`<>'' AND `id`<>@excludedId AND `name`<>@excludedName
                    LIMIT 1;
                    """, new
                    {
                        carrierId = request.carrier_warehouse_id,
                        excludedId = ShenzhenSelfWarehouseId,
                        excludedName = ShenzhenSelfWarehouseName
                    }, transaction, cancellationToken: cancellationToken));
            if (carrier == null) throw new ArgumentException("所选承运仓库不可用", nameof(request));

            var orders = (await connection.QueryAsync<DispatchOrderEntity>(new CommandDefinition("""
                SELECT * FROM `wms_dispatch_order`
                WHERE `id` IN @orderIds
                ORDER BY `id` FOR UPDATE;
                """, new { orderIds}, transaction,
                cancellationToken: cancellationToken))).AsList();
            if (orders.Count != orderIds.Length || orders.Any(x => x.status != DispatchOrderStatus.PendingOutbound))
                throw DispatchWorkflowCommandException.StatusNotAllowedForCarrier();
            foreach (var warehouseId in orders.Select(x => x.warehouse_id).Distinct())
                await _warehouseAccessService.EnsureAllowedAsync(warehouseId, currentUser);

            var detailStates = (await connection.QueryAsync<CarrierDetailState>(new CommandDefinition("""
                SELECT `dispatch_order_id`,`dispatch_status`
                FROM `wms_dispatchlist`
                WHERE `dispatch_order_id` IN @orderIds
                ORDER BY `dispatch_order_id`,`id` FOR UPDATE;
                """, new { orderIds}, transaction,
                cancellationToken: cancellationToken))).AsList();
            var detailGroups = detailStates.GroupBy(x => x.dispatch_order_id).ToDictionary(x => x.Key, x => x.ToList());
            if (detailGroups.Count != orderIds.Length || orderIds.Any(id => !detailGroups.TryGetValue(id, out var rows)
                || rows.Count == 0 || rows.Any(x => x.dispatch_status != 5)))
                throw DispatchWorkflowCommandException.StatusNotAllowedForCarrier();

            var now = DateTime.Now;
            await connection.ExecuteAsync(new CommandDefinition("""
                UPDATE `wms_dispatchlist`
                SET `carrier_warehouse_id`=@carrierId,`carrier_unit`=@carrierName,`last_update_time`=@now
                WHERE `dispatch_order_id` IN @orderIds  AND `dispatch_status`=5;
                UPDATE `wms_dispatch_order`
                SET `last_update_time`=@now,`row_version`=`row_version`+1
                WHERE `id` IN @orderIds  AND `status`=@status;
                """, new
                {
                    carrierId = carrier.id,
                    carrierName = carrier.name,
                    now,
                    orderIds,
                    status = DispatchOrderStatus.PendingOutbound
                }, transaction, cancellationToken: cancellationToken));
            await transaction.CommitAsync(cancellationToken);
            return new SetDispatchCarrierResult
            {
                updated_order_count = orderIds.Length,
                carrier_warehouse_id = carrier.id,
                carrier_unit = carrier.name
            };
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private async Task<bool> HasCarrierSettingAuthorityAsync(
        CurrentUser currentUser, CancellationToken cancellationToken)
    {
        var roleName = currentUser.user_role?.Trim() ?? string.Empty;
        if (string.Equals(roleName, "admin", StringComparison.OrdinalIgnoreCase)) return true;
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        var roleId = await connection.QuerySingleOrDefaultAsync<int?>(new CommandDefinition("""
            SELECT `id` FROM `wms_userrole`
            WHERE `is_valid`=1 AND `role_name`=@roleName LIMIT 1;
            """, new { roleName }, cancellationToken: cancellationToken));
        if (!roleId.HasValue) return false;
        var values = await connection.QueryAsync<string>(new CommandDefinition("""
            SELECT `menu_actions_authority` FROM `wms_rolemenu`
            WHERE `userrole_id`=@roleId AND `authority`=1;
            """, new { roleId = roleId.Value },
            cancellationToken: cancellationToken));
        return values.Any(value =>
        {
            try
            {
                return (JsonSerializer.Deserialize<List<string>>(value) ?? [])
                    .Any(x => string.Equals(x?.Trim(), CarrierSettingAuthority, StringComparison.Ordinal));
            }
            catch (JsonException) { return false; }
        });
    }

    private sealed class CarrierDetailState
    {
        public int dispatch_order_id { get; set; }
        public byte dispatch_status { get; set; }
    }
}

/// <summary>
/// 表示 DispatchWorkflowCommandException 类型。
/// </summary>
public sealed partial class DispatchWorkflowCommandException
{
    /// <summary>
    /// 执行 StatusNotAllowedForCarrier 操作。
    /// </summary>
    public static DispatchWorkflowCommandException StatusNotAllowedForCarrier() =>
        new("STATUS_NOT_ALLOWED", "仅待出库且明细状态完整的拣货单可以设置承运信息");

    /// <summary>
    /// 执行 CarrierRequired 操作。
    /// </summary>
    public static DispatchWorkflowCommandException CarrierRequired() =>
        new("CARRIER_REQUIRED", "请先设置承运信息再确认出库");
}
