using System.Data;
using ModernWMS.Core.DI;

namespace ModernWMS.WMS.IServices.StockAllocation;

/// <summary>
/// Coordinates ERP stock balance mutations and WMS location-allocation mutations
/// inside a transaction owned by the calling business service.
/// </summary>
public interface IStockAllocationMutationService : IDependency
{
    /// <summary>
    /// Acquires shared runtime gates, ERP stock locks and allocation locks in the
    /// canonical order for a multi-stock business transaction.
    /// </summary>
    Task PrelockAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        long tenantId,
        IReadOnlyCollection<long> erpWarehouseIds,
        IReadOnlyCollection<long> erpStockIds,
        IReadOnlyCollection<long> allocationIds,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Locks every reservation owner before taking any ERP stock/allocation lock.
    /// Required for commands that mutate more than one reservation item.
    /// </summary>
    Task PrelockReservationOwnersAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        long tenantId,
        IReadOnlyCollection<long> erpWarehouseIds,
        IReadOnlyCollection<StockReservationPrelockRequest> requests,
        CancellationToken cancellationToken = default);

    Task<StockAllocationMutationResult> AdjustAvailableAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        StockMutationContext context,
        long erpStockId,
        long allocationId,
        long quantityDelta,
        CancellationToken cancellationToken = default);

    Task<StockAllocationMutationResult> ReserveAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        StockMutationContext context,
        long erpStockId,
        long allocationId,
        long quantity,
        CancellationToken cancellationToken = default);

    Task<StockAllocationMutationResult> ReleaseAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        StockMutationContext context,
        long erpStockId,
        long allocationId,
        long quantity,
        CancellationToken cancellationToken = default);

    Task<StockAllocationMutationResult> ShipLockedAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        StockMutationContext context,
        long erpStockId,
        long allocationId,
        long quantity,
        CancellationToken cancellationToken = default);

    Task<StockAllocationMutationResult> MoveLocationAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        StockMutationContext context,
        long erpStockId,
        long sourceAllocationId,
        long targetAllocationId,
        long quantity,
        CancellationToken cancellationToken = default);
}
