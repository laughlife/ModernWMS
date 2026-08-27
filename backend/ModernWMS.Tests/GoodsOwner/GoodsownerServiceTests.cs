using Microsoft.Extensions.Localization;
using ModernWMS.Core.JWT;
using ModernWMS.Core.Models;
using ModernWMS.WMS.Services;

namespace ModernWMS.Tests.GoodsOwner;

public sealed class GoodsownerServiceTests
{
    [Fact]
    public async Task PageAsync_reads_all_business_owners_without_partition_context()
    {
        var source = new Source();
        source.Rows.Add(new(1, "货主甲", "深圳", "地址", "负责人", "13800000000", "u", DateTime.UtcNow, DateTime.UtcNow, true));
        var result = await new GoodsownerService(source, new EchoLocalizer()).PageAsync(new PageSearch { pageIndex = 1, pageSize = 20 }, new CurrentUser());
        Assert.Single(result.data);
    }

    private sealed class Source : IGoodsownerDataSource
    {
        public List<GoodsownerData> Rows { get; } = [];
        public Task<(List<GoodsownerData> Rows, int Total)> PageAsync(PageSearch page) => Task.FromResult((Rows.ToList(), Rows.Count));
        public Task<List<GoodsownerData>> GetAllAsync() => Task.FromResult(Rows.ToList());
        public Task<GoodsownerData?> GetAsync(int id) => Task.FromResult(Rows.SingleOrDefault(x => x.id == id));
        public Task<GoodsownerAddResult> AddAsync(GoodsownerData row) => Task.FromResult(new GoodsownerAddResult(GoodsownerWriteStatus.Succeeded, 1));
        public Task<GoodsownerWriteStatus> UpdateAsync(GoodsownerData row) => Task.FromResult(GoodsownerWriteStatus.Succeeded);
        public Task<bool> DeleteAsync(int id) => Task.FromResult(true);
        public Task<GoodsownerImportResult> ImportAsync(IReadOnlyCollection<GoodsownerData> rows) => Task.FromResult(new GoodsownerImportResult(rows.Count, new HashSet<string>()));
    }

    private sealed class EchoLocalizer : IStringLocalizer<ModernWMS.Core.MultiLanguage>
    {
        public LocalizedString this[string name] => new(name, name);
        public LocalizedString this[string name, params object[] arguments] => new(name, string.Format(name, arguments));
        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => [];
    }
}
