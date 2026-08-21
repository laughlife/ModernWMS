using ModernWMS.Core.DI;
using ModernWMS.Core.JWT;
using ModernWMS.WMS.Entities.ViewModels;

namespace ModernWMS.WMS.IServices;

/// <summary>
/// Authoritative backend boundary for ERP warehouse visibility and direct-request validation.
/// </summary>
public interface IWarehouseAccessService : IDependency
{
    /// <summary>
    /// 定义 GetAllowedAsync 操作。
    /// </summary>
    Task<WarehouseAccessViewModel> GetAllowedAsync(CurrentUser currentUser);

    /// <summary>
    /// 定义 EnsureAllowedAsync 操作。
    /// </summary>
    Task EnsureAllowedAsync(long warehouseId, CurrentUser currentUser);
}
