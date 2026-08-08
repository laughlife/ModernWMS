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
        return await BuildPageAsync(pageSearch, false);
    }

    /// <summary>
    /// Lists ERP shipments whose latest effective logistics status is delivered.
    /// </summary>
    [HttpPost("arrived-list")]
    public async Task<ResultModel<PageData<ErpPendingReceiptViewModel>>> ArrivedPageAsync(PageSearch pageSearch)
    {
        return await BuildPageAsync(pageSearch, true);
    }

    /// <summary>
    /// Gets the latest ERP logistics snapshot and its event timeline.
    /// </summary>
    [HttpGet("logistics")]
    public async Task<ResultModel<ErpPendingReceiptLogisticsViewModel>> GetLogisticsAsync(long shipmentId)
    {
        var data = await _erpPendingReceiptService.GetLogisticsAsync(shipmentId);
        return data == null
            ? ResultModel<ErpPendingReceiptLogisticsViewModel>.Error("未找到对应的物流信息")
            : ResultModel<ErpPendingReceiptLogisticsViewModel>.Success(data);
    }

    private async Task<ResultModel<PageData<ErpPendingReceiptViewModel>>> BuildPageAsync(
        PageSearch pageSearch,
        bool delivered)
    {
        var (data, totals) = await _erpPendingReceiptService.PageAsync(pageSearch, delivered);
        return ResultModel<PageData<ErpPendingReceiptViewModel>>.Success(new PageData<ErpPendingReceiptViewModel>
        {
            Rows = data,
            Totals = totals
        });
    }
}
