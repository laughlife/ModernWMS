namespace ModernWMS.WMS.Services;

internal static class ReceiptStorageRoutePolicy
{
    internal static ReceiptStorageRoute Resolve(int? warehouseAreaId, int? goodsLocationId)
    {
        warehouseAreaId = warehouseAreaId > 0 ? warehouseAreaId : null;
        goodsLocationId = goodsLocationId > 0 ? goodsLocationId : null;

        if (goodsLocationId != null && warehouseAreaId == null)
        {
            throw new InvalidOperationException("库位存在时必须同时保留所属库区");
        }

        return new ReceiptStorageRoute(
            warehouseAreaId,
            goodsLocationId,
            "ACTIVE");
    }
}

internal sealed record ReceiptStorageRoute(
    int? WarehouseAreaId,
    int? GoodsLocationId,
    string LocationState);
