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
}
