using ModernWMS.WMS.Services;

namespace ModernWMS.Tests.Asn;

public sealed class InventoryRuntimePolicyTests
{
    [Fact]
    public void ResolveMode_treats_a_missing_row_as_canonical_inventory()
    {
        Assert.Equal("CANONICAL_ERP", InventoryRuntimePolicy.ResolveMode(null));
    }

    [Fact]
    public void EnsureWriteAllowed_allows_a_missing_row_when_inventory_is_globally_canonical()
    {
        InventoryRuntimePolicy.EnsureWriteAllowed(null, maintenanceEnabled: false);
    }

    [Fact]
    public void EnsureWriteAllowed_rejects_an_explicit_maintenance_window()
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            InventoryRuntimePolicy.EnsureWriteAllowed("CANONICAL_ERP", maintenanceEnabled: true));

        Assert.Equal("库存正在维护切换，暂不允许签收入库", error.Message);
    }

    [Fact]
    public void EnsureWriteAllowed_rejects_an_explicit_legacy_override()
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            InventoryRuntimePolicy.EnsureWriteAllowed("LEGACY_READ", maintenanceEnabled: false));

        Assert.Equal("收货仓库尚未切换到 ERP 唯一库存模式，禁止继续写入旧库存", error.Message);
    }
}
