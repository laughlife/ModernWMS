using System.Data;
using Dapper;
using ModernWMS.WMS.IServices.PackingTask;

namespace ModernWMS.WMS.Services;

/// <summary>
/// Read-and-settle compatibility adapter for historical packing selections.
/// New bindings never call this adapter and never create an allocation decomposition.
/// </summary>
public sealed class LegacyPackingSelectionReleaseAdapter : ILegacyPackingSelectionReleaseAdapter
{
    /// <inheritdoc />
    public async Task SettleReleaseAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        long erpStockId,
        long allocationId,
        long reservationItemId,
        long quantity,
        string operatorName,
        CancellationToken cancellationToken = default)
    {
        if (erpStockId <= 0) throw new ArgumentOutOfRangeException(nameof(erpStockId));
        if (allocationId <= 0) throw new ArgumentOutOfRangeException(nameof(allocationId));
        if (reservationItemId <= 0) throw new ArgumentOutOfRangeException(nameof(reservationItemId));
        if (quantity <= 0) throw new ArgumentOutOfRangeException(nameof(quantity));
        var allocation = await connection.QuerySingleOrDefaultAsync<AllocationRow>(new CommandDefinition(
            """
            SELECT `id` Id,`erp_stock_id` ErpStockId,`occupied_qty` OccupiedQty,
                   `row_version` RowVersion
              FROM `wms_erp_stock_allocation`
             WHERE `id`=@AllocationId AND `erp_stock_id`=@ErpStockId
             FOR UPDATE;
            """, new { AllocationId = allocationId, ErpStockId = erpStockId }, transaction,
            cancellationToken: cancellationToken))
            ?? throw new InvalidOperationException("历史位置分配不存在或不属于绑定的ERP库存");
        var decomposition = await connection.QuerySingleOrDefaultAsync<ReservationAllocationRow>(
            new CommandDefinition(
                """
                SELECT `id` Id,`remaining_qty` RemainingQty,`released_qty` ReleasedQty,
                       `row_version` RowVersion
                  FROM `wms_erp_stock_reservation_allocation`
                 WHERE `reservation_item_id`=@ReservationItemId
                   AND `stock_allocation_id`=@AllocationId AND `deleted`=b'0'
                 FOR UPDATE;
                """, new { ReservationItemId = reservationItemId, AllocationId = allocationId },
                transaction, cancellationToken: cancellationToken))
            ?? throw new InvalidOperationException("历史位置预占分解不存在，无法安全结清");
        if (allocation.OccupiedQty < quantity || decomposition.RemainingQty < quantity)
            throw new InvalidOperationException("历史位置预占剩余数量不足，无法安全结清");

        var now = DateTime.Now;
        var allocationAffected = await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE `wms_erp_stock_allocation`
               SET `occupied_qty`=`occupied_qty`-@Quantity,
                   `row_version`=`row_version`+1,`updater`=@OperatorName,`update_time`=@Now
             WHERE `id`=@Id AND `row_version`=@RowVersion
               AND `occupied_qty`=@BeforeOccupied;
            """, new
        {
            allocation.Id,
            allocation.RowVersion,
            Quantity = quantity,
            BeforeOccupied = allocation.OccupiedQty,
            OperatorName = NormalizeOperator(operatorName),
            Now = now
        }, transaction, cancellationToken: cancellationToken));
        if (allocationAffected != 1)
            throw new InvalidOperationException("历史位置分配已被并发修改");

        var remainingAfter = decomposition.RemainingQty - quantity;
        var decompositionAffected = await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE `wms_erp_stock_reservation_allocation`
               SET `released_qty`=`released_qty`+@Quantity,`remaining_qty`=@RemainingAfter,
                   `status`=CASE WHEN @RemainingAfter=0 THEN 'RELEASED' ELSE 'PARTIALLY_SETTLED' END,
                   `row_version`=`row_version`+1,`updater`=@OperatorName,`update_time`=@Now
             WHERE `id`=@Id AND `row_version`=@RowVersion
               AND `remaining_qty`=@BeforeRemaining;
            """, new
        {
            decomposition.Id,
            decomposition.RowVersion,
            Quantity = quantity,
            RemainingAfter = remainingAfter,
            BeforeRemaining = decomposition.RemainingQty,
            OperatorName = NormalizeOperator(operatorName),
            Now = now
        }, transaction, cancellationToken: cancellationToken));
        if (decompositionAffected != 1)
            throw new InvalidOperationException("历史位置预占分解已被并发修改");
    }

    private static string NormalizeOperator(string value)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? "ModernWMS" : value.Trim();
        return normalized.Length <= 64 ? normalized : normalized[..64];
    }

    private sealed class AllocationRow
    {
        public long Id { get; init; }
        public long ErpStockId { get; init; }
        public long OccupiedQty { get; init; }
        public long RowVersion { get; init; }
    }

    private sealed class ReservationAllocationRow
    {
        public long Id { get; init; }
        public long RemainingQty { get; init; }
        public long ReleasedQty { get; init; }
        public long RowVersion { get; init; }
    }
}
