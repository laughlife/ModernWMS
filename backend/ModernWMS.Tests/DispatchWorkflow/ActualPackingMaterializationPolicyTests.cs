using ModernWMS.WMS.Services.DispatchWorkflow;

namespace ModernWMS.Tests.DispatchWorkflow;

public sealed class ActualPackingMaterializationPolicyTests
{
    [Fact]
    public void Actual_less_than_current_releases_only_the_difference()
    {
        var result = ActualPackingMaterializationPolicy.Build(
            [Pick(1, 11, 7, 1001, 101, 500)],
            [Target("11:7:1001:101", 11, 7, 1001, 101, 480)]);

        var release = Assert.Single(result.Releases);
        Assert.Equal(1, release.PickId);
        Assert.Equal(20, release.Quantity);
        Assert.Empty(result.Reserves);
    }

    [Fact]
    public void Equal_actual_quantity_keeps_the_existing_allocation()
    {
        var result = ActualPackingMaterializationPolicy.Build(
            [Pick(1, 11, 7, 1001, 101, 500)],
            [Target("11:7:1001:101", 11, 7, 1001, 101, 500)]);

        Assert.Empty(result.Releases);
        Assert.Empty(result.Reserves);
    }

    [Fact]
    public void Actual_more_than_current_reserves_the_difference_even_when_stock_is_short()
    {
        var result = ActualPackingMaterializationPolicy.Build(
            [Pick(1, 11, 7, 1001, 101, 480)],
            [Target("11:7:1001:101", 11, 7, 1001, 101, 500)]);

        var reserve = Assert.Single(result.Reserves);
        Assert.Equal(20, reserve.Quantity);
        Assert.Equal(101, reserve.StockAllocationId);
        Assert.Empty(result.Releases);
    }

    [Fact]
    public void Changed_allocation_and_task_external_sku_release_old_and_reserve_actual()
    {
        var result = ActualPackingMaterializationPolicy.Build(
            [Pick(1, 11, 7, 1001, 101, 500)],
            [Target("extra:9:2002:202", null, 9, 2002, 202, 501)]);

        var release = Assert.Single(result.Releases);
        Assert.Equal((1, 500), (release.PickId, release.Quantity));
        var reserve = Assert.Single(result.Reserves);
        Assert.Null(reserve.PackingTaskItemId);
        Assert.Equal((9, 2002L, 202L, 501),
            (reserve.WmsSkuId, reserve.ErpStockId, reserve.StockAllocationId, reserve.Quantity));
    }

    private static ActualPackingCurrentPick Pick(
        int pickId,int? taskItemId,int skuId,long erpStockId,long allocationId,int quantity) =>
        new(pickId,taskItemId,skuId,erpStockId,allocationId,quantity);

    private static ActualPackingTarget Target(
        string businessKey,int? taskItemId,int skuId,long erpStockId,long allocationId,int quantity) =>
        new(businessKey,taskItemId,skuId,erpStockId,allocationId,quantity);
}
