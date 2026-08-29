namespace ModernWMS.WMS.Services.StockAllocation;

/// <summary>Stable stock identity used before database row locks are acquired.</summary>
public sealed record PackingStockPrelockIdentity(long WarehouseId, long StockId);

/// <summary>Validated and deterministically ordered stock prelock input.</summary>
public sealed record PackingStockPrelockResult(
    IReadOnlyList<long> WarehouseIds,
    IReadOnlyList<long> StockIds);

/// <summary>
/// Validates stock-only packing prelocks without location or allocation semantics.
/// </summary>
public static class PackingStockPrelockPolicy
{
    /// <summary>Returns distinct positive warehouse and stock ids in lock order.</summary>
    public static PackingStockPrelockResult Validate(
        IReadOnlyCollection<long> warehouseIds,
        IReadOnlyCollection<PackingStockPrelockIdentity> identities)
    {
        ArgumentNullException.ThrowIfNull(warehouseIds);
        ArgumentNullException.ThrowIfNull(identities);
        var expectedWarehouses = ValidatePositiveIds(warehouseIds, nameof(warehouseIds));
        if (identities.Count == 0)
            return new PackingStockPrelockResult(expectedWarehouses, []);

        if (identities.Any(identity => identity.WarehouseId <= 0 || identity.StockId <= 0))
            throw new ArgumentOutOfRangeException(nameof(identities), "仓库和ERP库存ID必须大于0");
        var actualWarehouses = identities.Select(identity => identity.WarehouseId)
            .Distinct().Order().ToArray();
        if (!actualWarehouses.SequenceEqual(expectedWarehouses))
            throw new InvalidOperationException("预锁库存所属仓库与请求仓库不一致");

        return new PackingStockPrelockResult(
            expectedWarehouses,
            identities.Select(identity => identity.StockId).Distinct().Order().ToArray());
    }

    private static long[] ValidatePositiveIds(
        IReadOnlyCollection<long> values,
        string parameterName)
    {
        if (values.Count == 0 || values.Any(value => value <= 0))
            throw new ArgumentOutOfRangeException(parameterName, "仓库ID必须大于0");
        return values.Distinct().Order().ToArray();
    }
}
