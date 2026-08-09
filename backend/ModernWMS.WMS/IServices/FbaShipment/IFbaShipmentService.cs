using ModernWMS.Core.DI;
using ModernWMS.Core.Models;
using ModernWMS.WMS.Entities.ViewModels;

namespace ModernWMS.WMS.IServices;

/// <summary>
/// Reads ERP-prepared FBA shipments for the Shenzhen self-operated warehouse.
/// </summary>
public interface IFbaShipmentService : IDependency
{
    Task<(List<FbaShipmentViewModel> data, int totals)> PageAsync(PageSearch pageSearch);
}
