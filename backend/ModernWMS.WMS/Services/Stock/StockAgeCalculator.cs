namespace ModernWMS.WMS.Services.Stock;

/// <summary>
/// Calculates stock age using calendar-day boundaries.
/// </summary>
public static class StockAgeCalculator
{
    /// <summary>
    /// Returns the number of elapsed calendar days, never below zero.
    /// </summary>
    public static int Calculate(DateTime putawayDate, DateTime today) =>
        Math.Max(0, (today.Date - putawayDate.Date).Days);
}
