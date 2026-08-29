using ModernWMS.WMS.Services.DispatchWorkflow;

namespace ModernWMS.Tests.DispatchWorkflow;

public sealed class ActualPackingLinePolicyTests
{
    private const long WarehouseId = 320118;

    [Theory]
    [InlineData(11, 1001, 500)]
    [InlineData(null, 2002, 1)]
    public void Task_linked_and_task_external_stock_lines_are_valid(
        int? taskItemId, long erpStockId, int actualQty)
    {
        var lines = new[] { new ActualPackingDraftLine("line-1", taskItemId, erpStockId, actualQty) };
        var stocks = new Dictionary<long, ActualPackingStockIdentity>
        {
            [erpStockId] = Stock(erpStockId)
        };

        ActualPackingLinePolicy.ValidateBox(lines, new HashSet<int> { 11 }, stocks, WarehouseId);
    }

    [Fact]
    public void No_location_or_allocation_is_required_for_actual_packing()
    {
        var lines = new[] { new ActualPackingDraftLine("line-1", 11, 1001, 501) };
        var stocks = new Dictionary<long, ActualPackingStockIdentity> { [1001] = Stock(1001) };

        ActualPackingLinePolicy.ValidateBox(lines, new HashSet<int> { 11 }, stocks, WarehouseId);

        Assert.DoesNotContain(typeof(ActualPackingStockIdentity).GetProperties(), property =>
            property.Name.Contains("Allocation", StringComparison.OrdinalIgnoreCase)
            || property.Name.Contains("Location", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Duplicate_client_line_keys_in_one_box_are_rejected()
    {
        var lines = new[]
        {
            new ActualPackingDraftLine("same", 11, 1001, 1),
            new ActualPackingDraftLine("same", null, 2002, 1)
        };
        var stocks = new Dictionary<long, ActualPackingStockIdentity>
        {
            [1001] = Stock(1001),
            [2002] = Stock(2002)
        };

        Assert.Throws<InvalidOperationException>(() =>
            ActualPackingLinePolicy.ValidateBox(lines, new HashSet<int> { 11 }, stocks, WarehouseId));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Nonpositive_actual_quantity_is_rejected(int actualQty)
    {
        var lines = new[] { new ActualPackingDraftLine("line-1", 11, 1001, actualQty) };
        var stocks = new Dictionary<long, ActualPackingStockIdentity> { [1001] = Stock(1001) };

        Assert.Throws<InvalidOperationException>(() =>
            ActualPackingLinePolicy.ValidateBox(lines, new HashSet<int> { 11 }, stocks, WarehouseId));
    }

    [Fact]
    public void Stock_from_another_warehouse_is_rejected()
    {
        var lines = new[] { new ActualPackingDraftLine("line-1", null, 1001, 1) };
        var stocks = new Dictionary<long, ActualPackingStockIdentity>
        {
            [1001] = Stock(1001) with { WarehouseId = 999 }
        };

        Assert.Throws<InvalidOperationException>(() =>
            ActualPackingLinePolicy.ValidateBox(lines, new HashSet<int> { 11 }, stocks, WarehouseId));
    }

    private static ActualPackingStockIdentity Stock(long erpStockId) => new(
        erpStockId,
        CommodityId: 501,
        OrderUserId: 77,
        WarehouseId,
        SkuCode: $"SKU-{erpStockId}",
        CommodityName: $"商品-{erpStockId}",
        AvailableQty: -20);
}
