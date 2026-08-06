using ModernWMS.Core.DI;
using ModernWMS.WMS.Entities.ViewModels;

namespace ModernWMS.WMS.IServices;

/// <summary>
/// Interface of OperatorGroupService
/// </summary>
public interface IOperatorGroupService : IDependency
{
    /// <summary>
    /// Get all operator group details from ERP.
    /// </summary>
    /// <returns>operator group list</returns>
    Task<List<OperatorGroupViewModel>> GetAllAsync();
}
