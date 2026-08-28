namespace ModernWMS.WMS.Services.DispatchWorkflow;

internal sealed record ActualPackingCurrentPick(
    int PickId,
    int? PackingTaskItemId,
    int WmsSkuId,
    long ErpStockId,
    long StockAllocationId,
    int Quantity);

internal sealed record ActualPackingTarget(
    string BusinessKey,
    int? PackingTaskItemId,
    int WmsSkuId,
    long ErpStockId,
    long StockAllocationId,
    int Quantity);

internal sealed record ActualPackingRelease(int PickId, int Quantity);

internal sealed record ActualPackingReserve(
    string BusinessKey,
    int? PackingTaskItemId,
    int WmsSkuId,
    long ErpStockId,
    long StockAllocationId,
    int Quantity);

internal sealed record ActualPackingMaterialization(
    IReadOnlyList<ActualPackingRelease> Releases,
    IReadOnlyList<ActualPackingReserve> Reserves);

internal static class ActualPackingMaterializationPolicy
{
    public static ActualPackingMaterialization Build(
        IReadOnlyCollection<ActualPackingCurrentPick> currentPicks,
        IReadOnlyCollection<ActualPackingTarget> actualTargets)
    {
        if (currentPicks.Any(x => x.Quantity <= 0) || actualTargets.Any(x => x.Quantity <= 0))
            throw new InvalidOperationException("实际装箱物化数量必须为正数");

        var releases = new List<ActualPackingRelease>();
        var reserves = new List<ActualPackingReserve>();
        var currentGroups = currentPicks.GroupBy(Key).ToDictionary(x => x.Key, x => x.ToList());
        var targetGroups = actualTargets.GroupBy(Key).ToDictionary(x => x.Key, x => x.ToList());
        var keys = currentGroups.Keys.Concat(targetGroups.Keys).Distinct().OrderBy(x => x).ToArray();

        foreach (var key in keys)
        {
            var current = currentGroups.GetValueOrDefault(key) ?? [];
            var targets = targetGroups.GetValueOrDefault(key) ?? [];
            var currentQty = current.Sum(x => x.Quantity);
            var targetQty = targets.Sum(x => x.Quantity);
            if (currentQty > targetQty)
            {
                var remaining = currentQty - targetQty;
                foreach (var pick in current.OrderByDescending(x => x.PickId))
                {
                    var quantity = Math.Min(remaining, pick.Quantity);
                    if (quantity > 0) releases.Add(new ActualPackingRelease(pick.PickId, quantity));
                    remaining -= quantity;
                    if (remaining == 0) break;
                }
            }
            else if (targetQty > currentQty)
            {
                var target = targets.OrderBy(x => x.BusinessKey, StringComparer.Ordinal).First();
                reserves.Add(new ActualPackingReserve(
                    string.Join("+", targets.Select(x => x.BusinessKey).Order(StringComparer.Ordinal)),
                    target.PackingTaskItemId,
                    target.WmsSkuId,
                    target.ErpStockId,
                    target.StockAllocationId,
                    targetQty - currentQty));
            }
        }

        return new ActualPackingMaterialization(releases, reserves);
    }

    private static ActualPackingGroupKey Key(ActualPackingCurrentPick value) => new(
        value.PackingTaskItemId,value.WmsSkuId,value.ErpStockId,value.StockAllocationId);

    private static ActualPackingGroupKey Key(ActualPackingTarget value) => new(
        value.PackingTaskItemId,value.WmsSkuId,value.ErpStockId,value.StockAllocationId);

    private readonly record struct ActualPackingGroupKey(
        int? PackingTaskItemId,
        int WmsSkuId,
        long ErpStockId,
        long StockAllocationId) : IComparable<ActualPackingGroupKey>
    {
        public int CompareTo(ActualPackingGroupKey other)
        {
            var comparison = Nullable.Compare(PackingTaskItemId, other.PackingTaskItemId);
            if (comparison != 0) return comparison;
            comparison = WmsSkuId.CompareTo(other.WmsSkuId);
            if (comparison != 0) return comparison;
            comparison = ErpStockId.CompareTo(other.ErpStockId);
            return comparison != 0 ? comparison : StockAllocationId.CompareTo(other.StockAllocationId);
        }
    }
}
