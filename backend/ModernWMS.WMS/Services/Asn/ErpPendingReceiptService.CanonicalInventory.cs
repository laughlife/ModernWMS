using Dapper;
using ModernWMS.Core.DBContext.Entities;
using ModernWMS.Core.JWT;
using ModernWMS.WMS.Entities.ViewModels;
using ModernWMS.WMS.Services.StockAllocation;
using MySqlConnector;

namespace ModernWMS.WMS.Services;

/// <summary>
/// Posts receipt quantities to the ERP canonical balance and decomposes that balance by WMS location.
/// The allocation tables are a location subledger, never a second independently consumable balance.
/// </summary>
public partial class ErpPendingReceiptService
{
    private static Task EnsureCanonicalInventoryWriteEnabledAsync(long erpWarehouseId)
    {
        if(erpWarehouseId<=0)throw new InvalidOperationException("ERP仓库标识无效");
        return Task.CompletedTask;
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

    private static void EnsureValidErpBalance(ErpStockBalance balance)
    {
        StockBalanceInvariant.EnsureValid(
            balance.available_qty,
            balance.occupied_qty,
            balance.total_qty,
            balance.total_qty,
            balance.occupied_qty);
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
}
