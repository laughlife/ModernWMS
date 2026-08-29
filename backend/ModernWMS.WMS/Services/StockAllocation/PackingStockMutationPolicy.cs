using ModernWMS.WMS.IServices.StockAllocation;

namespace ModernWMS.WMS.Services.StockAllocation;

/// <summary>Pure stock balance transitions for packing mutations.</summary>
public static class PackingStockMutationPolicy
{
    /// <summary>Applies a mutation while allowing inventory debt but never negative occupied quantity.</summary>
    public static StockQuantitySnapshot Apply(
        string eventType,
        long quantity,
        StockQuantitySnapshot before)
    {
        if (eventType != "ADJUST" && quantity <= 0)
            throw new ArgumentOutOfRangeException(nameof(quantity), "数量必须大于0");
        if (eventType == "ADJUST" && quantity == 0)
            throw new ArgumentOutOfRangeException(nameof(quantity), "调整数量不能为0");

        var after = eventType switch
        {
            "LOCK" => new StockQuantitySnapshot(
                checked(before.AvailableQty - quantity),
                checked(before.OccupiedQty + quantity),
                before.TotalQty),
            "UNLOCK" => new StockQuantitySnapshot(
                checked(before.AvailableQty + quantity),
                checked(before.OccupiedQty - quantity),
                before.TotalQty),
            "SHIP_OUT" => new StockQuantitySnapshot(
                before.AvailableQty,
                checked(before.OccupiedQty - quantity),
                checked(before.TotalQty - quantity)),
            "ADJUST" => new StockQuantitySnapshot(
                checked(before.AvailableQty + quantity),
                before.OccupiedQty,
                checked(before.TotalQty + quantity)),
            _ => throw new ArgumentException($"不支持的库存动作：{eventType}", nameof(eventType))
        };
        if (after.OccupiedQty < 0)
            throw new InvalidOperationException("库存占用数量不能小于0");
        if (after.AvailableQty + after.OccupiedQty != after.TotalQty)
            throw new InvalidOperationException("ERP库存数量不守恒");
        return after;
    }
}
