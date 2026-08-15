using ModernWMS.Core.DI;
using ModernWMS.WMS.Entities.ViewModels.PackingTask;

namespace ModernWMS.WMS.IServices.PackingTask;

/// <summary>
/// Read-only boundary over XXL-maintained SellFox tables. Implementations must not
/// call SellFox HTTP or persist any source value as a WMS measurement.
/// </summary>
public interface IPackingTaskSourceReader : IDependency
{
    Task<PackingTaskSourceCapability> VerifyCapabilityAsync(
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PackingTaskSourceSnapshot>> ReadAsync(
        IReadOnlyCollection<long> sourceTaskIds,
        CancellationToken cancellationToken = default);
}
