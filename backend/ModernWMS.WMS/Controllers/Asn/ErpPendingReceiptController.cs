using Microsoft.AspNetCore.Authorization;
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
[Authorize]
[ApiExplorerSettings(GroupName = "WMS")]
public class ErpPendingReceiptController : BaseController
{
    private readonly IErpPendingReceiptService _erpPendingReceiptService;

    /// <summary>
    /// 初始化 ErpPendingReceiptController 的新实例。
    /// </summary>
    public ErpPendingReceiptController(IErpPendingReceiptService erpPendingReceiptService)
    {
        _erpPendingReceiptService = erpPendingReceiptService;
    }

    /// <summary>
    /// Lists ERP shipments that have not been shipped yet.
    /// </summary>
    [HttpPost("to-ship-list")]
    public async Task<ResultModel<PageData<ErpPendingReceiptViewModel>>> ToShipPageAsync(PageSearch pageSearch)
    {
        return await BuildPageAsync(pageSearch, ErpPendingReceiptListKind.ToShip);
    }

    /// <summary>
    /// Lists ERP shipments waiting for receipt (shipped but not signed).
    /// </summary>
    [HttpPost("list")]
    public async Task<ResultModel<PageData<ErpPendingReceiptViewModel>>> PageAsync(PageSearch pageSearch)
    {
        return await BuildPageAsync(pageSearch, ErpPendingReceiptListKind.PendingArrival);
    }

    /// <summary>
    /// Lists ERP shipments whose latest effective logistics status is delivered.
    /// </summary>
    [HttpPost("arrived-list")]
    public async Task<ResultModel<PageData<ErpPendingReceiptViewModel>>> ArrivedPageAsync(PageSearch pageSearch)
    {
        return await BuildPageAsync(pageSearch, ErpPendingReceiptListKind.Arrived);
    }

    /// <summary>
    /// Lists product-level receipt results posted directly into their bound warehouse areas.
    /// </summary>
    [HttpPost("receipt-detail-list")]
    public async Task<ResultModel<PageData<ErpReceiptDetailViewModel>>> ReceiptDetailsPageAsync(PageSearch pageSearch)
    {
        var (data, totals) = await _erpPendingReceiptService.ReceiptDetailsPageAsync(pageSearch, CurrentUser);
        return ResultModel<PageData<ErpReceiptDetailViewModel>>.Success(new PageData<ErpReceiptDetailViewModel>
        {
            Rows = data,
            Totals = totals
        });
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

    /// <summary>
    /// Confirms a receipt and saves its server-calculated inbound quantity.
    /// </summary>
    [HttpPost("confirm")]
    public async Task<ResultModel<long>> ConfirmAsync(ErpReceiptConfirmInputViewModel input)
    {
        var (flag, message, inboundQty) = await _erpPendingReceiptService.ConfirmAsync(input, CurrentUser);
        return flag
            ? ResultModel<long>.Success(inboundQty, message)
            : ResultModel<long>.Error(message);
    }

    private async Task<ResultModel<PageData<ErpPendingReceiptViewModel>>> BuildPageAsync(
        PageSearch pageSearch,
        ErpPendingReceiptListKind kind)
    {
        var (data, totals) = await _erpPendingReceiptService.PageAsync(pageSearch, kind, CurrentUser);
        return ResultModel<PageData<ErpPendingReceiptViewModel>>.Success(new PageData<ErpPendingReceiptViewModel>
        {
            Rows = data,
            Totals = totals
        });
    }
}
