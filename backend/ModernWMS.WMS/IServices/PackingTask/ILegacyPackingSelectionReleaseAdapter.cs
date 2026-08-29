using System.Data;
using ModernWMS.Core.DI;

namespace ModernWMS.WMS.IServices.PackingTask;

/// <summary>Settles an already-existing historical allocation decomposition.</summary>
public interface ILegacyPackingSelectionReleaseAdapter : IDependency
{
    /// <summary>Releases existing allocation occupation without creating any position row.</summary>
    Task SettleReleaseAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        long erpStockId,
        long allocationId,
        long reservationItemId,
        long quantity,
        string operatorName,
        CancellationToken cancellationToken = default);

    /// <summary>Consumes existing allocation occupation without creating any position row.</summary>
    Task SettleConsumeAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        long erpStockId,
        long allocationId,
        long reservationItemId,
        long quantity,
        string operatorName,
        CancellationToken cancellationToken = default);
}
