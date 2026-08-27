using Microsoft.Extensions.Localization;
using ModernWMS.Core.JWT;
using ModernWMS.WMS.Services;

namespace ModernWMS.Tests.Company;

public sealed class CompanyServiceTests
{
    [Fact]
    public async Task GetAllAsync_returns_all_companies_without_partition_context()
    {
        var source = new Source();
        source.Rows.Add(new(1, "甲公司", "深圳", "地址", "负责人", "13800000000", DateTime.UtcNow, DateTime.UtcNow));
        source.Rows.Add(new(2, "乙公司", "上海", "地址", "负责人", "13900000000", DateTime.UtcNow, DateTime.UtcNow));
        var rows = await new CompanyService(source, new EchoLocalizer()).GetAllAsync(new CurrentUser());
        Assert.Equal(2, rows.Count);
    }

    private sealed class Source : ICompanyDataSource
    {
        public List<CompanyData> Rows { get; } = [];
        public Task<List<CompanyData>> GetAllAsync() => Task.FromResult(Rows.ToList());
        public Task<CompanyData?> GetAsync(int id) => Task.FromResult(Rows.SingleOrDefault(x => x.id == id));
        public Task<CompanyAddResult> AddAsync(CompanyData row) { var id = Rows.Count + 1; Rows.Add(row with { id = id }); return Task.FromResult(new CompanyAddResult(CompanyWriteStatus.Succeeded, id)); }
        public Task<CompanyWriteStatus> UpdateAsync(CompanyData row) => Task.FromResult(CompanyWriteStatus.Succeeded);
        public Task<bool> DeleteAsync(int id) => Task.FromResult(Rows.RemoveAll(x => x.id == id) > 0);
    }

    private sealed class EchoLocalizer : IStringLocalizer<ModernWMS.Core.MultiLanguage>
    {
        public LocalizedString this[string name] => new(name, name);
        public LocalizedString this[string name, params object[] arguments] => new(name, string.Format(name, arguments));
        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => [];
    }
}
