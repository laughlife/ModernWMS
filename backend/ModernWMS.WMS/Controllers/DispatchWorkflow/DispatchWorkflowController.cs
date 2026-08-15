using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ModernWMS.Core.Controller;
using ModernWMS.Core.Models;
using ModernWMS.WMS.Entities.ViewModels.DispatchWorkflow;
using ModernWMS.WMS.IServices.DispatchWorkflow;
using ModernWMS.WMS.Services.DispatchWorkflow;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Http;

namespace ModernWMS.WMS.Controllers.DispatchWorkflow;

[Route("dispatch-workflow")]
[ApiController]
[Authorize]
[ApiExplorerSettings(GroupName = "WMS")]
public sealed class DispatchWorkflowController : BaseController
{
    private readonly IDispatchWorkflowService _workflowService;
    private readonly IDispatchOrderQueryService _queryService;

    public DispatchWorkflowController(
        IDispatchWorkflowService workflowService,
        IDispatchOrderQueryService queryService)
    {
        _workflowService = workflowService;
        _queryService = queryService;
    }

    [HttpPost]
    public Task<ActionResult<ResultModel<DispatchOrderDetailViewModel>>> CreateAsync(
        CreateDispatchOrderRequest request,
        CancellationToken cancellationToken) =>
        ExecuteAsync(() => _workflowService.CreateAsync(request, CurrentUser, cancellationToken));

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

    [HttpGet("counts")]
    public Task<ActionResult<ResultModel<IReadOnlyDictionary<string, int>>>> CountsAsync(
        [FromQuery] long warehouse_id,
        CancellationToken cancellationToken) =>
        ExecuteAsync(async () =>
            (await _queryService.CountsAsync(warehouse_id, CurrentUser, cancellationToken)).Counts);

    [HttpGet("{id:int}")]
    public Task<ActionResult<ResultModel<DispatchOrderDetailViewModel>>> GetAsync(
        int id,
        CancellationToken cancellationToken) =>
        ExecuteAsync(() => _queryService.GetAsync(id, CurrentUser, cancellationToken));

    [HttpPost("{id:int}/reconcile")]
    public Task<ActionResult<ResultModel<DispatchOrderDetailViewModel>>> ReconcileAsync(
        int id,
        CancellationToken cancellationToken) =>
        ExecuteAsync(() => _workflowService.ReconcileAsync(id, CurrentUser, cancellationToken));

    [Authorize]
    [HttpPost("{id:int}/complete-picking")]
    public Task<ActionResult<ResultModel<CompletePickingResult>>> CompletePickingAsync(
        int id,
        CompletePickingRequest request,
        CancellationToken cancellationToken) =>
        ExecuteAsync(() => _workflowService.CompletePickingAsync(id, request, CurrentUser, cancellationToken));

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
        catch (DbUpdateConcurrencyException exception)
        {
            return StatusCode(StatusCodes.Status409Conflict,
                ResultModel<T>.Error(exception.Message, StatusCodes.Status409Conflict));
        }
        catch (DbUpdateException exception)
        {
            return StatusCode(StatusCodes.Status409Conflict,
                ResultModel<T>.Error(exception.Message, StatusCodes.Status409Conflict));
        }
        catch (DispatchWorkflowCommandException exception)
        {
            return StatusCode(StatusCodes.Status409Conflict,
                ResultModel<T>.Error(exception.ErrorCode, StatusCodes.Status409Conflict));
        }
        catch (InvalidOperationException exception)
        {
            return StatusCode(StatusCodes.Status409Conflict,
                ResultModel<T>.Error(exception.Message, StatusCodes.Status409Conflict));
        }
    }
}
