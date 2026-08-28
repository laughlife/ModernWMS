using System.Net;
using System.Net.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using ModernWMS.WMS.Services.PackingTask;

namespace ModernWMS.Tests.PackingTask;

public class ErpPackingStockClientTests
{
    [Fact]
    public async Task GetPlanAsync_sends_internal_headers_and_actor_id()
    {
        var previous = Environment.GetEnvironmentVariable("ERP_PACKING_STOCK_INTERNAL_TOKEN");
        Environment.SetEnvironmentVariable("ERP_PACKING_STOCK_INTERNAL_TOKEN", "test-secret");
        try
        {
            var handler = new RecordingHandler();
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ErpIntegration:PackingStockBaseUrl"] = "https://ruoyi.internal/"
            }).Build();
            var client = new ErpPackingStockClient(new FixedHttpClientFactory(new HttpClient(handler)), configuration,
                NullLogger<ErpPackingStockClient>.Instance);

            var result = await client.GetPlanAsync(new ErpPackingStockPlanQuery(41, 42, "wms-user", "操作员"));

            Assert.True(result.IsSuccess);
            Assert.Equal("/admin-api/erp/packing-task/internal/stock-plan", handler.Request!.RequestUri!.AbsolutePath);
            Assert.Contains("sellfoxTaskId=41", handler.Request.RequestUri.Query, StringComparison.Ordinal);
            Assert.Contains("sellfoxItemId=42", handler.Request.RequestUri.Query, StringComparison.Ordinal);
            Assert.Contains("actorId=wms-user", handler.Request.RequestUri.Query, StringComparison.Ordinal);
            Assert.Equal("ModernWMS", Assert.Single(handler.Request.Headers.GetValues("X-Internal-Caller")));
            Assert.Equal("test-secret", Assert.Single(handler.Request.Headers.GetValues("X-Internal-Token")));
        }
        finally
        {
            Environment.SetEnvironmentVariable("ERP_PACKING_STOCK_INTERNAL_TOKEN", previous);
        }
    }

    [Fact]
    public async Task UpdateContributionAsync_fails_closed_when_the_environment_secret_is_missing()
    {
        var previous = Environment.GetEnvironmentVariable("ERP_PACKING_STOCK_INTERNAL_TOKEN");
        Environment.SetEnvironmentVariable("ERP_PACKING_STOCK_INTERNAL_TOKEN", null);
        try
        {
            var handler = new RecordingHandler();
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ErpIntegration:PackingStockBaseUrl"] = "https://ruoyi.internal/"
            }).Build();
            var client = new ErpPackingStockClient(new FixedHttpClientFactory(new HttpClient(handler)), configuration,
                NullLogger<ErpPackingStockClient>.Instance);

            var result = await client.UpdateContributionAsync(new ErpPackingStockContributionCommand(1, 2, "request", 3,
                "actor", "操作员", 4, 5, 6, false));

            Assert.False(result.IsSuccess);
            Assert.Null(handler.Request);
        }
        finally
        {
            Environment.SetEnvironmentVariable("ERP_PACKING_STOCK_INTERNAL_TOKEN", previous);
        }
    }

    private sealed class FixedHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"code\":0,\"msg\":\"\",\"data\":{\"rowVersion\":9}}")
            });
        }
    }
}
