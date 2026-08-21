using ModernWMS.WMS.Services;

namespace ModernWMS.Tests.Asn;

public sealed class ReceiptStorageRoutePolicyTests
{
    [Fact]
    public void Resolve_keeps_a_warehouse_only_receipt_when_no_area_binding_exists()
    {
        var route = ReceiptStorageRoutePolicy.Resolve(null, null);

        Assert.Null(route.WarehouseAreaId);
        Assert.Null(route.GoodsLocationId);
        Assert.Equal("ACTIVE", route.LocationState);
    }

    [Fact]
    public void Resolve_keeps_an_area_without_inventing_a_location()
    {
        var route = ReceiptStorageRoutePolicy.Resolve(12, null);

        Assert.Equal(12, route.WarehouseAreaId);
        Assert.Null(route.GoodsLocationId);
        Assert.Equal("ACTIVE", route.LocationState);
    }

    [Fact]
    public void Resolve_uses_the_bound_location_when_both_levels_exist()
    {
        var route = ReceiptStorageRoutePolicy.Resolve(12, 34);

        Assert.Equal(12, route.WarehouseAreaId);
        Assert.Equal(34, route.GoodsLocationId);
        Assert.Equal("ACTIVE", route.LocationState);
    }

    [Fact]
    public void Resolve_rejects_a_location_without_its_parent_area()
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            ReceiptStorageRoutePolicy.Resolve(null, 34));

        Assert.Equal("库位存在时必须同时保留所属库区", error.Message);
    }

    [Fact]
    public void Resolve_treats_legacy_zero_values_as_missing_optional_levels()
    {
        var route = ReceiptStorageRoutePolicy.Resolve(0, 0);

        Assert.Null(route.WarehouseAreaId);
        Assert.Null(route.GoodsLocationId);
        Assert.Equal("ACTIVE", route.LocationState);
    }
}
