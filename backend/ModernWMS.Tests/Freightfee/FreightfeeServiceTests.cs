using Microsoft.Extensions.Localization;
using ModernWMS.Core.JWT;
using ModernWMS.Core.Models;
using ModernWMS.WMS.Services;

namespace ModernWMS.Tests.Freightfee;

public sealed class FreightfeeServiceTests
{
    [Fact]
    public async Task PageAsync_reads_business_rows_without_partition_context()
    {
        var source = new Source();
        source.Rows.Add(new(1, "顺丰", "深圳", "上海", 1, 2, 3, "u", DateTime.UtcNow, DateTime.UtcNow, true));
        var result = await new FreightfeeService(source, new EchoLocalizer()).PageAsync(new PageSearch { pageIndex = 1, pageSize = 20 }, new CurrentUser());
        Assert.Single(result.data);
    }

    private sealed class Source : IFreightfeeDataSource
    {
        public List<FreightfeeData> Rows { get; } = [];
        public Task<(List<FreightfeeData> Rows, int Total)> PageAsync(PageSearch page) => Task.FromResult((Rows.ToList(), Rows.Count));
        public Task<List<FreightfeeData>> GetAllAsync() => Task.FromResult(Rows.ToList());
        public Task<FreightfeeData?> GetAsync(int id) => Task.FromResult(Rows.SingleOrDefault(x => x.id == id));
        public Task<FreightfeeAddResult> AddAsync(FreightfeeData row) => Task.FromResult(new FreightfeeAddResult(FreightfeeWriteStatus.Succeeded, 1));
        public Task<FreightfeeWriteStatus> UpdateAsync(FreightfeeData row) => Task.FromResult(FreightfeeWriteStatus.Succeeded);
        public Task<bool> DeleteAsync(int id) => Task.FromResult(true);
        public Task<int> AddRangeAsync(IReadOnlyCollection<FreightfeeData> rows) => Task.FromResult(rows.Count);
    }

    private sealed class EchoLocalizer : IStringLocalizer<ModernWMS.Core.MultiLanguage>
    {
        public LocalizedString this[string name] => new(name, name);
        public LocalizedString this[string name, params object[] arguments] => new(name, string.Format(name, arguments));
        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => [];
    }
}
