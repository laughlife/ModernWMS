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
    /// <summary>
    /// 定义 EnrichPickingRowsAsync 操作。
    /// </summary>
    Task EnrichPickingRowsAsync(List<DispatchlistViewModel> rows, CurrentUser currentUser);

    /// <summary>
    /// 定义 CompletePickingAsync 操作。
    /// </summary>
    Task<(bool flag, string msg)> CompletePickingAsync(List<int> ids, CurrentUser currentUser);

    /// <summary>
    /// 定义 RepickAsync 操作。
    /// </summary>
    Task<(bool flag, string msg)> RepickAsync(int id, CurrentUser currentUser);

    /// <summary>
    /// 定义 StartWeighingAsync 操作。
    /// </summary>
    Task<(bool flag, string msg)> StartWeighingAsync(int id, CurrentUser currentUser);

    /// <summary>
    /// 定义 UndoWeighingAsync 操作。
    /// </summary>
    Task<(bool flag, string msg)> UndoWeighingAsync(int id, CurrentUser currentUser);

    /// <summary>
    /// 定义 ReturnToWeighingAsync 操作。
    /// </summary>
    Task<(bool flag, string msg)> ReturnToWeighingAsync(int id, CurrentUser currentUser);

    /// <summary>
    /// 定义 CompleteWeighingAsync 操作。
    /// </summary>
    Task<(bool flag, string msg)> CompleteWeighingAsync(int id, CurrentUser currentUser);

    /// <summary>
    /// 定义 UndoDeliveryAsync 操作。
    /// </summary>
    Task<(bool flag, string msg)> UndoDeliveryAsync(int id, CurrentUser currentUser);

    /// <summary>
    /// 定义 GetOutboundCarrierOptionsAsync 操作。
    /// </summary>
    Task<List<OutboundCarrierOptionViewModel>> GetOutboundCarrierOptionsAsync();

    /// <summary>
    /// 定义 SetOutboundVolumeDivisorAsync 操作。
    /// </summary>
    Task<(bool flag, string msg)> SetOutboundVolumeDivisorAsync(SetOutboundVolumeDivisorViewModel viewModel, CurrentUser currentUser);

    /// <summary>
    /// 定义 SetOutboundCarrierAsync 操作。
    /// </summary>
    Task<(bool flag, string msg)> SetOutboundCarrierAsync(SetOutboundCarrierViewModel viewModel, CurrentUser currentUser);

    /// <summary>
    /// 定义 GetWeighingShipmentsAsync 操作。
    /// </summary>
    Task<(List<DispatchWeighingShipmentViewModel> data, int totals)> GetWeighingShipmentsAsync(PageSearch pageSearch, CurrentUser currentUser);

    /// <summary>
    /// 定义 GetWeighingBoxesAsync 操作。
    /// </summary>
    Task<List<DispatchWeighingBoxViewModel>> GetWeighingBoxesAsync(string dispatchNo, long shipmentId, CurrentUser currentUser);

    /// <summary>
    /// 定义 SaveWeighingBoxesAsync 操作。
    /// </summary>
    Task<(bool flag, string msg)> SaveWeighingBoxesAsync(List<SaveDispatchWeighingBoxViewModel> viewModels, CurrentUser currentUser);
}
