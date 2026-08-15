using ModernWMS.Core.DI;
using ModernWMS.Core.JWT;
using ModernWMS.Core.Models;
using ModernWMS.WMS.Entities.ViewModels;

namespace ModernWMS.WMS.IServices;

/// <summary>
/// Read-only query boundary for formal SellFox packing tasks.
/// </summary>
public interface IPackingTaskQueryService : IDependency
{
    Task<PackingTaskQueryResult> PageAsync(PageSearch pageSearch, CurrentUser currentUser);
}

public record PackingTaskQueryResult(
    bool IsSuccess,
    string ErrorMessage,
    List<PackingTaskQueryViewModel> Data,
    int Totals);
