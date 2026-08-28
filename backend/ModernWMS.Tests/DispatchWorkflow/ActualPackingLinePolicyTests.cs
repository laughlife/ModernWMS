using ModernWMS.WMS.Services.DispatchWorkflow;

namespace ModernWMS.Tests.DispatchWorkflow;

public sealed class ActualPackingLinePolicyTests
{
    private const long WarehouseId = 320118;

    [Theory]
    [InlineData(11, 101, 7, 500)]
    [InlineData(null, 202, 9, 1)]
    public void Task_linked_and_task_external_actual_lines_are_valid(
        int? taskItemId,
        long allocationId,
        int skuId,
        int actualQty)
    {
        var lines = new[] { new ActualPackingDraftLine("line-1", taskItemId, allocationId, actualQty) };
        var allocations = new Dictionary<long, ActualPackingStockIdentity>
        {
            [allocationId] = Stock(allocationId, skuId, ownerId: taskItemId is null ? 88 : 77)
        };

        ActualPackingLinePolicy.ValidateBox(lines, new HashSet<int> { 11 }, allocations, WarehouseId);
    }

    [Fact]
    public void Different_sku_and_other_owner_do_not_block_a_task_linked_line()
    {
        var lines = new[] { new ActualPackingDraftLine("line-1", 11, 202, 501) };
        var allocations = new Dictionary<long, ActualPackingStockIdentity>
        {
            [202] = Stock(202, skuId: 999, ownerId: 1234)
        };

        ActualPackingLinePolicy.ValidateBox(lines, new HashSet<int> { 11 }, allocations, WarehouseId);
    }

    [Fact]
    public void Duplicate_client_line_keys_in_one_box_are_rejected()
    {
        var lines = new[]
        {
            new ActualPackingDraftLine("same", 11, 101, 1),
            new ActualPackingDraftLine("same", null, 202, 1)
        };
        var allocations = new Dictionary<long, ActualPackingStockIdentity>
        {
            [101] = Stock(101, 7, 77),
            [202] = Stock(202, 9, 88)
        };

        Assert.Throws<InvalidOperationException>(() =>
            ActualPackingLinePolicy.ValidateBox(lines, new HashSet<int> { 11 }, allocations, WarehouseId));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Nonpositive_actual_quantity_is_rejected(int actualQty)
    {
        var lines = new[] { new ActualPackingDraftLine("line-1", 11, 101, actualQty) };
        var allocations = new Dictionary<long, ActualPackingStockIdentity>
        {
            [101] = Stock(101, 7, 77)
        };

        Assert.Throws<InvalidOperationException>(() =>
            ActualPackingLinePolicy.ValidateBox(lines, new HashSet<int> { 11 }, allocations, WarehouseId));
    }

    [Fact]
    public void Allocation_from_another_warehouse_is_rejected()
    {
        var lines = new[] { new ActualPackingDraftLine("line-1", null, 101, 1) };
        var allocations = new Dictionary<long, ActualPackingStockIdentity>
        {
            [101] = Stock(101, 7, 77) with { WarehouseId = 999 }
        };

        Assert.Throws<InvalidOperationException>(() =>
            ActualPackingLinePolicy.ValidateBox(lines, new HashSet<int> { 11 }, allocations, WarehouseId));
    }

    private static ActualPackingStockIdentity Stock(long allocationId, int skuId, int ownerId) => new(
        allocationId,
        ErpStockId: allocationId + 1000,
        WmsSkuId: skuId,
        GoodsOwnerId: ownerId,
        GoodsLocationId: 66,
        WarehouseId,
        LocationState: "ACTIVE",
        SkuCode: $"SKU-{skuId}",
        CommodityName: $"商品-{skuId}",
        AvailableQty: -20);
}
