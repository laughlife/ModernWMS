using ModernWMS.WMS.Services.Stock;

namespace ModernWMS.Tests.Domain;

public class StockAgeCalculatorTests
{
    [Fact]
    public void Stock_age_uses_calendar_days()
    {
        var putawayDate = new DateTime(2026, 8, 1, 23, 59, 59);
        var today = new DateTime(2026, 8, 5, 0, 0, 1);

        Assert.Equal(4, StockAgeCalculator.Calculate(putawayDate, today));
    }

    [Fact]
    public void Stock_age_never_becomes_negative()
    {
        Assert.Equal(0, StockAgeCalculator.Calculate(new DateTime(2026, 8, 6), new DateTime(2026, 8, 5)));
    }
}
