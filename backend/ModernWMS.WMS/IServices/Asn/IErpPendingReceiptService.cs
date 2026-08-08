using ModernWMS.Core.DI;
using ModernWMS.Core.Models;
using ModernWMS.WMS.Entities.ViewModels;

namespace ModernWMS.WMS.IServices;

/// <summary>
/// Reads ERP shipments waiting for receipt without using WMS ASN tables.
/// </summary>
public interface IErpPendingReceiptService : IDependency
{
    Task<(List<ErpPendingReceiptViewModel> data, int totals)> PageAsync(PageSearch pageSearch, bool delivered);

    Task<ErpPendingReceiptLogisticsViewModel?> GetLogisticsAsync(long shipmentId);
}
