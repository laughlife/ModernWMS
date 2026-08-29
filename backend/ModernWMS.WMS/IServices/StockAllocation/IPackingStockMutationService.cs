using System.Data;
using ModernWMS.Core.DI;

namespace ModernWMS.WMS.IServices.StockAllocation;

/// <summary>Mutates packing inventory by <c>trk_stock.id</c> only.</summary>
public interface IPackingStockMutationService : IDependency
{
    /// <summary>Locks reservation owners and stock rows in deterministic order.</summary>
    Task PrelockAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        IReadOnlyCollection<long> erpWarehouseIds,
        IReadOnlyCollection<PackingStockPrelockRequest> requests,
        CancellationToken cancellationToken = default);

    /// <summary>Moves available quantity into occupied quantity.</summary>
    Task<PackingStockMutationResult> ReserveAsync(
        IDbConnection connection, IDbTransaction transaction, StockMutationContext context,
        long erpStockId, long quantity, CancellationToken cancellationToken = default);

    /// <summary>Returns occupied quantity to available quantity.</summary>
    Task<PackingStockMutationResult> ReleaseAsync(
        IDbConnection connection, IDbTransaction transaction, StockMutationContext context,
        long erpStockId, long quantity, CancellationToken cancellationToken = default);

    /// <summary>Consumes occupied quantity irreversibly.</summary>
    Task<PackingStockMutationResult> ShipLockedAsync(
        IDbConnection connection, IDbTransaction transaction, StockMutationContext context,
        long erpStockId, long quantity, CancellationToken cancellationToken = default);

    /// <summary>Adjusts available and total quantity without a location prerequisite.</summary>
    Task<PackingStockMutationResult> AdjustAvailableAsync(
        IDbConnection connection, IDbTransaction transaction, StockMutationContext context,
        long erpStockId, long quantityDelta, CancellationToken cancellationToken = default);
}
