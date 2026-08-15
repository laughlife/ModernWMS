using ModernWMS.Core.DI;
using ModernWMS.Core.JWT;
using ModernWMS.WMS.Entities.ViewModels.DispatchWorkflow;

namespace ModernWMS.WMS.IServices.DispatchWorkflow;

public interface IDispatchWorkflowService : IDependency
{
    Task<PostPickSourceGuardResult> EnsurePostPickSourceCurrentAsync(
        int dispatchOrderId,
        CurrentUser currentUser,
        CancellationToken cancellationToken = default);

    Task<SourceDecisionResult> DecideSourceChangeAsync(
        int dispatchOrderId,
        SourceDecisionRequest request,
        CurrentUser currentUser,
        CancellationToken cancellationToken = default);

    Task<CompletePickingResult> CompletePickingAsync(
        int orderId,
        CompletePickingRequest request,
        CurrentUser currentUser,
        CancellationToken cancellationToken = default);

    Task<WeighingCommandResult> StartWeighingAsync(
        int orderId,
        WeighingOrderCommandRequest request,
        CurrentUser currentUser,
        CancellationToken cancellationToken = default);

    Task<List<WeighingBoxViewModel>> GetTaskBoxesAsync(
        int orderId,
        int packingTaskId,
        CurrentUser currentUser,
        CancellationToken cancellationToken = default);

    Task<WeighingCommandResult> SaveWeighingBoxAsync(
        int orderId,
        int boxId,
        SaveWeighingBoxRequest request,
        CurrentUser currentUser,
        CancellationToken cancellationToken = default);

    Task<WeighingCommandResult> CopyWeighingBoxAsync(
        int orderId,
        int targetBoxId,
        CopyWeighingBoxRequest request,
        CurrentUser currentUser,
        CancellationToken cancellationToken = default);

    Task<WeighingCommandResult> CompleteTaskWeighingAsync(
        int orderId,
        int packingTaskId,
        WeighingOrderCommandRequest request,
        CurrentUser currentUser,
        CancellationToken cancellationToken = default);

    Task<WeighingCommandResult> CompleteOrderWeighingAsync(
        int orderId,
        WeighingOrderCommandRequest request,
        CurrentUser currentUser,
        CancellationToken cancellationToken = default);

    Task<OutboundCommandResult> ConfirmOutboundAsync(
        int orderId,
        OutboundCommandRequest request,
        CurrentUser currentUser,
        CancellationToken cancellationToken = default);

    Task<OutboundCommandResult> CancelOutboundAsync(
        int orderId,
        OutboundCommandRequest request,
        CurrentUser currentUser,
        CancellationToken cancellationToken = default);

    Task<SignDispatchOrderResult> SignAsync(
        int orderId,
        SignDispatchOrderRequest request,
        CurrentUser currentUser,
        CancellationToken cancellationToken = default);

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
