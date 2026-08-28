using ModernWMS.WMS.Services;

namespace ModernWMS.Tests.Architecture;

public class WmsApiIsolationTests
{
    [Fact]
    public void Wms_backend_does_not_ship_the_ruoyi_packing_stock_http_client()
    {
        var wmsAssembly = typeof(PackingTaskQueryService).Assembly;

        Assert.Null(wmsAssembly.GetType("ModernWMS.WMS.Services.PackingTask.IErpPackingStockClient"));
        Assert.Null(wmsAssembly.GetType("ModernWMS.WMS.Services.PackingTask.ErpPackingStockClient"));
    }
}
