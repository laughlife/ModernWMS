namespace ModernWMS.WMS.Services.StockAllocation;

/// <summary>
/// Validates stock conservation while permitting an explicit inventory debt.
/// </summary>
internal static class StockBalanceInvariant
{
    /// <summary>
    /// Validates the canonical ERP balance and one WMS location allocation.
    /// Available, total and allocated quantities may be negative. Occupied quantities may not.
    /// </summary>
    public static void EnsureValid(
        long available,
        long occupied,
        long total,
        long allocated,
        long allocationOccupied)
    {
        if (occupied < 0 || allocationOccupied < 0)
            throw new InvalidOperationException("预占数量不能为负数");
        if (total != checked(available + occupied))
            throw new InvalidOperationException("ERP库存三分量不守恒");
    }
}
