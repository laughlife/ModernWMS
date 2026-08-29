using ModernWMS.WMS.Services.DispatchWorkflow;

namespace ModernWMS.Tests.DispatchWorkflow;

public sealed class ActualPackingMaterializationPolicyTests
{
    [Fact]
    public void Actual_less_than_current_releases_only_the_difference()
    {
        var result = ActualPackingMaterializationPolicy.Build(
            [Pick(1, 11, 1001, 500)],
            [Target("11:1001", 11, 1001, 480)]);

        var release = Assert.Single(result.Releases);
        Assert.Equal((1, 20), (release.PickId, release.Quantity));
        Assert.Empty(result.Reserves);
    }

    [Fact]
    public void Equal_actual_quantity_keeps_the_existing_stock()
    {
        var result = ActualPackingMaterializationPolicy.Build(
            [Pick(1, 11, 1001, 500)],
            [Target("11:1001", 11, 1001, 500)]);

        Assert.Empty(result.Releases);
        Assert.Empty(result.Reserves);
    }

    [Fact]
    public void Multiple_lines_for_same_erp_stock_are_grouped_without_allocation_identity()
    {
        var result = ActualPackingMaterializationPolicy.Build(
            [Pick(1, 11, 1001, 480)],
            [Target("box-a", 11, 1001, 250), Target("box-b", 11, 1001, 250)]);

        var reserve = Assert.Single(result.Reserves);
        Assert.Equal((1001L, 20), (reserve.ErpStockId, reserve.Quantity));
        Assert.DoesNotContain(typeof(ActualPackingReserve).GetProperties(), property =>
            property.Name.Contains("Allocation", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Changed_stock_releases_old_and_reserves_actual()
    {
        var result = ActualPackingMaterializationPolicy.Build(
            [Pick(1, 11, 1001, 500)],
            [Target("extra:2002", null, 2002, 501)]);

        var release = Assert.Single(result.Releases);
        Assert.Equal((1, 500), (release.PickId, release.Quantity));
        var reserve = Assert.Single(result.Reserves);
        Assert.Null(reserve.PackingTaskItemId);
        Assert.Equal((2002L, 501), (reserve.ErpStockId, reserve.Quantity));
    }

    private static ActualPackingCurrentPick Pick(
        int pickId, int? taskItemId, long erpStockId, int quantity) =>
        new(pickId, taskItemId, erpStockId, quantity);

    private static ActualPackingTarget Target(
        string businessKey, int? taskItemId, long erpStockId, int quantity) =>
        new(businessKey, taskItemId, erpStockId, quantity);
}
