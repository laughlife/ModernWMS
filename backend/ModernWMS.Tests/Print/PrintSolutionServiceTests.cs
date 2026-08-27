using Microsoft.Extensions.Localization;
using ModernWMS.Core.JWT;
using ModernWMS.Core.Models;
using ModernWMS.WMS.Services;

namespace ModernWMS.Tests.Print;

public sealed class PrintSolutionServiceTests
{
    [Fact]
    public async Task Path_lookup_uses_path_and_tab_as_business_identity()
    {
        var source = new Source();
        source.Rows.Add(new(1, "shipment", "main", "模板", "{}", 100, 80, "A4", DateTime.UtcNow));
        var rows = await new PrintSolutionService(source, new EchoLocalizer()).GetByPathAsync(
            new ModernWMS.WMS.Entities.ViewModels.PrintSolutionGetByPathInputViewModel { vue_path = "shipment", tab_page = "main" }, new CurrentUser());
        Assert.Single(rows);
    }

    private sealed class Source : IPrintSolutionDataSource
    {
        public List<PrintSolutionData> Rows { get; } = [];
        public Task<(List<PrintSolutionData> Rows, int Total)> PageAsync(PageSearch page) => Task.FromResult((Rows.ToList(), Rows.Count));
        public Task<List<PrintSolutionData>> GetAllAsync() => Task.FromResult(Rows.ToList());
        public Task<PrintSolutionData?> GetAsync(int id) => Task.FromResult(Rows.SingleOrDefault(x => x.id == id));
        public Task<List<PrintSolutionData>> GetByPathAsync(string path, string tab) => Task.FromResult(Rows.Where(x => x.vue_path == path && x.tab_page == tab).ToList());
        public Task<int> AddAsync(PrintSolutionData row) => Task.FromResult(1);
        public Task<PrintSolutionWriteStatus> UpdateAsync(PrintSolutionData row) => Task.FromResult(PrintSolutionWriteStatus.Succeeded);
        public Task<bool> DeleteAsync(int id) => Task.FromResult(true);
    }

    private sealed class EchoLocalizer : IStringLocalizer<ModernWMS.Core.MultiLanguage>
    {
        public LocalizedString this[string name] => new(name, name);
        public LocalizedString this[string name, params object[] arguments] => new(name, string.Format(name, arguments));
        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => [];
    }
}
