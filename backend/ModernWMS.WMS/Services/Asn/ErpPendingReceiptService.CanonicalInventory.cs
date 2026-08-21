using Dapper;
using ModernWMS.Core.DBContext.Entities;
using ModernWMS.Core.JWT;
using ModernWMS.WMS.Entities.ViewModels;
using MySqlConnector;

namespace ModernWMS.WMS.Services;

/// <summary>
/// Posts receipt quantities to the ERP canonical balance and decomposes that balance by WMS location.
/// The allocation tables are a location subledger, never a second independently consumable balance.
/// </summary>
public partial class ErpPendingReceiptService
{
    private const string CanonicalInventoryMode = "CANONICAL_ERP";

    private async Task EnsureCanonicalInventoryWriteEnabledAsync(long erpWarehouseId, long tenantId)
    {
        var connection = _activeConnection
            ?? throw new InvalidOperationException("数据库连接尚未打开");
        var config = await connection.QuerySingleOrDefaultAsync<InventoryRuntimeGate>(
            """
            SELECT mode,maintenance_enabled
             FROM wms_inventory_runtime_config
             WHERE tenant_id=@tenantId AND erp_warehouse_id=@erpWarehouseId
             LIMIT 1 FOR SHARE
            """,
            new { tenantId, erpWarehouseId },
            _activeTransaction);
        InventoryRuntimePolicy.EnsureWriteAllowed(
            config?.mode,
            config?.maintenance_enabled ?? false);
        // The shared config-row lock lets normal inventory commands run concurrently while a
        // maintenance-window UPDATE waits for every in-flight command to release its shared lock.
        // CANONICAL_ERP may only be enabled after the ERP migration has enforced one active POOL
        // row per warehouse/product/owner/forwarder business dimension.
    }

    private async Task<ErpStockPosting> PostErpStockAsync(
        ErpLogisticsInfoEntity shipment,
        ErpPendingReceiptProductViewModel product,
        long quantity,
        int itemIndex,
        CurrentUser currentUser,
        DateTime now)
    {
        if (quantity <= 0)
        {
            throw new InvalidOperationException("ERP 入库数量必须大于 0");
        }

        var operationKey = BuildReceiptOperationKey(shipment.id, itemIndex);
        var stockId = await FindPoolStockIdForUpdateAsync(shipment, product);
        if (stockId == null)
        {
            try
            {
                await ExecuteAsync(
                    """
                    INSERT INTO trk_stock
                        (freight_forwarder_id, freight_forwarder_name, warehouse_id, warehouse_name,
                         dept_id, dept_name, order_user_id, order_user_name, commodity_id,
                         commodity_sku, commodity_name, product_snapshot_json, stock_batch_no,
                         source_logistics_info_id, source_shipment_batch_id, available_qty,
                         occupied_qty, total_qty, creator, create_time, updater, update_time, deleted)
                    VALUES
                        (@forwarderId, @forwarderName, @warehouseId, @warehouseName,
                         @deptId, @deptName, @orderUserId, @orderUserName, @commodityId,
                         @sku, @name, @snapshot, 'POOL', @shipmentId, @batchId, 0, 0, 0,
                         @operatorName, @now, @operatorName, @now, b'0')
                    """,
                    ("@forwarderId", shipment.freight_forwarder_id),
                    ("@forwarderName", shipment.freight_forwarder_name),
                    ("@warehouseId", shipment.to_warehouse_id), ("@warehouseName", shipment.to_warehouse_name),
                    ("@deptId", product.dept_id), ("@deptName", product.dept_name),
                    ("@orderUserId", product.order_user_id), ("@orderUserName", product.order_user_name),
                    ("@commodityId", product.commodity_id), ("@sku", product.sku),
                    ("@name", product.product_name), ("@snapshot", shipment.product_snapshot_json),
                    ("@shipmentId", shipment.id), ("@batchId", shipment.source_shipment_batch_id),
                    ("@operatorName", Truncate(currentUser.user_name, 64)), ("@now", now));
                stockId = await ScalarAsync<long>("SELECT LAST_INSERT_ID()");
            }
            catch (MySqlException ex) when (ex.Number == 1062)
            {
                stockId = await FindPoolStockIdForUpdateAsync(shipment, product);
                if (stockId == null)
                {
                    throw new InvalidOperationException("ERP 库存池并发创建冲突，签收入库已回滚", ex);
                }
            }
        }

        var balance = await ReadStockBalanceForUpdateAsync(stockId.Value);
        EnsureValidErpBalance(balance);
        var afterAvailable = checked(balance.available_qty + quantity);
        var afterOccupied = balance.occupied_qty;
        var afterTotal = checked(balance.total_qty + quantity);
        if (afterTotal != checked(afterAvailable + afterOccupied))
        {
            throw new InvalidOperationException("ERP 库存数量不守恒，签收入库已回滚");
        }

        var updated = await ExecuteAsync(
            """
            UPDATE trk_stock
               SET available_qty=@afterAvailable,occupied_qty=@afterOccupied,total_qty=@afterTotal,
                   updater=@operatorName,update_time=@now
             WHERE id=@stockId AND deleted=b'0'
               AND available_qty=@beforeAvailable
               AND occupied_qty=@beforeOccupied
               AND total_qty=@beforeTotal
            """,
            ("@afterAvailable", afterAvailable), ("@afterOccupied", afterOccupied),
            ("@afterTotal", afterTotal), ("@operatorName", Truncate(currentUser.user_name, 64)),
            ("@now", now), ("@stockId", stockId.Value),
            ("@beforeAvailable", balance.available_qty), ("@beforeOccupied", balance.occupied_qty),
            ("@beforeTotal", balance.total_qty));
        if (updated != 1)
        {
            throw new InvalidOperationException("ERP 库存已发生并发变化，签收入库已回滚");
        }

        return new ErpStockPosting(
            stockId.Value,
            operationKey,
            balance.available_qty,
            afterAvailable,
            balance.occupied_qty,
            afterOccupied,
            balance.total_qty,
            afterTotal);
    }

    private async Task<long> WriteErpStockReceiptRecordAsync(
        ErpStockPosting posting,
        ErpLogisticsInfoEntity shipment,
        ErpPendingReceiptProductViewModel product,
        long quantity,
        int itemIndex,
        CurrentUser currentUser,
        DateTime now)
    {
        await ExecuteAsync(
            """
            INSERT INTO trk_stock_record
                (record_no,operation_key,biz_type,biz_id,biz_item_id,biz_no,stock_id,
                 freight_forwarder_id,warehouse_id,dept_id,order_user_id,commodity_id,
                 commodity_sku,commodity_name,change_qty,before_qty,after_qty,
                 available_change_qty,occupied_change_qty,total_change_qty,
                 before_available_qty,after_available_qty,before_occupied_qty,after_occupied_qty,
                 before_total_qty,after_total_qty,direction,operate_time,operator_id,operator_name,
                 remark,creator,create_time,updater,update_time,deleted)
            VALUES
                (@recordNo,@operationKey,'RECEIPT_IN',@shipmentId,@itemIndex,@bizNo,@stockId,
                 @forwarderId,@warehouseId,@deptId,@orderUserId,@commodityId,
                 @sku,@name,@qty,@beforeTotal,@afterTotal,
                 @qty,0,@qty,
                 @beforeAvailable,@afterAvailable,@beforeOccupied,@afterOccupied,
                 @beforeTotal,@afterTotal,'IN',@now,@operatorId,@operatorName,
                 'ModernWMS确认签收入库',@operatorName,@now,@operatorName,@now,b'0')
            """,
            ("@recordNo", posting.OperationKey), ("@operationKey", posting.OperationKey),
            ("@shipmentId", shipment.id), ("@itemIndex", itemIndex),
            ("@bizNo", shipment.shipment_batch_no), ("@stockId", posting.StockId),
            ("@forwarderId", shipment.freight_forwarder_id), ("@warehouseId", shipment.to_warehouse_id),
            ("@deptId", product.dept_id), ("@orderUserId", product.order_user_id),
            ("@commodityId", product.commodity_id), ("@sku", product.sku),
            ("@name", product.product_name), ("@qty", quantity),
            ("@beforeAvailable", posting.BeforeAvailable), ("@afterAvailable", posting.AfterAvailable),
            ("@beforeOccupied", posting.BeforeOccupied), ("@afterOccupied", posting.AfterOccupied),
            ("@beforeTotal", posting.BeforeTotal), ("@afterTotal", posting.AfterTotal),
            ("@now", now), ("@operatorId", currentUser.user_id),
            ("@operatorName", Truncate(currentUser.user_name, 64)));
        return await ScalarAsync<long>("SELECT LAST_INSERT_ID()");
    }

    private async Task LockWarehousePoolStocksAsync(long erpWarehouseId)
    {
        var connection = _activeConnection
            ?? throw new InvalidOperationException("数据库连接尚未打开");
        _ = (await connection.QueryAsync<long>(
            """
            SELECT id
              FROM trk_stock
             WHERE warehouse_id=@erpWarehouseId
               AND stock_batch_no='POOL' AND deleted=b'0'
             ORDER BY id
             FOR UPDATE
            """,
            new { erpWarehouseId },
            _activeTransaction)).AsList();
    }

    private async Task<long?> FindPoolStockIdForUpdateAsync(
        ErpLogisticsInfoEntity shipment,
        ErpPendingReceiptProductViewModel product)
    {
        var connection = _activeConnection
            ?? throw new InvalidOperationException("数据库连接尚未打开");
        var stockIds = (await connection.QueryAsync<long>(
            """
            SELECT id FROM trk_stock
             WHERE deleted=b'0' AND stock_batch_no='POOL'
               AND warehouse_id=@warehouseId
               AND commodity_id <=> @commodityId AND commodity_sku <=> @sku
               AND dept_id <=> @deptId AND order_user_id <=> @orderUserId
               AND freight_forwarder_id <=> @forwarderId
             ORDER BY id
             LIMIT 2 FOR UPDATE
            """,
            new
            {
                warehouseId = shipment.to_warehouse_id,
                commodityId = product.commodity_id,
                sku = product.sku,
                deptId = product.dept_id,
                orderUserId = product.order_user_id,
                forwarderId = shipment.freight_forwarder_id
            },
            _activeTransaction)).AsList();
        if (stockIds.Count > 1)
        {
            throw new InvalidOperationException(
                "ERP POOL 库存唯一约束尚未生效或现有数据重复，禁止在唯一库存模式下收货");
        }
        return stockIds.Count == 0 ? null : stockIds[0];
    }

    private async Task<ErpStockBalance> ReadStockBalanceForUpdateAsync(long stockId)
    {
        var connection = _activeConnection
            ?? throw new InvalidOperationException("数据库连接尚未打开");
        return await connection.QuerySingleAsync<ErpStockBalance>(
            """
            SELECT id,available_qty,occupied_qty,total_qty
              FROM trk_stock
             WHERE id=@stockId AND deleted=b'0'
             FOR UPDATE
            """,
            new { stockId },
            _activeTransaction);
    }

    private async Task LockStockAllocationsInIdOrderAsync(long erpStockId, long tenantId)
    {
        var connection = _activeConnection
            ?? throw new InvalidOperationException("数据库连接尚未打开");
        _ = (await connection.QueryAsync<long>(
            """
            SELECT id
              FROM wms_erp_stock_allocation
             WHERE tenant_id=@tenantId AND erp_stock_id=@erpStockId
             ORDER BY id
             FOR UPDATE
            """,
            new { tenantId, erpStockId },
            _activeTransaction)).AsList();
    }

    private async Task<long> PostStockAllocationAsync(
        ErpStockPosting posting,
        long erpStockRecordId,
        ErpLogisticsInfoEntity shipment,
        int itemIndex,
        int skuId,
        ReceiptAllocation allocation,
        CurrentUser currentUser,
        DateTime now)
    {
        await EnsureAllocationDimensionMatchesErpStockAsync(
            shipment,
            posting.StockId,
            allocation,
            currentUser.tenant_id);
        var price = await ScalarAsync<decimal>(
            "SELECT cost FROM wms_sku WHERE id=@skuId",
            ("@skuId", skuId));
        var putawayDate = now.Date;
        var row = await FindStockAllocationAsync(
            posting.StockId,
            allocation,
            currentUser.tenant_id,
            price,
            putawayDate);
        var created = false;
        if (row == null)
        {
            try
            {
                await ExecuteAsync(
                    """
                    INSERT INTO wms_erp_stock_allocation
                        (tenant_id,erp_stock_id,warehouse_area_id,goods_location_id,goods_owner_id,
                         series_number,expiry_date,price,putaway_date,allocated_qty,occupied_qty,
                         location_state,row_version,creator,create_time,updater,update_time)
                    VALUES
                        (@tenantId,@erpStockId,@areaId,@locationId,@ownerId,
                         '','9999-12-31 00:00:00.000000',@price,@putawayDate,@qty,0,
                          @locationState,0,@operatorName,@now,@operatorName,@now)
                    """,
                    ("@tenantId", currentUser.tenant_id), ("@erpStockId", posting.StockId),
                    ("@areaId", allocation.AreaId), ("@locationId", allocation.LocationId),
                    ("@ownerId", allocation.GoodsOwnerId), ("@price", price),
                    ("@putawayDate", putawayDate), ("@qty", allocation.Qty),
                    ("@locationState", allocation.LocationState),
                    ("@operatorName", Truncate(currentUser.user_name, 128)), ("@now", now));
                var allocationId = await ScalarAsync<long>("SELECT LAST_INSERT_ID()");
                row = new StockAllocationBalance(allocationId, 0, 0, allocation.LocationState);
                created = true;
            }
            catch (MySqlException ex) when (ex.Number == 1062)
            {
                row = await FindStockAllocationAsync(
                    posting.StockId,
                    allocation,
                    currentUser.tenant_id,
                    price,
                    putawayDate);
                if (row == null)
                {
                    throw new InvalidOperationException("库存库位分配并发创建冲突，签收入库已回滚", ex);
                }
            }
        }

        var afterAllocated = checked(row.allocated_qty + allocation.Qty);
        var afterOccupied = row.occupied_qty;
        if (!created)
        {
            var updated = await ExecuteAsync(
                """
                UPDATE wms_erp_stock_allocation
                   SET allocated_qty=@afterAllocated,occupied_qty=@afterOccupied,
                        location_state=@locationState,row_version=row_version+1,
                       updater=@operatorName,update_time=@now
                 WHERE id=@allocationId AND tenant_id=@tenantId
                   AND allocated_qty=@beforeAllocated AND occupied_qty=@beforeOccupied
                """,
                ("@afterAllocated", afterAllocated), ("@afterOccupied", afterOccupied),
                ("@locationState", allocation.LocationState),
                ("@operatorName", Truncate(currentUser.user_name, 128)), ("@now", now),
                ("@allocationId", row.id), ("@tenantId", currentUser.tenant_id),
                ("@beforeAllocated", row.allocated_qty), ("@beforeOccupied", row.occupied_qty));
            if (updated != 1)
            {
                throw new InvalidOperationException("库存库位分配已发生并发变化，签收入库已回滚");
            }
        }

        await ExecuteAsync(
            """
            INSERT INTO wms_erp_stock_allocation_log
                (tenant_id,operation_key,biz_type,biz_id,biz_item_id,event_type,
                 erp_stock_id,allocation_id,counterpart_allocation_id,erp_stock_record_id,
                 allocated_delta,occupied_delta,before_allocated_qty,after_allocated_qty,
                 before_occupied_qty,after_occupied_qty,operator,operate_time,remark)
            VALUES
                (@tenantId,@operationKey,'RECEIPT_IN',@shipmentId,@itemIndex,'RECEIPT',
                 @erpStockId,@allocationId,NULL,@recordId,
                 @qty,0,@beforeAllocated,@afterAllocated,
                 @beforeOccupied,@afterOccupied,@operatorName,@now,'确认签收入库位置分配')
            """,
            ("@tenantId", currentUser.tenant_id), ("@operationKey", posting.OperationKey),
            ("@shipmentId", shipment.id), ("@itemIndex", itemIndex),
            ("@erpStockId", posting.StockId), ("@allocationId", row.id),
            ("@recordId", erpStockRecordId), ("@qty", allocation.Qty),
            ("@beforeAllocated", row.allocated_qty), ("@afterAllocated", afterAllocated),
            ("@beforeOccupied", row.occupied_qty), ("@afterOccupied", afterOccupied),
            ("@operatorName", Truncate(currentUser.user_name, 128)), ("@now", now));
        return row.id;
    }

    private async Task<StockAllocationBalance?> FindStockAllocationAsync(
        long erpStockId,
        ReceiptAllocation allocation,
        long tenantId,
        decimal price,
        DateTime putawayDate)
    {
        var connection = _activeConnection
            ?? throw new InvalidOperationException("数据库连接尚未打开");
        return await connection.QuerySingleOrDefaultAsync<StockAllocationBalance>(
            """
            SELECT id,allocated_qty,occupied_qty,location_state
             FROM wms_erp_stock_allocation
             WHERE tenant_id=@tenantId AND erp_stock_id=@erpStockId
               AND warehouse_area_id <=> @areaId
               AND goods_location_id <=> @locationId AND goods_owner_id=@ownerId
               AND series_number='' AND expiry_date='9999-12-31 00:00:00.000000'
               AND price=@price AND putaway_date=@putawayDate
             LIMIT 1 FOR UPDATE
            """,
            new
            {
                tenantId,
                erpStockId,
                areaId = allocation.AreaId,
                locationId = allocation.LocationId,
                ownerId = allocation.GoodsOwnerId,
                price,
                putawayDate
            },
            _activeTransaction);
    }

    private async Task EnsureAllocationDimensionMatchesErpStockAsync(
        ErpLogisticsInfoEntity shipment,
        long erpStockId,
        ReceiptAllocation allocation,
        long tenantId)
    {
        var valid = await ScalarAsync<long>(
            """
            SELECT COUNT(*)
              FROM trk_stock stock
              LEFT JOIN wms_warehouse warehouse
                ON warehouse.erp_warehouse_id=stock.warehouse_id
               AND warehouse.tenant_id=@tenantId AND warehouse.is_valid=1
              LEFT JOIN wms_warehousearea area
                ON area.id=@areaId AND area.warehouse_id=warehouse.id
               AND area.tenant_id=@tenantId AND area.is_valid=1
              LEFT JOIN wms_goodslocation location
                ON location.id=@locationId
               AND location.warehouse_id=warehouse.id
               AND location.warehouse_area_id=area.id
               AND location.tenant_id=@tenantId AND location.is_valid=1
              JOIN wms_erp_goods_owner_map owner_map
                ON owner_map.wms_goods_owner_id=@ownerId
               AND owner_map.erp_dept_id <=> stock.dept_id
               AND owner_map.erp_order_user_id <=> stock.order_user_id
               AND owner_map.tenant_id=@tenantId
             WHERE stock.id=@erpStockId AND stock.deleted=b'0'
               AND stock.warehouse_id=@erpWarehouseId
               AND (@areaId IS NULL OR area.id IS NOT NULL)
               AND (@locationId IS NULL OR location.id IS NOT NULL)
            """,
            ("@tenantId", tenantId), ("@locationId", allocation.LocationId),
            ("@areaId", allocation.AreaId), ("@ownerId", allocation.GoodsOwnerId),
            ("@erpStockId", erpStockId), ("@erpWarehouseId", shipment.to_warehouse_id));
        if (valid != 1)
        {
            throw new InvalidOperationException("库位、库存所属人或 ERP 库存维度不一致，签收入库已回滚");
        }
    }

    private async Task EnsureStockAllocationInvariantAsync(long erpStockId, long tenantId)
    {
        var connection = _activeConnection
            ?? throw new InvalidOperationException("数据库连接尚未打开");
        var invariant = await connection.QuerySingleAsync<StockAllocationInvariant>(
            """
            SELECT stock.available_qty,stock.occupied_qty AS stock_occupied_qty,stock.total_qty,
                   COALESCE(SUM(CASE WHEN allocation.location_state IN ('ACTIVE','UNLOCATED')
                                     THEN allocation.allocated_qty ELSE 0 END),0) allocated_qty,
                   COALESCE(SUM(CASE WHEN allocation.location_state IN ('ACTIVE','UNLOCATED')
                                     THEN allocation.occupied_qty ELSE 0 END),0) occupied_qty
              FROM trk_stock stock
              LEFT JOIN wms_erp_stock_allocation allocation
                ON allocation.erp_stock_id=stock.id AND allocation.tenant_id=@tenantId
             WHERE stock.id=@erpStockId AND stock.deleted=b'0'
             GROUP BY stock.id,stock.available_qty,stock.occupied_qty,stock.total_qty
            """,
            new { tenantId, erpStockId },
            _activeTransaction);
        var allocatedAvailable = checked(invariant.allocated_qty - invariant.occupied_qty);
        if (invariant.allocated_qty != invariant.total_qty
            || invariant.occupied_qty != invariant.stock_occupied_qty
            || allocatedAvailable != invariant.available_qty)
        {
            throw new InvalidOperationException(
                $"ERP库存 {erpStockId} 与库位分配数量不守恒，签收入库已回滚");
        }
    }

    private static void EnsureValidErpBalance(ErpStockBalance balance)
    {
        if (balance.available_qty < 0 || balance.occupied_qty < 0 || balance.total_qty < 0
            || balance.total_qty != checked(balance.available_qty + balance.occupied_qty))
        {
            throw new InvalidOperationException($"ERP库存 {balance.id} 的现有数量不守恒，禁止继续入库");
        }
    }

    private static string BuildReceiptOperationKey(long shipmentId, int itemIndex)
    {
        var operationKey = $"MWMS:RI:{shipmentId}:{itemIndex}";
        if (operationKey.Length > 64)
        {
            throw new InvalidOperationException("收货库存操作幂等键超过 64 个字符");
        }
        return operationKey;
    }

    private sealed record InventoryRuntimeGate(string mode, bool maintenance_enabled);
    private sealed record ErpStockBalance(long id, long available_qty, long occupied_qty, long total_qty);
    private sealed record ErpStockPosting(
        long StockId,
        string OperationKey,
        long BeforeAvailable,
        long AfterAvailable,
        long BeforeOccupied,
        long AfterOccupied,
        long BeforeTotal,
        long AfterTotal);
    private sealed record StockAllocationBalance(
        long id,
        long allocated_qty,
        long occupied_qty,
        string location_state);
    private sealed record StockAllocationInvariant(
        long available_qty,
        long stock_occupied_qty,
        long total_qty,
        long allocated_qty,
        long occupied_qty);
}
