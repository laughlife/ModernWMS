using ModernWMS.Core.DI;
using ModernWMS.Core.JWT;
using ModernWMS.Core.Models;
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

    Task<(bool flag, string msg)> UndoWeighingAsync(int id, CurrentUser currentUser);

    Task<(bool flag, string msg)> UndoDeliveryAsync(int id, CurrentUser currentUser);

    Task<(List<DispatchWeighingShipmentViewModel> data, int totals)> GetWeighingShipmentsAsync(PageSearch pageSearch, CurrentUser currentUser);

    Task<List<DispatchWeighingBoxViewModel>> GetWeighingBoxesAsync(string dispatchNo, long shipmentId, CurrentUser currentUser);

    Task<(bool flag, string msg)> SaveWeighingBoxesAsync(List<SaveDispatchWeighingBoxViewModel> viewModels, CurrentUser currentUser);
}
