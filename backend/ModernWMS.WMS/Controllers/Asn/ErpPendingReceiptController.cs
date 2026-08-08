using Microsoft.AspNetCore.Mvc;
using ModernWMS.Core.Controller;
using ModernWMS.Core.Models;
using ModernWMS.WMS.Entities.ViewModels;
using ModernWMS.WMS.IServices;

namespace ModernWMS.WMS.Controllers;

/// <summary>
/// ERP-backed pending receipt endpoints.
/// </summary>
[Route("asn/erp-pending-receipt")]
[ApiController]
[ApiExplorerSettings(GroupName = "WMS")]
public class ErpPendingReceiptController : BaseController
{
    private readonly IErpPendingReceiptService _erpPendingReceiptService;

    public ErpPendingReceiptController(IErpPendingReceiptService erpPendingReceiptService)
    {
        _erpPendingReceiptService = erpPendingReceiptService;
    }

    /// <summary>
    /// Lists ERP shipments waiting for receipt at the Shenzhen warehouse.
    /// </summary>
    [HttpPost("list")]
    public async Task<ResultModel<PageData<ErpPendingReceiptViewModel>>> PageAsync(PageSearch pageSearch)
    {
        var (data, totals) = await _erpPendingReceiptService.PageAsync(pageSearch);
        return ResultModel<PageData<ErpPendingReceiptViewModel>>.Success(new PageData<ErpPendingReceiptViewModel>
        {
            Rows = data,
            Totals = totals
        });
    }
}
