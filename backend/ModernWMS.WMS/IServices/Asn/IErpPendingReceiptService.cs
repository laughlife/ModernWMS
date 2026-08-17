using ModernWMS.Core.DI;
using ModernWMS.Core.Models;
using ModernWMS.Core.JWT;
using ModernWMS.WMS.Entities.ViewModels;

namespace ModernWMS.WMS.IServices;

/// <summary>
/// 收货列表页签口径：待发货（未发货）、待到货（已发货未签收）、到货通知（已签收）。
/// </summary>
public enum ErpPendingReceiptListKind
{
    ToShip = 1,
    PendingArrival = 2,
    Arrived = 3
}

/// <summary>
/// Reads ERP shipments waiting for receipt without using WMS ASN tables.
/// </summary>
public interface IErpPendingReceiptService : IDependency
{
    Task<(List<ErpPendingReceiptViewModel> data, int totals)> PageAsync(
        PageSearch pageSearch,
        ErpPendingReceiptListKind kind,
        CurrentUser currentUser);

    Task<ErpPendingReceiptLogisticsViewModel?> GetLogisticsAsync(long shipmentId);

    Task<(List<ErpReceiptDetailViewModel> data, int totals)> ReceiptDetailsPageAsync(
        PageSearch pageSearch,
        CurrentUser currentUser);

    Task<(bool flag, string message, long inboundQty)> ConfirmAsync(
        ErpReceiptConfirmInputViewModel input,
        CurrentUser currentUser);
}
