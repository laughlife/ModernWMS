using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Localization;
using ModernWMS.Core.DBContext;
using ModernWMS.WMS.Entities.Models;
using ModernWMS.WMS.Entities.ViewModels;
using ModernWMS.WMS.Services;
using ModernWMS.WMS.Services.Dispatchlist;

namespace ModernWMS.Tests.Dispatchlist;

public class DispatchSignNotificationTests
{
    [Fact]
    public async Task SignForArrival_notifies_erp_only_after_signing_succeeds()
    {
        await using var database = CreateDatabase();
        await database.GetDbSet<DispatchlistEntity>().AddAsync(new DispatchlistEntity
        {
            id = 1, dispatch_no = "DB20260814001", dispatch_status = 6, actual_qty = 10, tenant_id = 1
        });
        await database.SaveChangesAsync();
        var notifier = new RecordingDispatchSignNotifier();
        var service = new DispatchlistService(database, new TestStringLocalizer(), null!, notifier);

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

    private sealed class RecordingDispatchSignNotifier : IDispatchSignNotificationClient
    {
        public List<string> DispatchNos { get; } = [];
        public Task NotifySignedAsync(string dispatchNo, CancellationToken cancellationToken = default)
        {
            DispatchNos.Add(dispatchNo);
            return Task.CompletedTask;
        }
    }

    private sealed class TestStringLocalizer : IStringLocalizer<ModernWMS.Core.MultiLanguage>
    {
        public LocalizedString this[string name] => new(name, name);
        public LocalizedString this[string name, params object[] arguments] => new(name, string.Format(name, arguments));
        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => [];
    }
}
