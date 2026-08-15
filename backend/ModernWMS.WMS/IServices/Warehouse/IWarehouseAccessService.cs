using ModernWMS.Core.DI;
using ModernWMS.Core.JWT;
using ModernWMS.WMS.Entities.ViewModels;

namespace ModernWMS.WMS.IServices;

/// <summary>
/// Authoritative backend boundary for ERP warehouse visibility and direct-request validation.
/// </summary>
public interface IWarehouseAccessService : IDependency
{
    Task<WarehouseAccessViewModel> GetAllowedAsync(CurrentUser currentUser);

    Task EnsureAllowedAsync(long warehouseId, CurrentUser currentUser);
}
