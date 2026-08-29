namespace ModernWMS.WMS.Services.DispatchWorkflow;

internal sealed record ActualPackingDraftLine(
    string ClientLineKey,
    int? PackingTaskItemId,
    long ErpStockId,
    int ActualQty);

internal sealed record ActualPackingStockIdentity(
    long ErpStockId,
    long? CommodityId,
    long OrderUserId,
    long WarehouseId,
    string SkuCode,
    string CommodityName,
    long AvailableQty);

internal static class ActualPackingLinePolicy
{
    public static void ValidateBox(
        IReadOnlyCollection<ActualPackingDraftLine> lines,
        IReadOnlySet<int> packingTaskItemIds,
        IReadOnlyDictionary<long, ActualPackingStockIdentity> stocks,
        long warehouseId)
    {
        var duplicateKey = lines.GroupBy(line => line.ClientLineKey, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicateKey != null)
            throw new InvalidOperationException($"箱内实际商品行键重复：{duplicateKey.Key}");

        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line.ClientLineKey) || line.ClientLineKey.Length > 64)
                throw new InvalidOperationException("箱内实际商品行键无效");
            if (line.ActualQty <= 0)
                throw new InvalidOperationException("箱内实际商品数量必须为正整数");
            if (line.PackingTaskItemId is int itemId && !packingTaskItemIds.Contains(itemId))
                throw new InvalidOperationException("计划商品不属于当前装箱任务");
            if (!stocks.TryGetValue(line.ErpStockId, out var stock))
                throw new InvalidOperationException("实际ERP库存不存在");
            if (stock.WarehouseId != warehouseId)
                throw new InvalidOperationException("实际ERP库存不属于当前仓库");
        }
    }
}
