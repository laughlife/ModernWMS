using ModernWMS.WMS.Services.StockAllocation;

namespace ModernWMS.Tests.StockAllocation;

public sealed class StockBalanceInvariantTests
{
    [Fact]
    public void Negative_available_total_and_allocation_are_valid_when_stock_conservation_holds()
    {
        StockBalanceInvariant.EnsureValid(-20, 0, -20, -20, 0);
    }

    [Fact]
    public void Allocation_occupied_may_exceed_allocated_to_represent_inventory_debt()
    {
        StockBalanceInvariant.EnsureValid(-20, 500, 480, 480, 500);
    }

    [Theory]
    [InlineData(0, -1, -1, 0, 0)]
    [InlineData(0, 0, 1, 0, 0)]
    [InlineData(0, 0, 0, 0, -1)]
    public void Negative_occupied_or_broken_stock_conservation_is_rejected(
        long available,
        long occupied,
        long total,
        long allocated,
        long allocationOccupied)
    {
        Assert.Throws<InvalidOperationException>(() =>
            StockBalanceInvariant.EnsureValid(
                available,
                occupied,
                total,
                allocated,
                allocationOccupied));
    }
}
