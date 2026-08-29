namespace ModernWMS.WMS.Services;

/// <summary>An active user candidate matching a Sellfox task creator snapshot.</summary>
public sealed record PackingTaskOwnerCandidate(long UserId, string Name);

/// <summary>Resolves a task creator without widening stock visibility.</summary>
public static class PackingTaskOwnerPolicy
{
    /// <summary>Returns the only active matching system user id.</summary>
    public static long Resolve(
        string? creatorName,
        IReadOnlyCollection<PackingTaskOwnerCandidate> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        var normalizedName = creatorName?.Trim() ?? string.Empty;
        if (normalizedName.Length == 0)
            throw new InvalidOperationException("装箱任务缺少创建人，无法确定库存边界");
        var matches = candidates
            .Where(candidate => string.Equals(candidate.Name.Trim(), normalizedName,
                StringComparison.OrdinalIgnoreCase))
            .Select(candidate => candidate.UserId)
            .Distinct()
            .ToArray();
        return matches.Length switch
        {
            1 when matches[0] > 0 => matches[0],
            0 => throw new InvalidOperationException(
                $"装箱任务创建人“{normalizedName}”未匹配到有效系统用户"),
            _ => throw new InvalidOperationException(
                $"装箱任务创建人“{normalizedName}”匹配到多个系统用户")
        };
    }
}
