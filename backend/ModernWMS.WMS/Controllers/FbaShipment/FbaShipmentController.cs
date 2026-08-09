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
        var (data, totals) = await _fbaShipmentService.PageAsync(pageSearch);
        return ResultModel<PageData<FbaShipmentViewModel>>.Success(new PageData<FbaShipmentViewModel>
        {
            Rows = data,
            Totals = totals
        });
    }
}
