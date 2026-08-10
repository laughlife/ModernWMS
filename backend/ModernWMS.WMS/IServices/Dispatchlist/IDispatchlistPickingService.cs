using ModernWMS.Core.DI;
using ModernWMS.Core.JWT;
using ModernWMS.WMS.Entities.ViewModels;

namespace ModernWMS.WMS.IServices;

/// <summary>
/// Picking-list enrichment and explicit line-level workflow transitions.
/// </summary>
public interface IDispatchlistPickingService : IDependency
{
    Task EnrichPickingRowsAsync(List<DispatchlistViewModel> rows, CurrentUser currentUser);

    Task<(bool flag, string msg)> CompletePickingAsync(List<int> ids, CurrentUser currentUser);

    Task<(bool flag, string msg)> RepickAsync(int id, CurrentUser currentUser);

    Task<(bool flag, string msg)> StartWeighingAsync(int id, CurrentUser currentUser);
}
