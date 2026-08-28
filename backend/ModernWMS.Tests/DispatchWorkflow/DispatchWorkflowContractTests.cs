using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using ModernWMS.Core.JWT;
using ModernWMS.WMS.Controllers.DispatchWorkflow;
using ModernWMS.WMS.Entities.ViewModels.DispatchWorkflow;
using ModernWMS.WMS.Entities.Models;
using ModernWMS.WMS.Services.DispatchWorkflow;
using ModernWMS.WMS.IServices.StockAllocation;
using MySqlConnector;

namespace ModernWMS.Tests.DispatchWorkflow;

public sealed class DispatchWorkflowContractTests
{
    [Fact]
    public void Dispatch_workflow_does_not_own_stock_allocation_mutations()
    {
        var constructorDependencies = typeof(DispatchWorkflowService).GetConstructors()
            .SelectMany(constructor => constructor.GetParameters())
            .Select(parameter => parameter.ParameterType)
            .ToArray();

        Assert.DoesNotContain(typeof(IStockAllocationMutationService), constructorDependencies);
        Assert.NotNull(typeof(DispatchPackingTaskItemEntity).GetProperty("erp_stock_plan_row_version"));
    }

    [Theory]
    [InlineData(nameof(DispatchWorkflowController.CompletePickingAsync))]
    [InlineData(nameof(DispatchWorkflowController.RollbackPendingPickAsync))]
    [InlineData(nameof(DispatchWorkflowController.DecideSourceChangeAsync))]
    [InlineData(nameof(DispatchWorkflowController.StartWeighingAsync))]
    [InlineData(nameof(DispatchWorkflowController.SaveWeighingBoxAsync))]
    [InlineData(nameof(DispatchWorkflowController.CopyWeighingBoxAsync))]
    [InlineData(nameof(DispatchWorkflowController.CompleteTaskWeighingAsync))]
    [InlineData(nameof(DispatchWorkflowController.CompleteOrderWeighingAsync))]
    [InlineData(nameof(DispatchWorkflowController.ConfirmOutboundAsync))]
    [InlineData(nameof(DispatchWorkflowController.CancelOutboundAsync))]
    [InlineData(nameof(DispatchWorkflowController.SignAsync))]
    public void Mutation_endpoints_require_explicit_authorization(string methodName)
    {
        var method = typeof(DispatchWorkflowController).GetMethod(methodName);

        Assert.NotNull(method);
        Assert.NotNull(method!.GetCustomAttribute<AuthorizeAttribute>());
    }

    [Fact]
    public void Weighing_box_routes_use_resource_ids_without_repeating_target_ids_in_bodies()
    {
        var save = typeof(DispatchWorkflowController).GetMethod(
            nameof(DispatchWorkflowController.SaveWeighingBoxAsync))!;
        var copy = typeof(DispatchWorkflowController).GetMethod(
            nameof(DispatchWorkflowController.CopyWeighingBoxAsync))!;

        Assert.Equal("{id:int}/boxes/{boxId:int}",
            save.GetCustomAttribute<Microsoft.AspNetCore.Mvc.HttpPutAttribute>()!.Template);
        Assert.Equal("{id:int}/boxes/{targetBoxId:int}/copy",
            copy.GetCustomAttribute<Microsoft.AspNetCore.Mvc.HttpPostAttribute>()!.Template);
        Assert.DoesNotContain(typeof(SaveWeighingBoxRequest).GetProperties(), property => property.Name == "box_id");
        Assert.DoesNotContain(typeof(CopyWeighingBoxRequest).GetProperties(), property => property.Name == "target_box_id");
    }

    [Fact]
    public void Rollback_pending_pick_route_uses_the_order_id_and_a_versioned_request()
    {
        var rollback = typeof(DispatchWorkflowController).GetMethod(
            nameof(DispatchWorkflowController.RollbackPendingPickAsync))!;

        Assert.Equal("{id:int}/rollback-pending-pick",
            rollback.GetCustomAttribute<Microsoft.AspNetCore.Mvc.HttpPostAttribute>()!.Template);
        Assert.Equal(["request_id", "row_version"],
            typeof(RollbackPendingPickRequest).GetProperties().Select(property => property.Name).OrderBy(name => name).ToArray());
    }

    [Fact]
    public void Source_guard_exposes_an_internal_overload_for_the_callers_transaction()
    {
        var overload = typeof(DispatchWorkflowService).GetMethod(
            nameof(DispatchWorkflowService.EnsurePostPickSourceCurrentAsync),
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            types:
            [
                typeof(MySqlConnection), typeof(MySqlTransaction), typeof(int),
                typeof(CurrentUser), typeof(CancellationToken)
            ],
            modifiers: null);

        Assert.NotNull(overload);
        Assert.Equal(typeof(Task<PostPickSourceGuardResult>), overload!.ReturnType);
    }
}
