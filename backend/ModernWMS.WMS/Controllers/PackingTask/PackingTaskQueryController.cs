using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ModernWMS.Core.Controller;
using ModernWMS.Core.Models;
using ModernWMS.WMS.Entities.ViewModels;
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

    public PackingTaskQueryController(IPackingTaskQueryService packingTaskQueryService)
    {
        _packingTaskQueryService = packingTaskQueryService;
    }

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
}
