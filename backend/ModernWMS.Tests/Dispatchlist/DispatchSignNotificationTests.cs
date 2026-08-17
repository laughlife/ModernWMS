using System.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using ModernWMS.WMS.Services.Dispatchlist;

namespace ModernWMS.Tests.Dispatchlist;

public class DispatchSignNotificationTests
{
    [Fact]
    public async Task TryNotifySignedAsync_posts_the_stable_dispatch_number_and_reports_success()
    {
        var handler = new RecordingHttpHandler(HttpStatusCode.OK);
        var client = CreateClient(handler);

        var succeeded = await client.TryNotifySignedAsync("CW-DISPATCH");

        Assert.True(succeeded);
        Assert.Contains("\"dispatchNo\":\"CW-DISPATCH\"", handler.Body);
        Assert.Equal("token", handler.Token);
    }

    [Fact]
    public async Task TryNotifySignedAsync_reports_failure_while_legacy_notification_still_does_not_throw()
    {
        var handler = new RecordingHttpHandler(HttpStatusCode.InternalServerError);
        var client = CreateClient(handler);

        Assert.False(await client.TryNotifySignedAsync("CW-DISPATCH"));
        await client.NotifySignedAsync("CW-DISPATCH");

        Assert.Equal(2, handler.RequestCount);
    }

    private static DispatchSignNotificationClient CreateClient(RecordingHttpHandler handler)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["ErpIntegration:WmsSignNotificationUrl"] = "https://erp.test/internal/wms/stock-move/signed",
                ["ErpIntegration:InternalToken"] = "token"
            }).Build();
        return new DispatchSignNotificationClient(
            new StaticHttpClientFactory(new HttpClient(handler)), configuration,
            NullLogger<DispatchSignNotificationClient>.Instance);
    }

    private sealed class StaticHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class RecordingHttpHandler(HttpStatusCode statusCode) : HttpMessageHandler
    {
        public string Body { get; private set; } = string.Empty;
        public string Token { get; private set; } = string.Empty;
        public int RequestCount { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            Body = request.Content == null ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);
            Token = request.Headers.GetValues("X-WMS-Internal-Token").Single();
            return new HttpResponseMessage(statusCode);
        }
    }
}
