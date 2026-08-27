using ModernWMS.Core.JWT;
using ModernWMS.Core.DBContext.Entities;
using ModernWMS.WMS.Entities.ViewModels;
using System.Data;

namespace ModernWMS.WMS.Services;

/// <summary>
/// Resolves default receipt allocations and validates operator-selected splits.
/// </summary>
public partial class ErpPendingReceiptService
{
    private async Task FillDefaultReceiptAllocationsAsync(
        IReadOnlyList<ErpPendingReceiptProductViewModel> products,
        int warehouseId,
        CurrentUser currentUser)
    {
        foreach (var product in products)
        {
            var area = await ResolveDefaultAreaAsync(warehouseId, product.dept_id, currentUser);
            product.default_warehouse_area_id = area?.Id;
            product.default_warehouse_area_name = area?.Name ?? string.Empty;
            // GET阶段不自动锁定库位；确认事务内再根据当时的真实候选决定唯一库位。
            product.default_goods_location_id = null;
            product.default_goods_location_name = string.Empty;

            var ownerId = await ScalarOrDefaultAsync<int>(
                "SELECT wms_goods_owner_id FROM wms_erp_goods_owner_map WHERE erp_dept_id=@deptId AND erp_order_user_id=@userId LIMIT 1",
                ("@deptId", product.dept_id ?? 0),
                ("@userId", product.order_user_id ?? 0));
            product.default_goods_owner_id = ownerId;
            product.default_goods_owner_name = ownerId == null
                ? BuildDefaultGoodsOwnerName(product)
                : await ScalarReferenceOrDefaultAsync<string>(
                    "SELECT goods_owner_name FROM wms_goodsowner WHERE id=@id AND is_valid=1 LIMIT 1",
                    ("@id", ownerId.Value)) ?? string.Empty;
            if (string.IsNullOrWhiteSpace(product.default_goods_owner_name))
            {
                product.default_goods_owner_id = null;
                product.default_goods_owner_name = BuildDefaultGoodsOwnerName(product);
            }
        }
    }

    private async Task<List<ReceiptAllocation>> BuildReceiptAllocationsAsync(
        ErpLogisticsInfoEntity shipment,
        ErpPendingReceiptProductViewModel product,
        ErpReceiptConfirmItemInputViewModel item,
        long inboundQty,
        CurrentUser currentUser,
        DateTime now)
    {
        if (inboundQty == 0)
        {
            return [];
        }

        var requested = item.allocations ?? [];
        if (requested.Count == 0)
        {
            var defaultArea = await ResolveDefaultAreaForShipmentAsync(shipment, product, currentUser);
            var defaultLocation = defaultArea == null
                ? null
                : await ResolveUniqueLocationForShipmentAsync(shipment, defaultArea.Id, currentUser);
            requested =
            [
                new ErpReceiptAllocationInputViewModel
                {
                    warehouse_area_id = defaultArea?.Id,
                    goods_location_id = defaultLocation?.Id,
                    goods_owner_id = 0,
                    qty = inboundQty
                }
            ];
        }

        var allocatedQty = 0L;
        var allocationInvalid = false;
        foreach (var allocation in requested)
        {
            try
            {
                _ = ReceiptStorageRoutePolicy.Resolve(
                    allocation.warehouse_area_id,
                    allocation.goods_location_id);
            }
            catch (InvalidOperationException)
            {
                allocationInvalid = true;
                break;
            }
            if (allocation.qty <= 0 || allocation.qty > inboundQty - allocatedQty)
            {
                allocationInvalid = true;
                break;
            }
            allocatedQty += allocation.qty;
        }
        if (allocationInvalid || allocatedQty != inboundQty)
        {
            throw new InvalidOperationException($"商品 {product.sku} 的入库分配数量必须大于 0，且合计等于实际入库数量 {inboundQty}");
        }
        if (requested.GroupBy(t => new { t.warehouse_area_id, t.goods_location_id, t.goods_owner_id }).Any(t => t.Count() > 1))
        {
            throw new InvalidOperationException($"商品 {product.sku} 存在重复的仓储位置和库存所属人组合");
        }

        var result = new List<ReceiptAllocation>(requested.Count);
        foreach (var allocation in requested)
        {
            var storage = await ResolveReceiptStorageAsync(
                shipment,
                product,
                currentUser,
                allocation.warehouse_area_id,
                allocation.goods_location_id);
            var ownerId = allocation.goods_owner_id;
            string ownerName;
            if (ownerId == 0)
            {
                ownerId = await EnsureGoodsOwnerAsync(product, currentUser, now);
                ownerName = await ScalarAsync<string>(
                    "SELECT goods_owner_name FROM wms_goodsowner WHERE id=@id LIMIT 1",
                    ("@id", ownerId));
            }
            else
            {
                ownerName = await ScalarReferenceOrDefaultAsync<string>(
                    "SELECT goods_owner_name FROM wms_goodsowner WHERE id=@id AND is_valid=1 LIMIT 1",
                    ("@id", ownerId)) ?? string.Empty;
                if (string.IsNullOrWhiteSpace(ownerName))
                {
                    throw new InvalidOperationException($"商品 {product.sku} 选择的库存所属人无效");
                }
            }

            result.Add(new ReceiptAllocation(
                storage.LocationId,
                storage.LocationName,
                storage.AreaId,
                storage.AreaName,
                storage.OperatorGroupName,
                storage.LocationState,
                ownerId,
                ownerName,
                allocation.qty));
        }
        if (result.GroupBy(t => new { t.AreaId, t.LocationId, t.GoodsOwnerId }).Any(t => t.Count() > 1))
        {
            throw new InvalidOperationException($"商品 {product.sku} 存在重复的仓储位置和库存所属人组合");
        }
        return result;
    }

    private async Task<AreaReference?> ResolveDefaultAreaForShipmentAsync(
        ErpLogisticsInfoEntity shipment,
        ErpPendingReceiptProductViewModel product,
        CurrentUser currentUser)
    {
        var warehouseId = await ScalarOrDefaultAsync<int>(
            "SELECT id FROM wms_warehouse WHERE erp_warehouse_id=@erpWarehouseId AND is_valid=1 LIMIT 1",
            ("@erpWarehouseId", shipment.to_warehouse_id));
        return warehouseId == null
            ? null
            : await ResolveDefaultAreaAsync(warehouseId.Value, product.dept_id, currentUser);
    }

    private async Task<AreaReference?> ResolveDefaultAreaAsync(
        int warehouseId,
        long? deptId,
        CurrentUser currentUser)
    {
        await using var connectionLease = await OpenConnectionLeaseAsync();
        await using var command = CreateCommand(
            """
            WITH RECURSIVE dept_chain AS
            (
                SELECT id,parent_id,0 AS depth
                  FROM system_dept
                 WHERE id=@deptId AND deleted=b'0' AND status=0
                UNION ALL
                SELECT parent.id,parent.parent_id,child.depth+1
                  FROM system_dept parent
                  JOIN dept_chain child ON child.parent_id=parent.id
                 WHERE parent.deleted=b'0' AND parent.status=0 AND child.depth < 20
            )
            SELECT area.id,area.area_name
              FROM dept_chain chain
              JOIN wms_warehousearea_operator_group binding
                ON binding.dept_id=chain.id
              JOIN wms_warehousearea area
                ON area.id=binding.warehouse_area_id
               AND area.warehouse_id=@warehouseId

               AND area.is_valid=1
             ORDER BY chain.depth,area.sort,area.id
             LIMIT 1
            """,
            ("@deptId", deptId), ("@warehouseId", warehouseId));
        await using var reader = await command.ExecuteReaderAsync();
        return await reader.ReadAsync()
            ? new AreaReference(reader.GetInt32(0), reader.GetString(1))
            : null;
    }

    private async Task<LocationReference?> ResolveUniqueLocationForShipmentAsync(
        ErpLogisticsInfoEntity shipment,
        int areaId,
        CurrentUser currentUser)
    {
        var warehouseId = await ScalarOrDefaultAsync<int>(
            "SELECT id FROM wms_warehouse WHERE erp_warehouse_id=@erpWarehouseId AND is_valid=1 LIMIT 1",
            ("@erpWarehouseId", shipment.to_warehouse_id));
        return warehouseId == null
            ? null
            : await ResolveUniqueLocationAsync(warehouseId.Value, areaId);
    }

    private async Task<LocationReference?> ResolveUniqueLocationAsync(
        int warehouseId,
        int areaId)
    {
        await using var connectionLease = await OpenConnectionLeaseAsync();
        await using var command = CreateCommand(
            """
            SELECT id,location_name
              FROM wms_goodslocation
             WHERE warehouse_id=@warehouseId AND warehouse_area_id=@areaId
               AND is_valid=1
              ORDER BY id
              LIMIT 2
              FOR SHARE
            """,
            ("@warehouseId", warehouseId), ("@areaId", areaId));
        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            return null;
        }
        var result = new LocationReference(reader.GetInt32(0), reader.GetString(1));
        return await reader.ReadAsync() ? null : result;
    }

    private static string BuildDefaultGoodsOwnerName(ErpPendingReceiptProductViewModel product)
    {
        var ownerName = string.Join(" / ", new[] { product.dept_name, product.order_user_name }
            .Where(t => !string.IsNullOrWhiteSpace(t)));
        return string.IsNullOrWhiteSpace(ownerName)
            ? $"采购人 {product.order_user_id ?? 0}"
            : ownerName;
    }

    private async Task<Dictionary<int, List<ErpReceiptAllocationViewModel>>> ReadReceiptAllocationsAsync(
        IReadOnlyCollection<int> receiptItemIds)
    {
        var result = new Dictionary<int, List<ErpReceiptAllocationViewModel>>();
        if (receiptItemIds.Count == 0)
        {
            return result;
        }

        var idParameters = receiptItemIds.Select((id, index) => ($"@id{index}", (object?)id)).ToArray();
        var parameters = idParameters;
        var placeholders = string.Join(",", idParameters.Select(t => t.Item1));
        await using var connectionLease = await OpenConnectionLeaseAsync();
        await using var command = CreateCommand(
            $"""
            SELECT receipt_item_id,warehouse_area_id,warehouse_area_name,
                   goods_location_id,goods_location_name,goods_owner_id,goods_owner_name,qty
              FROM wms_receipt_item_owner
             WHERE receipt_item_id IN ({placeholders})
             ORDER BY receipt_item_id,id
            """,
            parameters);
        await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SequentialAccess);
        while (await reader.ReadAsync())
        {
            var receiptItemId = reader.GetInt32(0);
            if (!result.TryGetValue(receiptItemId, out var allocations))
            {
                allocations = [];
                result[receiptItemId] = allocations;
            }
            allocations.Add(new ErpReceiptAllocationViewModel
            {
                warehouse_area_id = reader.IsDBNull(1) ? null : reader.GetInt32(1),
                warehouse_area_name = reader.GetString(2),
                goods_location_id = reader.IsDBNull(3) ? null : reader.GetInt32(3),
                goods_location_name = reader.GetString(4),
                goods_owner_id = reader.GetInt32(5),
                goods_owner_name = reader.GetString(6),
                qty = reader.GetInt64(7)
            });
        }
        return result;
    }
}
