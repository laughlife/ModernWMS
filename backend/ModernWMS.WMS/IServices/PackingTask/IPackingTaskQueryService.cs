using ModernWMS.Core.DI;
using ModernWMS.Core.JWT;
using ModernWMS.Core.Models;
using ModernWMS.WMS.Entities.ViewModels;
using ModernWMS.WMS.Entities.ViewModels.PackingTask;
using ModernWMS.WMS.Services;

namespace ModernWMS.WMS.IServices;

/// <summary>
/// Read-only query boundary for formal SellFox packing tasks.
/// </summary>
public interface IPackingTaskQueryService : IDependency
{
    /// <summary>
    /// 定义 PageAsync 操作。
    /// </summary>
    Task<PackingTaskQueryResult> PageAsync(PageSearch pageSearch, CurrentUser currentUser);

    /// <summary>
    /// 定义 SelectableStockPageAsync 操作。
    /// </summary>
    Task<PackingTaskSelectableResult> SelectableStockPageAsync(
        PackingTaskStockPageRequest request,
        CurrentUser currentUser);

    /// <summary>
    /// 定义 SelectStockAsync 操作。
    /// </summary>
    Task<(bool flag, string message)> SelectStockAsync(
        PackingTaskStockSelectRequest request,
        CurrentUser currentUser);

    /// <summary>
    /// 定义 DeleteStockSelectionAsync 操作。
    /// </summary>
    Task<(bool flag, string message)> DeleteStockSelectionAsync(
        PackingTaskStockSelectRequest request,
        CurrentUser currentUser);

    Task<string> BeginSkuMismatchChallengeAsync(PackingTaskSkuMismatchChallengeRequest request, CurrentUser currentUser);
}

/// <summary>装箱任务分页查询结果。</summary>
public record PackingTaskQueryResult(
    bool IsSuccess,
    string ErrorMessage,
    List<PackingTaskQueryViewModel> Data,
    int Totals);
