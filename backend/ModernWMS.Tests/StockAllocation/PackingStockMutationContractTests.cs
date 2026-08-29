using ModernWMS.WMS.Services.StockAllocation;

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
}
