namespace ModernWMS.WMS.Services.DispatchWorkflow;

internal sealed record ActualPackingCurrentPick(
    int PickId,
    int? PackingTaskItemId,
    long ErpStockId,
    int Quantity);

internal sealed record ActualPackingTarget(
    string BusinessKey,
    int? PackingTaskItemId,
    long ErpStockId,
    int Quantity);

internal sealed record ActualPackingRelease(int PickId, int Quantity);

internal sealed record ActualPackingReserve(
    string BusinessKey,
    int? PackingTaskItemId,
    long ErpStockId,
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
        if (currentPicks.Any(value => value.Quantity <= 0)
            || actualTargets.Any(value => value.Quantity <= 0))
            throw new InvalidOperationException("实际装箱物化数量必须为正数");

        var releases = new List<ActualPackingRelease>();
        var reserves = new List<ActualPackingReserve>();
        var currentGroups = currentPicks.GroupBy(Key).ToDictionary(group => group.Key, group => group.ToList());
        var targetGroups = actualTargets.GroupBy(Key).ToDictionary(group => group.Key, group => group.ToList());
        var keys = currentGroups.Keys.Concat(targetGroups.Keys).Distinct().Order().ToArray();
        foreach (var key in keys)
        {
            var current = currentGroups.GetValueOrDefault(key) ?? [];
            var targets = targetGroups.GetValueOrDefault(key) ?? [];
            var currentQty = current.Sum(value => value.Quantity);
            var targetQty = targets.Sum(value => value.Quantity);
            if (currentQty > targetQty)
            {
                var remaining = currentQty - targetQty;
                foreach (var pick in current.OrderByDescending(value => value.PickId))
                {
                    var quantity = Math.Min(remaining, pick.Quantity);
                    if (quantity > 0) releases.Add(new ActualPackingRelease(pick.PickId, quantity));
                    remaining -= quantity;
                    if (remaining == 0) break;
                }
            }
            else if (targetQty > currentQty)
            {
                var target = targets.OrderBy(value => value.BusinessKey, StringComparer.Ordinal).First();
                reserves.Add(new ActualPackingReserve(
                    string.Join("+", targets.Select(value => value.BusinessKey).Order(StringComparer.Ordinal)),
                    target.PackingTaskItemId,
                    target.ErpStockId,
                    targetQty - currentQty));
            }
        }
        return new ActualPackingMaterialization(releases, reserves);
    }

    private static ActualPackingGroupKey Key(ActualPackingCurrentPick value) =>
        new(value.PackingTaskItemId, value.ErpStockId);

    private static ActualPackingGroupKey Key(ActualPackingTarget value) =>
        new(value.PackingTaskItemId, value.ErpStockId);

    private readonly record struct ActualPackingGroupKey(
        int? PackingTaskItemId,
        long ErpStockId) : IComparable<ActualPackingGroupKey>
    {
        public int CompareTo(ActualPackingGroupKey other)
        {
            var comparison = Nullable.Compare(PackingTaskItemId, other.PackingTaskItemId);
            return comparison != 0 ? comparison : ErpStockId.CompareTo(other.ErpStockId);
        }
    }
}
