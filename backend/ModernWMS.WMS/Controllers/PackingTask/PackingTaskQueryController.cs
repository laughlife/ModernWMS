using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ModernWMS.Core.Controller;
using ModernWMS.Core.Models;
using ModernWMS.WMS.Entities.ViewModels;
using ModernWMS.WMS.Entities.ViewModels.PackingTask;
using ModernWMS.WMS.IServices;

namespace ModernWMS.WMS.Controllers;

/// <summary>
/// Read-only formal packing-task endpoints.
/// </summary>
[Route("packing-task-query")]
[ApiController]
[Authorize]
[ApiExplorerSettings(GroupName = "WMS")]
public class PackingTaskQueryController : BaseController
{
    private readonly IPackingTaskQueryService _packingTaskQueryService;

    /// <summary>
    /// 初始化 PackingTaskQueryController 的新实例。
    /// </summary>
    public PackingTaskQueryController(IPackingTaskQueryService packingTaskQueryService)
    {
        _packingTaskQueryService = packingTaskQueryService;
    }

    /// <summary>
    /// 执行 PageAsync 操作。
    /// </summary>
    [HttpPost("page")]
    public async Task<ResultModel<PageData<PackingTaskQueryViewModel>>> PageAsync(PageSearch pageSearch)
    {
        var result = await _packingTaskQueryService.PageAsync(pageSearch, CurrentUser);
        if (!result.IsSuccess)
        {
            return ResultModel<PageData<PackingTaskQueryViewModel>>.Error(result.ErrorMessage);
        }

        return ResultModel<PageData<PackingTaskQueryViewModel>>.Success(new PageData<PackingTaskQueryViewModel>
        {
            Rows = result.Data,
            Totals = result.Totals
        });
    }

    /// <summary>
    /// 查询装箱任务明细行可选择的库存列表（分页，查看更多=下一页）。
    /// </summary>
    [HttpPost("selectable-stock")]
    public async Task<ResultModel<PageData<SelectableStockViewModel>>> SelectableStockPageAsync(
        PackingTaskStockPageRequest request)
    {
        var result = await _packingTaskQueryService.SelectableStockPageAsync(request, CurrentUser);
        if (!result.IsSuccess)
        {
            return ResultModel<PageData<SelectableStockViewModel>>.Error(result.ErrorMessage);
        }
        return ResultModel<PageData<SelectableStockViewModel>>.Success(new PageData<SelectableStockViewModel>
        {
            Rows = result.Data,
            Totals = result.Totals
        });
    }

    /// <summary>
    /// 保存装箱任务明细行对某个库存行的选择。
    /// </summary>
    [HttpPost("select-stock")]
    public async Task<ResultModel<bool>> SelectStockAsync(PackingTaskStockSelectRequest request)
    {
        var (flag, message) = await _packingTaskQueryService.SelectStockAsync(request, CurrentUser);
        return flag
            ? ResultModel<bool>.Success(true, message)
            : ResultModel<bool>.Error(message);
    }

    /// <summary>签发 SKU 不匹配确认挑战；挑战从服务端计时且只能使用一次。</summary>
    [HttpPost("sku-mismatch-challenge")]
    public async Task<ResultModel<string>> BeginSkuMismatchChallengeAsync(
        PackingTaskSkuMismatchChallengeRequest request)
    {
        try
        {
            var challenge = await _packingTaskQueryService.BeginSkuMismatchChallengeAsync(request, CurrentUser);
            return ResultModel<string>.Success(challenge);
        }
        catch (ArgumentException exception)
        {
            return ResultModel<string>.Error(exception.Message);
        }
    }

    /// <summary>
    /// 取消装箱任务明细行对某个库存行的选择，释放锁定的库存。
    /// </summary>
    [HttpPost("delete-selection")]
    public async Task<ResultModel<bool>> DeleteStockSelectionAsync(PackingTaskStockSelectRequest request)
    {
        var (flag, message) = await _packingTaskQueryService.DeleteStockSelectionAsync(request, CurrentUser);
        return flag
            ? ResultModel<bool>.Success(true, message)
            : ResultModel<bool>.Error(message);
    }
}
