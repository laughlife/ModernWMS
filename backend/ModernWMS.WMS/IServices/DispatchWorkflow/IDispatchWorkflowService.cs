using ModernWMS.Core.DI;
using ModernWMS.Core.JWT;
using ModernWMS.WMS.Entities.ViewModels.DispatchWorkflow;

namespace ModernWMS.WMS.IServices.DispatchWorkflow;

public interface IDispatchWorkflowService : IDependency
{
    Task<DispatchOrderDetailViewModel> CreateAsync(
        CreateDispatchOrderRequest request,
        CurrentUser currentUser,
        CancellationToken cancellationToken = default);

    Task<DispatchOrderDetailViewModel> ReconcileAsync(
        int orderId,
        CurrentUser currentUser,
        CancellationToken cancellationToken = default);

    Task<DispatchOrderDetailViewModel> PrintAsync(
        int orderId,
        CurrentUser currentUser,
        CancellationToken cancellationToken = default);
}
