using ModernWMS.Core.DI;
using ModernWMS.Core.JWT;
using ModernWMS.Core.Models;
using ModernWMS.WMS.Entities.ViewModels;

namespace ModernWMS.WMS.IServices;

/// <summary>
/// Reads ERP-prepared FBA shipments for the Shenzhen self-operated warehouse.
/// </summary>
public interface IFbaShipmentService : IDependency
{
    /// <summary>
    /// 定义 PageAsync 操作。
    /// </summary>
    Task<(List<FbaShipmentViewModel> data, int totals)> PageAsync(PageSearch pageSearch, CurrentUser currentUser);

    /// <summary>
    /// 定义 PreparePickingAsync 操作。
    /// </summary>
    Task<(bool flag, string msg)> PreparePickingAsync(long stockMoveId, CurrentUser currentUser);
}
