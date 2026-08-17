using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using ModernWMS.WMS.IServices;
using ModernWMS.WMS.IServices.PackingTask;
using ModernWMS.WMS.Services;
using ModernWMS.WMS.Services.PackingTask;
using Microsoft.AspNetCore.Mvc;
using ModernWMS.WMS.Controllers.DispatchWorkflow;
using ModernWMS.WMS.Entities.ViewModels.DispatchWorkflow;
using ModernWMS.WMS.IServices.DispatchWorkflow;
using ModernWMS.Core.JWT;
using System.Reflection;
using ModernWMS.WMS.Services.DispatchWorkflow;

namespace ModernWMS.Tests.DispatchWorkflow;

[CollectionDefinition("DispatchWorkflowWebApplicationFactory", DisableParallelization = true)]
public sealed class DispatchWorkflowWebApplicationFactoryCollection;

[Collection("DispatchWorkflowWebApplicationFactory")]
public class DispatchWorkflowEndpointTests
{
    [Theory]
    [InlineData("POST", "/dispatch-workflow")]
    [InlineData("POST", "/dispatch-workflow/page")]
    [InlineData("GET", "/dispatch-workflow/counts?warehouse_id=320118")]
    [InlineData("GET", "/dispatch-workflow/1")]
    [InlineData("POST", "/dispatch-workflow/1/reconcile")]
    [InlineData("GET", "/dispatch-workflow/1/print")]
    public async Task Dispatch_workflow_endpoints_reject_anonymous_requests(string method, string url)
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        using var response = await client.SendAsync(new HttpRequestMessage(new HttpMethod(method), url));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public void Application_DI_uses_production_source_and_warehouse_services()
    {
        using var factory = CreateFactory();
        using var scope = factory.Services.CreateScope();

        Assert.IsType<PackingTaskSourceReader>(scope.ServiceProvider.GetRequiredService<IPackingTaskSourceReader>());
        Assert.IsType<WarehouseAccessService>(scope.ServiceProvider.GetRequiredService<IWarehouseAccessService>());
    }

    [Theory]
    [MemberData(nameof(HttpErrorCases))]
    public async Task Controller_maps_domain_errors_to_HTTP_status(Exception exception, int expectedStatus)
    {
        var workflow = ThrowingWorkflow.Create(exception);
        var controller = new DispatchWorkflowController(workflow, ThrowingQuery.Create(exception));

        var result = await controller.CreateAsync(new CreateDispatchOrderRequest
        {
            warehouse_id = 320118,
            source_task_ids = [101]
        }, CancellationToken.None);

        Assert.Equal(expectedStatus, Assert.IsType<ObjectResult>(result.Result).StatusCode);
    }

    public static TheoryData<Exception, int> HttpErrorCases => new()
    {
        { new ArgumentException("bad request"), 400 },
        { new UnauthorizedAccessException("denied"), 403 },
        { new KeyNotFoundException("missing"), 404 },
        { new InvalidOperationException("state conflict"), 409 },
        { DispatchWorkflowCommandException.ConcurrencyConflict(), 409 }
    };

    private static WebApplicationFactory<Program> CreateFactory() =>
        new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Development");
                builder.UseSetting(
                    "ConnectionStrings:MySqlConn",
                    "Server=127.0.0.1;Port=3306;Database=ruoyi_smoke;User Id=smoke");
                builder.UseSetting(
                    "TokenSettings:SigningKey",
                    "modernwms-local-smoke-key-32-bytes-minimum");
                builder.UseSetting("DatabaseInitialization:Enabled", "false");
            });
}

internal sealed class ThrowingWorkflow
{
    public Exception Exception { get; private init; } = null!;

    public static IDispatchWorkflowService Create(Exception exception)
    {
        var contract = DispatchProxy.Create<IDispatchWorkflowService, ThrowingWorkflowProxy>();
        ((ThrowingWorkflowProxy)(object)contract).Owner = new ThrowingWorkflow { Exception = exception };
        return contract;
    }
}

public class ThrowingWorkflowProxy : DispatchProxy
{
    internal ThrowingWorkflow Owner { get; set; } = null!;
    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
        Task.FromException<DispatchOrderDetailViewModel>(Owner.Exception);
}

internal sealed class ThrowingQuery
{
    public Exception Exception { get; private init; } = null!;

    public static IDispatchOrderQueryService Create(Exception exception)
    {
        var contract = DispatchProxy.Create<IDispatchOrderQueryService, ThrowingQueryProxy>();
        ((ThrowingQueryProxy)(object)contract).Owner = new ThrowingQuery { Exception = exception };
        return contract;
    }
}

public class ThrowingQueryProxy : DispatchProxy
{
    internal ThrowingQuery Owner { get; set; } = null!;
    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
        throw Owner.Exception;
}
