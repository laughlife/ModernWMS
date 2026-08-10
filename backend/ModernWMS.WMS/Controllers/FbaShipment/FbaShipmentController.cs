using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ModernWMS.Core.Controller;
using ModernWMS.Core.Models;
using ModernWMS.WMS.Entities.ViewModels;
using ModernWMS.WMS.IServices;

namespace ModernWMS.WMS.Controllers;

/// <summary>
/// Shenzhen self-operated warehouse FBA shipment endpoints.
/// </summary>
[Route("fba-shipment")]
[ApiController]
[Authorize]
[ApiExplorerSettings(GroupName = "WMS")]
public class FbaShipmentController : BaseController
{
    private readonly IFbaShipmentService _fbaShipmentService;

    public FbaShipmentController(IFbaShipmentService fbaShipmentService)
    {
        _fbaShipmentService = fbaShipmentService;
    }

    /// <summary>
    /// Lists ERP preparations waiting for FBA shipment from warehouse 320118.
    /// </summary>
    [HttpPost("page")]
    public async Task<ResultModel<PageData<FbaShipmentViewModel>>> PageAsync(PageSearch pageSearch)
    {
        var (data, totals) = await _fbaShipmentService.PageAsync(pageSearch, CurrentUser);
        return ResultModel<PageData<FbaShipmentViewModel>>.Success(new PageData<FbaShipmentViewModel>
        {
            Rows = data,
            Totals = totals
        });
    }

    /// <summary>
    /// Creates the WMS dispatch and stock locks, then moves the FBA shipment into pending picking.
    /// </summary>
    [HttpPost("{stockMoveId:long}/prepare-picking")]
    public async Task<ResultModel<string>> PreparePickingAsync(long stockMoveId)
    {
        var (flag, msg) = await _fbaShipmentService.PreparePickingAsync(stockMoveId, CurrentUser);
        return flag ? ResultModel<string>.Success(msg) : ResultModel<string>.Error(msg);
    }
}
