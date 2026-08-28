using ModernWMS.WMS.Services;

namespace ModernWMS.Tests.Architecture;

public class WmsApiIsolationTests
{
    [Fact]
    public void Wms_backend_does_not_ship_ruoyi_http_clients()
    {
        var wmsAssembly = typeof(PackingTaskQueryService).Assembly;

        Assert.Null(wmsAssembly.GetType("ModernWMS.WMS.Services.PackingTask.IErpPackingStockClient"));
        Assert.Null(wmsAssembly.GetType("ModernWMS.WMS.Services.PackingTask.ErpPackingStockClient"));
        Assert.Null(wmsAssembly.GetType("ModernWMS.WMS.Services.Dispatchlist.IDispatchSignNotificationClient"));
        Assert.Null(wmsAssembly.GetType("ModernWMS.WMS.Services.Dispatchlist.DispatchSignNotificationClient"));
    }

    [Fact]
    public void Wms_application_layer_cannot_introduce_outbound_http_clients()
    {
        var repositoryRoot = FindRepositoryRoot();
        var applicationRoot = Path.Combine(repositoryRoot, "backend", "ModernWMS.WMS");
        var source = string.Join('\n', Directory.EnumerateFiles(applicationRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Select(File.ReadAllText));

        Assert.DoesNotContain("HttpClient", source, StringComparison.Ordinal);
        Assert.DoesNotContain("IHttpClientFactory", source, StringComparison.Ordinal);
        Assert.DoesNotContain("System.Net.Http", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Runtime_configuration_cannot_reference_ruoyi_http_routes()
    {
        var repositoryRoot = FindRepositoryRoot();
        var runtimeRoot = Path.Combine(repositoryRoot, "backend", "ModernWMS");
        var configuration = string.Join('\n', Directory.EnumerateFiles(runtimeRoot, "*.json", SearchOption.TopDirectoryOnly)
            .Select(File.ReadAllText));

        Assert.DoesNotContain("ErpIntegration", configuration, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("admin-api", configuration, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("/internal/wms/", configuration, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("WmsSignNotificationUrl", configuration, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("PackingStock", configuration, StringComparison.OrdinalIgnoreCase);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "backend", "ModernWMS.WMS")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("ModernWMS repository root not found");
    }
}
