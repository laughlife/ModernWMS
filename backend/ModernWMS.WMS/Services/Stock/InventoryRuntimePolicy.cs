namespace ModernWMS.WMS.Services;

internal static class InventoryRuntimePolicy
{
    internal const string CanonicalMode = "CANONICAL_ERP";
    internal const string LegacyMode = "LEGACY_READ";

    internal static string ResolveMode(string? configuredMode) =>
        string.IsNullOrWhiteSpace(configuredMode)
            ? CanonicalMode
            : configuredMode.Trim().ToUpperInvariant();

    internal static void EnsureWriteAllowed(string? configuredMode, bool maintenanceEnabled)
    {
        if (maintenanceEnabled)
        {
            throw new InvalidOperationException("库存正在维护切换，暂不允许签收入库");
        }

        if (!string.Equals(ResolveMode(configuredMode), CanonicalMode, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("收货仓库尚未切换到 ERP 唯一库存模式，禁止继续写入旧库存");
        }
    }
}
