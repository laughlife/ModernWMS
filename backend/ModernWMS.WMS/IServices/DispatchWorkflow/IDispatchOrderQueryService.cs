using ModernWMS.Core.DI;
using ModernWMS.Core.JWT;
using ModernWMS.WMS.Entities.ViewModels.DispatchWorkflow;

namespace ModernWMS.WMS.IServices.DispatchWorkflow;

/// <summary>提供出库单查询功能。</summary>
public interface IDispatchOrderQueryService : IDependency
{
    /// <summary>分页查询出库单。</summary>
    Task<DispatchOrderPageResult> PageAsync(
        DispatchOrderPageRequest request,
        CurrentUser currentUser,
        CancellationToken cancellationToken = default);

    /// <summary>统计出库单状态数量。</summary>
    Task<DispatchOrderStatusCounts> CountsAsync(
        long warehouseId,
        CurrentUser currentUser,
        CancellationToken cancellationToken = default);

    /// <summary>获取出库单详情。</summary>
    Task<DispatchOrderDetailViewModel> GetAsync(
        int orderId,
        CurrentUser currentUser,
        CancellationToken cancellationToken = default);
}
