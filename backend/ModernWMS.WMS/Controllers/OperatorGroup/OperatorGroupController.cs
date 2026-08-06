using Microsoft.AspNetCore.Mvc;
using ModernWMS.Core.Controller;
using ModernWMS.Core.Models;
using ModernWMS.WMS.Entities.ViewModels;
using ModernWMS.WMS.IServices;

namespace ModernWMS.WMS.Controllers;

/// <summary>
/// operator group controller
/// </summary>
[Route("operator-group")]
[ApiController]
[ApiExplorerSettings(GroupName = "Base")]
public class OperatorGroupController : BaseController
{
    /// <summary>
    /// operator group Service
    /// </summary>
    private readonly IOperatorGroupService _operatorGroupService;

    /// <summary>
    /// constructor
    /// </summary>
    /// <param name="operatorGroupService">operator group Service</param>
    public OperatorGroupController(IOperatorGroupService operatorGroupService)
    {
        this._operatorGroupService = operatorGroupService;
    }

    /// <summary>
    /// Get all operator group details
    /// </summary>
    /// <returns>args</returns>
    [HttpGet("all")]
    public async Task<ResultModel<List<OperatorGroupViewModel>>> GetAllAsync()
    {
        var data = await _operatorGroupService.GetAllAsync();
        return ResultModel<List<OperatorGroupViewModel>>.Success(data);
    }
}
