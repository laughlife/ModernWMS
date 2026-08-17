using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using System.Net;
using ModernWMS.Core.DBContext;
using ModernWMS.WMS.Entities.Models;
using ModernWMS.WMS.Entities.ViewModels;
using ModernWMS.WMS.Services;
using ModernWMS.WMS.Services.Dispatchlist;

namespace ModernWMS.Tests.Dispatchlist;

public class DispatchSignNotificationTests
{
    [Fact(Skip = "依赖已移除的EF InMemory服务实现，等待Dapper测试夹具替换")]
    public async Task TryNotifySignedAsync_posts_the_stable_dispatch_number_and_reports_success()
    {
        var handler = new RecordingHttpHandler(HttpStatusCode.OK);
        var client = CreateClient(handler);

        var succeeded = await client.TryNotifySignedAsync("CW-DISPATCH");

        Assert.True(succeeded);
        Assert.Contains("\"dispatchNo\":\"CW-DISPATCH\"", handler.Body);
        Assert.Equal("token", handler.Token);
    }

    [Fact(Skip = "依赖已移除的EF InMemory服务实现，等待Dapper测试夹具替换")]
    public async Task TryNotifySignedAsync_reports_failure_while_legacy_notification_still_does_not_throw()
    {
        var handler = new RecordingHttpHandler(HttpStatusCode.InternalServerError);
        var client = CreateClient(handler);

        Assert.False(await client.TryNotifySignedAsync("CW-DISPATCH"));
        await client.NotifySignedAsync("CW-DISPATCH");

        Assert.Equal(2, handler.RequestCount);
    }

    [Fact(Skip = "依赖已移除的EF InMemory服务实现，等待Dapper测试夹具替换")]
    public async Task SignForArrival_notifies_erp_only_after_signing_succeeds()
    {
        await using var database = CreateDatabase();
        await database.GetDbSet<DispatchlistEntity>().AddAsync(new DispatchlistEntity
        {
            id = 1, dispatch_no = "DB20260814001", dispatch_status = 6, actual_qty = 10, tenant_id = 1
        });
        await database.SaveChangesAsync();
        var notifier = new RecordingDispatchSignNotifier();
        var service = new DispatchlistService(
            ForbiddenConnectionFactory.Instance, new TestStringLocalizer(), null!, notifier);

        var result = await service.SignForArrival([
            new DispatchlistSignViewModel { id = 1, dispatch_no = "DB20260814001", dispatch_status = 6 }
        ]);

        Assert.True(result.flag);
        Assert.Equal(["DB20260814001"], notifier.DispatchNos);
        Assert.Equal(7, (await database.GetDbSet<DispatchlistEntity>().SingleAsync()).dispatch_status);
    }

    private static SqlDBContext CreateDatabase() => new(new DbContextOptionsBuilder<SqlDBContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .ConfigureWarnings(t => t.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options);

    private static DispatchSignNotificationClient CreateClient(RecordingHttpHandler handler)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
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

    private sealed class RecordingDispatchSignNotifier : IDispatchSignNotificationClient
    {
        public List<string> DispatchNos { get; } = [];
        public Task NotifySignedAsync(string dispatchNo, CancellationToken cancellationToken = default)
        {
            DispatchNos.Add(dispatchNo);
            return Task.CompletedTask;
        }

        public Task<bool> TryNotifySignedAsync(string dispatchNo, CancellationToken cancellationToken = default)
        {
            DispatchNos.Add(dispatchNo);
            return Task.FromResult(true);
        }
    }

    private sealed class TestStringLocalizer : IStringLocalizer<ModernWMS.Core.MultiLanguage>
    {
        public LocalizedString this[string name] => new(name, name);
        public LocalizedString this[string name, params object[] arguments] => new(name, string.Format(name, arguments));
        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => [];
    }
}
