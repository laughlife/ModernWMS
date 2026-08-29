using ModernWMS.WMS.Services.StockAllocation;
using ModernWMS.WMS.IServices.StockAllocation;

namespace ModernWMS.Tests.StockAllocation;

public sealed class PackingStockMutationContractTests
{
    [Fact]
    public void Prelock_contract_does_not_reject_every_reservation_as_cross_scope()
    {
        var result = PackingStockPrelockPolicy.Validate(
            [320118],
            [new PackingStockPrelockIdentity(320118, 9001)]);

        Assert.Equal([9001L], result.StockIds);
    }

    [Fact]
    public void Stock_only_mutation_contract_has_no_allocation_identity()
    {
        var methods = typeof(IPackingStockMutationService).GetMethods();

        Assert.Contains(methods, method => method.Name == nameof(IPackingStockMutationService.ReserveAsync));
        Assert.DoesNotContain(methods.SelectMany(method => method.GetParameters()), parameter =>
            parameter.Name?.Contains("allocation", StringComparison.OrdinalIgnoreCase) == true);
        Assert.DoesNotContain(typeof(PackingStockMutationResult).GetProperties(), property =>
            property.Name.Contains("allocation", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("LOCK", 20, 100, 5, 105, 80, 25, 105)]
    [InlineData("UNLOCK", 5, 80, 25, 105, 85, 20, 105)]
    [InlineData("SHIP_OUT", 5, 80, 25, 105, 80, 20, 100)]
    [InlineData("ADJUST", -120, 100, 5, 105, -20, 5, -15)]
    public void Stock_only_mutation_applies_expected_balance_transition(
        string eventType,
        long quantity,
        long available,
        long occupied,
        long total,
        long expectedAvailable,
        long expectedOccupied,
        long expectedTotal)
    {
        var after = PackingStockMutationPolicy.Apply(
            eventType,
            quantity,
            new StockQuantitySnapshot(available, occupied, total));

        Assert.Equal(
            new StockQuantitySnapshot(expectedAvailable, expectedOccupied, expectedTotal),
            after);
    }
}
