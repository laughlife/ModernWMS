using ModernWMS.Core.DI;
using ModernWMS.Core.JWT;
using ModernWMS.WMS.Entities.ViewModels.DispatchWorkflow;

namespace ModernWMS.WMS.IServices.DispatchWorkflow;

/// <summary>提供出库单拣货、装箱、称重及出库流程操作。</summary>
public interface IDispatchWorkflowService : IDependency
{
    /// <summary>校验拣货来源是否仍为最新。</summary>
    Task<PostPickSourceGuardResult> EnsurePostPickSourceCurrentAsync(
        int dispatchOrderId,
        CurrentUser currentUser,
        CancellationToken cancellationToken = default);

    /// <summary>处理拣货来源变更决策。</summary>
    Task<SourceDecisionResult> DecideSourceChangeAsync(
        int dispatchOrderId,
        SourceDecisionRequest request,
        CurrentUser currentUser,
        CancellationToken cancellationToken = default);

    /// <summary>完成拣货。</summary>
    Task<CompletePickingResult> CompletePickingAsync(
        int orderId,
        CompletePickingRequest request,
        CurrentUser currentUser,
        CancellationToken cancellationToken = default);

    /// <summary>回滚待处理拣货。</summary>
    Task<RollbackPendingPickResult> RollbackPendingPickAsync(
        int orderId,
        RollbackPendingPickRequest request,
        CurrentUser currentUser,
        CancellationToken cancellationToken = default);

    /// <summary>开始称重。</summary>
    Task<WeighingCommandResult> StartWeighingAsync(
        int orderId,
        WeighingOrderCommandRequest request,
        CurrentUser currentUser,
        CancellationToken cancellationToken = default);

    /// <summary>获取拣货任务箱列表。</summary>
    Task<List<WeighingBoxViewModel>> GetTaskBoxesAsync(
        int orderId,
        int packingTaskId,
        CurrentUser currentUser,
        CancellationToken cancellationToken = default);

    /// <summary>获取装箱计划。</summary>
    Task<PackingPlanViewModel> GetPackingPlanAsync(int orderId, int packingTaskId,
        CurrentUser currentUser, CancellationToken cancellationToken = default);

    /// <summary>保存装箱计划。</summary>
    Task<PackingPlanViewModel> SavePackingPlanAsync(int orderId, int packingTaskId,
        SavePackingPlanRequest request, CurrentUser currentUser, CancellationToken cancellationToken = default);

    /// <summary>确认装箱。</summary>
    Task<PackingPlanViewModel> ConfirmPackingAsync(int orderId, int packingTaskId,
        ConfirmActualPackingRequest request, CurrentUser currentUser, CancellationToken cancellationToken = default);

    /// <summary>确认实际装箱。</summary>
    Task<PackingPlanViewModel> ConfirmActualPackingAsync(int orderId, int packingTaskId,
        ConfirmActualPackingRequest request, CurrentUser currentUser, CancellationToken cancellationToken = default);

    /// <summary>重试已提交但尚未完成的 ERP 装箱库存消费。</summary>
    Task<PackingPlanViewModel> RetryPackingConsumeAsync(int orderId, int packingTaskId,
        CurrentUser currentUser, CancellationToken cancellationToken = default);

    /// <summary>保存称重箱信息。</summary>
    Task<WeighingCommandResult> SaveWeighingBoxAsync(
        int orderId,
        int boxId,
        SaveWeighingBoxRequest request,
        CurrentUser currentUser,
        CancellationToken cancellationToken = default);

    /// <summary>复制称重箱。</summary>
    Task<WeighingCommandResult> CopyWeighingBoxAsync(
        int orderId,
        int targetBoxId,
        CopyWeighingBoxRequest request,
        CurrentUser currentUser,
        CancellationToken cancellationToken = default);

    /// <summary>完成任务称重。</summary>
    Task<WeighingCommandResult> CompleteTaskWeighingAsync(
        int orderId,
        int packingTaskId,
        WeighingOrderCommandRequest request,
        CurrentUser currentUser,
        CancellationToken cancellationToken = default);

    /// <summary>完成订单称重。</summary>
    Task<WeighingCommandResult> CompleteOrderWeighingAsync(
        int orderId,
        WeighingOrderCommandRequest request,
        CurrentUser currentUser,
        CancellationToken cancellationToken = default);

    /// <summary>确认出库。</summary>
    Task<OutboundCommandResult> ConfirmOutboundAsync(
        int orderId,
        OutboundCommandRequest request,
        CurrentUser currentUser,
        CancellationToken cancellationToken = default);

    /// <summary>获取承运商选项。</summary>
    Task<List<DispatchCarrierOptionViewModel>> GetCarrierOptionsAsync(
        CurrentUser currentUser,
        CancellationToken cancellationToken = default);

    /// <summary>设置承运商。</summary>
    Task<SetDispatchCarrierResult> SetCarrierAsync(
        SetDispatchCarrierRequest request,
        CurrentUser currentUser,
        CancellationToken cancellationToken = default);

    /// <summary>取消出库。</summary>
    Task<OutboundCommandResult> CancelOutboundAsync(
        int orderId,
        OutboundCommandRequest request,
        CurrentUser currentUser,
        CancellationToken cancellationToken = default);

    /// <summary>签收出库单。</summary>
    Task<SignDispatchOrderResult> SignAsync(
        int orderId,
        SignDispatchOrderRequest request,
        CurrentUser currentUser,
        CancellationToken cancellationToken = default);

    /// <summary>创建出库单。</summary>
    Task<DispatchOrderDetailViewModel> CreateAsync(
        CreateDispatchOrderRequest request,
        CurrentUser currentUser,
        CancellationToken cancellationToken = default);

    /// <summary>对账出库单。</summary>
    Task<DispatchOrderDetailViewModel> ReconcileAsync(
        int orderId,
        CurrentUser currentUser,
        CancellationToken cancellationToken = default);

    /// <summary>获取出库单打印数据。</summary>
    Task<DispatchOrderDetailViewModel> PrintAsync(
        int orderId,
        CurrentUser currentUser,
        CancellationToken cancellationToken = default);
}
