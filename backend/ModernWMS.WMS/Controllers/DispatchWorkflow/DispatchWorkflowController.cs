using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ModernWMS.Core.Controller;
using ModernWMS.Core.Models;
using ModernWMS.WMS.Entities.ViewModels.DispatchWorkflow;
using ModernWMS.WMS.IServices.DispatchWorkflow;
using ModernWMS.WMS.Services.DispatchWorkflow;
using Microsoft.AspNetCore.Http;

namespace ModernWMS.WMS.Controllers.DispatchWorkflow;

/// <summary>
/// 表示 DispatchWorkflowController 类型。
/// </summary>
[Route("dispatch-workflow")]
[ApiController]
[Authorize]
[ApiExplorerSettings(GroupName = "WMS")]
/// <summary>提供出库单拣货、装箱、称重及出库流程接口。</summary>
public sealed class DispatchWorkflowController : BaseController
{
    private readonly IDispatchWorkflowService _workflowService;
    private readonly IDispatchOrderQueryService _queryService;

    /// <summary>
    /// 初始化 DispatchWorkflowController 的新实例。
    /// </summary>
    public DispatchWorkflowController(
        IDispatchWorkflowService workflowService,
        IDispatchOrderQueryService queryService)
    {
        _workflowService = workflowService;
        _queryService = queryService;
    }

    /// <summary>创建出库单。</summary>
    [HttpPost]
    public Task<ActionResult<ResultModel<DispatchOrderDetailViewModel>>> CreateAsync(
        CreateDispatchOrderRequest request,
        CancellationToken cancellationToken) =>
        ExecuteAsync(() => _workflowService.CreateAsync(request, CurrentUser, cancellationToken));

    /// <summary>分页查询出库单。</summary>
    [HttpPost("page")]
    public Task<ActionResult<ResultModel<PageData<DispatchOrderSummaryViewModel>>>> PageAsync(
        DispatchOrderPageRequest request,
        CancellationToken cancellationToken) => ExecuteAsync(async () =>
        {
            var result = await _queryService.PageAsync(request, CurrentUser, cancellationToken);
            return new PageData<DispatchOrderSummaryViewModel>
            {
                Rows = result.Data,
                Totals = result.Totals
            };
        });

    /// <summary>统计出库单状态数量。</summary>
    [HttpGet("counts")]
    public Task<ActionResult<ResultModel<IReadOnlyDictionary<string, int>>>> CountsAsync(
        [FromQuery] long warehouse_id,
        CancellationToken cancellationToken) =>
        ExecuteAsync(async () =>
            (await _queryService.CountsAsync(warehouse_id, CurrentUser, cancellationToken)).Counts);

    /// <summary>获取出库单详情。</summary>
    [HttpGet("{id:int}")]
    public Task<ActionResult<ResultModel<DispatchOrderDetailViewModel>>> GetAsync(
        int id,
        CancellationToken cancellationToken) =>
        ExecuteAsync(() => _queryService.GetAsync(id, CurrentUser, cancellationToken));

    /// <summary>对账出库单。</summary>
    [HttpPost("{id:int}/reconcile")]
    public Task<ActionResult<ResultModel<DispatchOrderDetailViewModel>>> ReconcileAsync(
        int id,
        CancellationToken cancellationToken) =>
        ExecuteAsync(() => _workflowService.ReconcileAsync(id, CurrentUser, cancellationToken));

    /// <summary>完成拣货。</summary>
    [Authorize]
    [HttpPost("{id:int}/complete-picking")]
    public Task<ActionResult<ResultModel<CompletePickingResult>>> CompletePickingAsync(
        int id,
        CompletePickingRequest request,
        CancellationToken cancellationToken) =>
        ExecuteAsync(() => _workflowService.CompletePickingAsync(id, request, CurrentUser, cancellationToken));

    /// <summary>回滚待处理拣货。</summary>
    [Authorize]
    [HttpPost("{id:int}/rollback-pending-pick")]
    public Task<ActionResult<ResultModel<RollbackPendingPickResult>>> RollbackPendingPickAsync(
        int id,
        RollbackPendingPickRequest request,
        CancellationToken cancellationToken) =>
        ExecuteAsync(() => _workflowService.RollbackPendingPickAsync(id, request, CurrentUser, cancellationToken));

    /// <summary>开始称重。</summary>
    [Authorize]
    [HttpPost("{id:int}/start-weighing")]
    public Task<ActionResult<ResultModel<WeighingCommandResult>>> StartWeighingAsync(
        int id,
        WeighingOrderCommandRequest request,
        CancellationToken cancellationToken) =>
        ExecuteAsync(() => _workflowService.StartWeighingAsync(id, request, CurrentUser, cancellationToken));

    /// <summary>获取拣货任务箱列表。</summary>
    [HttpGet("{id:int}/packing-tasks/{packingTaskId:int}/boxes")]
    public Task<ActionResult<ResultModel<List<WeighingBoxViewModel>>>> GetTaskBoxesAsync(
        int id,
        int packingTaskId,
        CancellationToken cancellationToken) =>
        ExecuteAsync(() => _workflowService.GetTaskBoxesAsync(
            id, packingTaskId, CurrentUser, cancellationToken));

    /// <summary>获取装箱计划。</summary>
    [HttpGet("{id:int}/packing-tasks/{packingTaskId:int}/packing-plan")]
    public Task<ActionResult<ResultModel<PackingPlanViewModel>>> GetPackingPlanAsync(
        int id, int packingTaskId, CancellationToken cancellationToken) =>
        ExecuteAsync(() => _workflowService.GetPackingPlanAsync(id, packingTaskId, CurrentUser, cancellationToken));

    /// <summary>保存装箱计划。</summary>
    [Authorize]
    [HttpPut("{id:int}/packing-tasks/{packingTaskId:int}/packing-plan")]
    public Task<ActionResult<ResultModel<PackingPlanViewModel>>> SavePackingPlanAsync(
        int id, int packingTaskId, SavePackingPlanRequest request, CancellationToken cancellationToken) =>
        ExecuteAsync(() => _workflowService.SavePackingPlanAsync(id, packingTaskId, request, CurrentUser, cancellationToken));

    /// <summary>确认装箱。</summary>
    [Authorize]
    [HttpPost("{id:int}/packing-tasks/{packingTaskId:int}/confirm-packing")]
    public Task<ActionResult<ResultModel<PackingPlanViewModel>>> ConfirmPackingAsync(
        int id, int packingTaskId, ConfirmActualPackingRequest request, CancellationToken cancellationToken) =>
        ExecuteAsync(() => _workflowService.ConfirmPackingAsync(id, packingTaskId, request, CurrentUser, cancellationToken));

    /// <summary>确认实际装箱。</summary>
    [Authorize]
    [HttpPost("{id:int}/packing-tasks/{packingTaskId:int}/confirm-actual")]
    public Task<ActionResult<ResultModel<PackingPlanViewModel>>> ConfirmActualPackingAsync(
        int id, int packingTaskId, ConfirmActualPackingRequest request, CancellationToken cancellationToken) =>
        ExecuteAsync(() => _workflowService.ConfirmActualPackingAsync(id, packingTaskId, request, CurrentUser, cancellationToken));

    /// <summary>保存称重箱信息。</summary>
    [Authorize]
    [HttpPut("{id:int}/boxes/{boxId:int}")]
    public Task<ActionResult<ResultModel<WeighingCommandResult>>> SaveWeighingBoxAsync(
        int id,
        int boxId,
        SaveWeighingBoxRequest request,
        CancellationToken cancellationToken) =>
        ExecuteAsync(() => _workflowService.SaveWeighingBoxAsync(
            id, boxId, request, CurrentUser, cancellationToken));

    /// <summary>复制称重箱。</summary>
    [Authorize]
    [HttpPost("{id:int}/boxes/{targetBoxId:int}/copy")]
    public Task<ActionResult<ResultModel<WeighingCommandResult>>> CopyWeighingBoxAsync(
        int id,
        int targetBoxId,
        CopyWeighingBoxRequest request,
        CancellationToken cancellationToken) =>
        ExecuteAsync(() => _workflowService.CopyWeighingBoxAsync(
            id, targetBoxId, request, CurrentUser, cancellationToken));

    /// <summary>完成任务称重。</summary>
    [Authorize]
    [HttpPost("{id:int}/packing-tasks/{packingTaskId:int}/complete-weighing")]
    public Task<ActionResult<ResultModel<WeighingCommandResult>>> CompleteTaskWeighingAsync(
        int id,
        int packingTaskId,
        WeighingOrderCommandRequest request,
        CancellationToken cancellationToken) =>
        ExecuteAsync(() => _workflowService.CompleteTaskWeighingAsync(
            id, packingTaskId, request, CurrentUser, cancellationToken));

    /// <summary>完成订单称重。</summary>
    [Authorize]
    [HttpPost("{id:int}/complete-weighing")]
    public Task<ActionResult<ResultModel<WeighingCommandResult>>> CompleteOrderWeighingAsync(
        int id,
        WeighingOrderCommandRequest request,
        CancellationToken cancellationToken) =>
        ExecuteAsync(() => _workflowService.CompleteOrderWeighingAsync(
            id, request, CurrentUser, cancellationToken));

    /// <summary>获取承运商选项。</summary>
    [Authorize]
    [HttpGet("carrier-options")]
    public Task<ActionResult<ResultModel<List<DispatchCarrierOptionViewModel>>>> GetCarrierOptionsAsync(
        CancellationToken cancellationToken) =>
        ExecuteAsync(() => _workflowService.GetCarrierOptionsAsync(CurrentUser, cancellationToken));

    /// <summary>设置承运商。</summary>
    [Authorize]
    [HttpPut("carrier")]
    public Task<ActionResult<ResultModel<SetDispatchCarrierResult>>> SetCarrierAsync(
        SetDispatchCarrierRequest request,
        CancellationToken cancellationToken) =>
        ExecuteAsync(() => _workflowService.SetCarrierAsync(request, CurrentUser, cancellationToken));

    /// <summary>确认出库。</summary>
    [Authorize]
    [HttpPost("{id:int}/confirm-outbound")]
    public Task<ActionResult<ResultModel<OutboundCommandResult>>> ConfirmOutboundAsync(
        int id,
        OutboundCommandRequest request,
        CancellationToken cancellationToken) =>
        ExecuteAsync(() => _workflowService.ConfirmOutboundAsync(
            id, request, CurrentUser, cancellationToken));

    /// <summary>取消出库。</summary>
    [Authorize]
    [HttpPost("{id:int}/cancel-outbound")]
    public Task<ActionResult<ResultModel<OutboundCommandResult>>> CancelOutboundAsync(
        int id,
        OutboundCommandRequest request,
        CancellationToken cancellationToken) =>
        ExecuteAsync(() => _workflowService.CancelOutboundAsync(
            id, request, CurrentUser, cancellationToken));

    /// <summary>签收出库单。</summary>
    [Authorize]
    [HttpPost("{id:int}/sign")]
    public Task<ActionResult<ResultModel<SignDispatchOrderResult>>> SignAsync(
        int id,
        SignDispatchOrderRequest request,
        CancellationToken cancellationToken) =>
        ExecuteAsync(() => _workflowService.SignAsync(
            id, request, CurrentUser, cancellationToken));

    /// <summary>处理拣货来源变更决策。</summary>
    [Authorize]
    [HttpPost("{id:int}/source-decision")]
    public Task<ActionResult<ResultModel<SourceDecisionResult>>> DecideSourceChangeAsync(
        int id,
        SourceDecisionRequest request,
        CancellationToken cancellationToken) =>
        ExecuteAsync(() => _workflowService.DecideSourceChangeAsync(id, request, CurrentUser, cancellationToken));

    /// <summary>获取出库单打印数据。</summary>
    [HttpGet("{id:int}/print")]
    public Task<ActionResult<ResultModel<DispatchOrderDetailViewModel>>> PrintAsync(
        int id,
        CancellationToken cancellationToken) =>
        ExecuteAsync(() => _workflowService.PrintAsync(id, CurrentUser, cancellationToken));

    private async Task<ActionResult<ResultModel<T>>> ExecuteAsync<T>(Func<Task<T>> operation)
    {
        try
        {
            return Ok(ResultModel<T>.Success(await operation()));
        }
        catch (ArgumentException exception)
        {
            return StatusCode(StatusCodes.Status400BadRequest,
                ResultModel<T>.Error(exception.Message, StatusCodes.Status400BadRequest));
        }
        catch (UnauthorizedAccessException exception)
        {
            return StatusCode(StatusCodes.Status403Forbidden,
                ResultModel<T>.Error(exception.Message, StatusCodes.Status403Forbidden));
        }
        catch (KeyNotFoundException exception)
        {
            return StatusCode(StatusCodes.Status404NotFound,
                ResultModel<T>.Error(exception.Message, StatusCodes.Status404NotFound));
        }
        catch (DispatchWorkflowCommandException exception)
        {
            return StatusCode(StatusCodes.Status409Conflict,
                ResultModel<T>.Error(exception.Message, StatusCodes.Status409Conflict));
        }
        catch (InvalidOperationException exception)
        {
            return StatusCode(StatusCodes.Status409Conflict,
                ResultModel<T>.Error(exception.Message, StatusCodes.Status409Conflict));
        }
    }
}
