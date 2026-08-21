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
    /// <summary>
    /// 表示 ToShip 枚举值。
    /// </summary>
    ToShip = 1,
    /// <summary>
    /// 表示 PendingArrival 枚举值。
    /// </summary>
    PendingArrival = 2,
    /// <summary>
    /// 表示 Arrived 枚举值。
    /// </summary>
    Arrived = 3
}

/// <summary>
/// Reads ERP shipments waiting for receipt without using WMS ASN tables.
/// </summary>
public interface IErpPendingReceiptService : IDependency
{
    /// <summary>
    /// 定义 PageAsync 操作。
    /// </summary>
    Task<(List<ErpPendingReceiptViewModel> data, int totals)> PageAsync(
        PageSearch pageSearch,
        ErpPendingReceiptListKind kind,
        CurrentUser currentUser);

    /// <summary>
    /// 定义 GetLogisticsAsync 操作。
    /// </summary>
    Task<ErpPendingReceiptLogisticsViewModel?> GetLogisticsAsync(long shipmentId);

    /// <summary>
    /// 定义 ReceiptDetailsPageAsync 操作。
    /// </summary>
    Task<(List<ErpReceiptDetailViewModel> data, int totals)> ReceiptDetailsPageAsync(
        PageSearch pageSearch,
        CurrentUser currentUser);

    /// <summary>
    /// 定义 ConfirmAsync 操作。
    /// </summary>
    Task<(bool flag, string message, long inboundQty)> ConfirmAsync(
        ErpReceiptConfirmInputViewModel input,
        CurrentUser currentUser);
}
