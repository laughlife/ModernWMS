using ModernWMS.Core.DI;
using ModernWMS.Core.JWT;
using ModernWMS.Core.Models;
using ModernWMS.WMS.Entities.ViewModels;
using ModernWMS.WMS.Entities.ViewModels.PackingTask;

namespace ModernWMS.WMS.IServices;

/// <summary>
/// Read-only query boundary for formal SellFox packing tasks.
/// </summary>
public interface IPackingTaskQueryService : IDependency
{
    Task<PackingTaskQueryResult> PageAsync(PageSearch pageSearch, CurrentUser currentUser);

    Task<(List<SelectableStockViewModel> data, int totals)> SelectableStockPageAsync(
        PackingTaskStockPageRequest request,
        CurrentUser currentUser);

    Task<(bool flag, string message)> SelectStockAsync(
        PackingTaskStockSelectRequest request,
        CurrentUser currentUser);

    Task<(bool flag, string message)> DeleteStockSelectionAsync(
        PackingTaskStockSelectRequest request,
        CurrentUser currentUser);
}

public record PackingTaskQueryResult(
    bool IsSuccess,
    string ErrorMessage,
    List<PackingTaskQueryViewModel> Data,
    int Totals);
