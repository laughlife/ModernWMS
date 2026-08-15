using ModernWMS.Core.DI;
using ModernWMS.Core.JWT;
using ModernWMS.WMS.Entities.ViewModels.DispatchWorkflow;

namespace ModernWMS.WMS.IServices.DispatchWorkflow;

public interface IDispatchOrderQueryService : IDependency
{
    Task<DispatchOrderPageResult> PageAsync(
        DispatchOrderPageRequest request,
        CurrentUser currentUser,
        CancellationToken cancellationToken = default);

    Task<DispatchOrderStatusCounts> CountsAsync(
        long warehouseId,
        CurrentUser currentUser,
        CancellationToken cancellationToken = default);

    Task<DispatchOrderDetailViewModel> GetAsync(
        int orderId,
        CurrentUser currentUser,
        CancellationToken cancellationToken = default);
}
