using ModernWMS.Core.DI;
using ModernWMS.Core.Models;
using ModernWMS.Core.JWT;
using ModernWMS.WMS.Entities.ViewModels;

namespace ModernWMS.WMS.IServices;

/// <summary>
/// Reads ERP shipments waiting for receipt without using WMS ASN tables.
/// </summary>
public interface IErpPendingReceiptService : IDependency
{
    Task<(List<ErpPendingReceiptViewModel> data, int totals)> PageAsync(
        PageSearch pageSearch,
        bool delivered,
        CurrentUser currentUser);

    Task<ErpPendingReceiptLogisticsViewModel?> GetLogisticsAsync(long shipmentId);

    Task<(List<ErpReceiptDetailViewModel> data, int totals)> ReceiptDetailsPageAsync(
        PageSearch pageSearch,
        CurrentUser currentUser);

    Task<(bool flag, string message, long inboundQty)> ConfirmAsync(
        ErpReceiptConfirmInputViewModel input,
        CurrentUser currentUser);
}
