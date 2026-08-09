using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using ModernWMS.Core.DBContext.Entities;
using ModernWMS.Core.JWT;
using ModernWMS.WMS.Entities.ViewModels;

namespace ModernWMS.WMS.Services;

public partial class ErpPendingReceiptService
{
    private const string ReceiptBizType = "RECEIPT_IN";

    private async Task LockShipmentAsync(long shipmentId)
    {
        await ScalarAsync<long?>(
            "SELECT id FROM trk_logistics_info WHERE id = @id AND deleted = b'0' FOR UPDATE",
            ("@id", shipmentId));
    }

    private async Task ApplyInventoryReceiptAsync(
        ErpLogisticsInfoEntity shipment,
        IReadOnlyList<ErpPendingReceiptProductViewModel> products,
        ErpReceiptConfirmInputViewModel input,
        int receiptId,
        long actualReceiptQty,
        long lossQty,
        long inboundQty,
        string freightPaymentStatus,
        CurrentUser currentUser,
        DateTime now)
    {
        await UpdateShipmentReceiptAsync(
            shipment,
            input,
            actualReceiptQty,
            lossQty,
            freightPaymentStatus,
            currentUser,
            now);
        shipment.actual_receipt_qty = actualReceiptQty;
        shipment.receipt_remark = input.receipt_remark?.Trim();
        shipment.receipt_time = now;
        var locationId = await EnsureReceiptLocationAsync(shipment, currentUser, now);
        var inputItems = input.items.ToDictionary(t => t.source_item_key, StringComparer.Ordinal);

        for (var index = 0; index < products.Count; index++)
        {
            var product = products[index];
            var item = inputItems[product.source_item_key];
            var itemInboundQty = checked(item.actual_receipt_qty - item.loss_qty);
            var erpStockId = 0L;
            var wmsSkuId = await EnsureWmsSkuAsync(product, currentUser, now);
            var goodsOwnerId = await EnsureGoodsOwnerAsync(product, currentUser, now);
            var wmsStockId = 0;

            if (itemInboundQty > 0)
            {
                erpStockId = await PostErpStockAsync(shipment, product, itemInboundQty, index + 1, currentUser, now);
                wmsStockId = await PostWmsStockAsync(
                    shipment.id,
                    index + 1,
                    wmsSkuId,
                    locationId,
                    goodsOwnerId,
                    itemInboundQty,
                    currentUser,
                    now);
            }

            await ExecuteAsync(
                """
                INSERT INTO wms_erp_receipt_item
                    (receipt_id, shipment_id, source_item_key, task_item_id, allocation_id,
                     commodity_id, commodity_sku, commodity_name, dept_id, order_user_id,
                     shipment_qty, actual_receipt_qty, loss_qty, inbound_qty, erp_stock_id,
                     wms_sku_id, wms_stock_id, create_time, tenant_id)
                VALUES
                    (@receiptId, @shipmentId, @sourceItemKey, @taskItemId, @allocationId,
                     @commodityId, @sku, @name, @deptId, @orderUserId,
                     @shipmentQty, @actualQty, @lossQty, @inboundQty, @erpStockId,
                     @wmsSkuId, @wmsStockId, @now, @tenantId)
                """,
                ("@receiptId", receiptId), ("@shipmentId", shipment.id),
                ("@sourceItemKey", product.source_item_key), ("@taskItemId", product.task_item_id),
                ("@allocationId", product.allocation_id), ("@commodityId", product.commodity_id),
                ("@sku", product.sku), ("@name", product.product_name),
                ("@deptId", product.dept_id), ("@orderUserId", product.order_user_id),
                ("@shipmentQty", item.shipment_qty), ("@actualQty", item.actual_receipt_qty),
                ("@lossQty", item.loss_qty), ("@inboundQty", itemInboundQty),
                ("@erpStockId", erpStockId), ("@wmsSkuId", wmsSkuId),
                ("@wmsStockId", wmsStockId), ("@now", now), ("@tenantId", currentUser.tenant_id));
        }

        await SynchronizeSourceAsync(shipment, actualReceiptQty, lossQty, currentUser, now);

        await ExecuteAsync(
            """
            INSERT INTO trk_logistics_progress
                (logistics_info_id, event_type, from_status, to_status, event_time,
                 operator_id, operator_name, operator_role, event_remark,
                 creator, create_time, updater, update_time, deleted)
            VALUES
                (@shipmentId, 'RECEIPT_CONFIRMED', 'WAIT_RECEIPT', 'RECEIVED', @now,
                 @operatorId, @operatorName, @operatorRole, @remark,
                 @operatorName, @now, @operatorName, @now, b'0')
            """,
            ("@shipmentId", shipment.id), ("@now", now), ("@operatorId", currentUser.user_id),
            ("@operatorName", Truncate(currentUser.user_name, 64)),
            ("@operatorRole", Truncate(currentUser.user_role, 64)),
            ("@remark", $"ModernWMS确认签收入库，实收{actualReceiptQty}，损耗{lossQty}，入库{inboundQty}"));
    }

    private async Task UpdateShipmentReceiptAsync(
        ErpLogisticsInfoEntity shipment,
        ErpReceiptConfirmInputViewModel input,
        long actualReceiptQty,
        long lossQty,
        string freightPaymentStatus,
        CurrentUser currentUser,
        DateTime now)
    {
        var affected = await ExecuteAsync(
            """
            UPDATE trk_logistics_info
               SET lifecycle_status = 'RECEIVED', actual_receipt_qty = @actualQty,
                   receipt_time = @now, receipt_remark = @remark,
                   receipt_attachment_list = @receiptFiles,
                   receipt_freight_payment_status = @freightStatus,
                   receipt_freight_amount = @freightAmount,
                   receipt_freight_attachment_list = @freightFiles,
                   loss_qty = @lossQty, loss_reason = @lossReason,
                   loss_attachment_list = @lossFiles, last_sync_time = @now,
                   source_version = source_version + 1, updater = @operatorName, update_time = @now
             WHERE id = @shipmentId AND deleted = b'0'
               AND lifecycle_status = 'WAIT_RECEIPT' AND source_version = @sourceVersion
            """,
            ("@actualQty", actualReceiptQty), ("@now", now),
            ("@remark", input.receipt_remark?.Trim() ?? string.Empty),
            ("@receiptFiles", SerializeImages(input.receipt_files)),
            ("@freightStatus", freightPaymentStatus),
            ("@freightAmount", freightPaymentStatus == "PAY" ? input.receipt_freight_amount : null),
            ("@freightFiles", SerializeImages(freightPaymentStatus == "PAY" ? input.receipt_freight_files : [])),
            ("@lossQty", lossQty), ("@lossReason", lossQty > 0 ? input.loss_reason.Trim() : string.Empty),
            ("@lossFiles", SerializeImages(lossQty > 0 ? input.loss_files : [])),
            ("@operatorName", Truncate(currentUser.user_name, 64)),
            ("@shipmentId", shipment.id), ("@sourceVersion", input.source_version));
        if (affected != 1)
        {
            throw new InvalidOperationException("货件状态或版本已变化，签收入库已回滚");
        }
    }

    private async Task<long> PostErpStockAsync(
        ErpLogisticsInfoEntity shipment,
        ErpPendingReceiptProductViewModel product,
        long quantity,
        int itemIndex,
        CurrentUser currentUser,
        DateTime now)
    {
        var stockId = await ScalarAsync<long?>(
            """
            SELECT id FROM trk_stock
             WHERE deleted = b'0' AND stock_batch_no = 'POOL'
               AND warehouse_id = @warehouseId
               AND commodity_id <=> @commodityId AND commodity_sku <=> @sku
               AND dept_id <=> @deptId AND order_user_id <=> @orderUserId
               AND freight_forwarder_id <=> @forwarderId
             LIMIT 1 FOR UPDATE
            """,
            ("@warehouseId", shipment.to_warehouse_id), ("@commodityId", product.commodity_id),
            ("@sku", product.sku), ("@deptId", product.dept_id),
            ("@orderUserId", product.order_user_id), ("@forwarderId", shipment.freight_forwarder_id));

        if (stockId == null)
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

        var beforeQty = await ScalarAsync<long>(
            "SELECT available_qty FROM trk_stock WHERE id = @stockId FOR UPDATE", ("@stockId", stockId.Value));
        var afterQty = checked(beforeQty + quantity);
        await ExecuteAsync(
            "UPDATE trk_stock SET available_qty=@afterQty,total_qty=total_qty+@qty,updater=@name,update_time=@now WHERE id=@stockId",
            ("@afterQty", afterQty), ("@qty", quantity), ("@name", Truncate(currentUser.user_name, 64)),
            ("@now", now), ("@stockId", stockId.Value));
        await ExecuteAsync(
            """
            INSERT INTO trk_stock_record
                (record_no,biz_type,biz_id,biz_item_id,biz_no,stock_id,freight_forwarder_id,
                 warehouse_id,dept_id,order_user_id,commodity_id,commodity_sku,commodity_name,
                 change_qty,before_qty,after_qty,direction,operate_time,operator_id,operator_name,
                 remark,creator,create_time,updater,update_time,deleted)
            VALUES
                (@recordNo,'RECEIPT_IN',@shipmentId,@itemIndex,@bizNo,@stockId,@forwarderId,
                 @warehouseId,@deptId,@orderUserId,@commodityId,@sku,@name,@qty,@beforeQty,@afterQty,
                 'IN',@now,@operatorId,@operatorName,'ModernWMS确认签收入库',@operatorName,@now,
                 @operatorName,@now,b'0')
            """,
            ("@recordNo", $"MWMS-RI-{shipment.id}-{itemIndex}"), ("@shipmentId", shipment.id),
            ("@itemIndex", itemIndex), ("@bizNo", shipment.shipment_batch_no), ("@stockId", stockId.Value),
            ("@forwarderId", shipment.freight_forwarder_id), ("@warehouseId", shipment.to_warehouse_id),
            ("@deptId", product.dept_id), ("@orderUserId", product.order_user_id),
            ("@commodityId", product.commodity_id), ("@sku", product.sku), ("@name", product.product_name),
            ("@qty", quantity), ("@beforeQty", beforeQty), ("@afterQty", afterQty), ("@now", now),
            ("@operatorId", currentUser.user_id), ("@operatorName", Truncate(currentUser.user_name, 64)));
        return stockId.Value;
    }

    private async Task<int> EnsureReceiptLocationAsync(
        ErpLogisticsInfoEntity shipment,
        CurrentUser currentUser,
        DateTime now)
    {
        var warehouseTenantId = await ScalarAsync<long?>(
            "SELECT tenant_id FROM wms_warehouse WHERE erp_warehouse_id=@erpWarehouseId LIMIT 1 FOR UPDATE",
            ("@erpWarehouseId", shipment.to_warehouse_id));
        if (warehouseTenantId != null && warehouseTenantId != currentUser.tenant_id)
        {
            throw new UnauthorizedAccessException("当前用户无权操作该收货仓库");
        }
        if (warehouseTenantId != null)
        {
            var warehouseValid = await ScalarAsync<bool>(
                "SELECT is_valid FROM wms_warehouse WHERE erp_warehouse_id=@erpWarehouseId LIMIT 1",
                ("@erpWarehouseId", shipment.to_warehouse_id));
            if (!warehouseValid)
            {
                throw new InvalidOperationException("收货仓库已停用");
            }
        }
        var warehouseId = await ScalarAsync<int?>(
            "SELECT id FROM wms_warehouse WHERE erp_warehouse_id=@erpWarehouseId AND tenant_id=@tenantId AND is_valid=1 LIMIT 1 FOR UPDATE",
            ("@erpWarehouseId", shipment.to_warehouse_id), ("@tenantId", currentUser.tenant_id));
        if (warehouseId == null)
        {
            await ExecuteAsync(
                """
                INSERT INTO wms_warehouse
                    (warehouse_name,erp_warehouse_id,city,address,email,manager,contact_tel,creator,
                     create_time,last_update_time,is_valid,tenant_id)
                VALUES (@name,@erpId,'深圳','','','','',@creator,@now,@now,1,@tenantId)
                """,
                ("@name", shipment.to_warehouse_name ?? "有座山深圳仓"), ("@erpId", shipment.to_warehouse_id),
                ("@creator", currentUser.user_name), ("@now", now), ("@tenantId", currentUser.tenant_id));
            warehouseId = await ScalarAsync<int>("SELECT LAST_INSERT_ID()");
        }

        var areaId = await ScalarAsync<int?>(
            "SELECT id FROM wms_warehousearea WHERE warehouse_id=@warehouseId AND area_name='1.临时库区' AND is_valid=1 LIMIT 1 FOR UPDATE",
            ("@warehouseId", warehouseId.Value));
        if (areaId == null)
        {
            await ExecuteAsync(
                """
                INSERT INTO wms_warehousearea
                    (warehouse_id,area_name,parent_id,create_time,last_update_time,is_valid,tenant_id,area_property,sort)
                VALUES (@warehouseId,'1.临时库区',0,@now,@now,1,@tenantId,6,0)
                """,
                ("@warehouseId", warehouseId.Value), ("@now", now), ("@tenantId", currentUser.tenant_id));
            areaId = await ScalarAsync<int>("SELECT LAST_INSERT_ID()");
        }

        var locationId = await ScalarAsync<int?>(
            "SELECT id FROM wms_goodslocation WHERE warehouse_id=@warehouseId AND warehouse_area_id=@areaId AND location_name='收货暂存位' AND tenant_id=@tenantId LIMIT 1 FOR UPDATE",
            ("@warehouseId", warehouseId.Value), ("@areaId", areaId.Value), ("@tenantId", currentUser.tenant_id));
        if (locationId != null)
        {
            return locationId.Value;
        }

        await ExecuteAsync(
            """
            INSERT INTO wms_goodslocation
                (warehouse_id,warehouse_name,warehouse_area_name,warehouse_area_property,location_name,
                 location_length,location_width,location_heigth,location_volume,location_load,
                 roadway_number,shelf_number,layer_number,tag_number,create_time,last_update_time,
                 is_valid,tenant_id,warehouse_area_id)
            VALUES (@warehouseId,@warehouseName,'1.临时库区',6,'收货暂存位',0,0,0,0,0,
                    '','','','RECEIPT-TEMP',@now,@now,1,@tenantId,@areaId)
            """,
            ("@warehouseId", warehouseId.Value), ("@warehouseName", shipment.to_warehouse_name ?? "有座山深圳仓"),
            ("@now", now), ("@tenantId", currentUser.tenant_id), ("@areaId", areaId.Value));
        return await ScalarAsync<int>("SELECT LAST_INSERT_ID()");
    }

    private async Task<int> EnsureWmsSkuAsync(
        ErpPendingReceiptProductViewModel product,
        CurrentUser currentUser,
        DateTime now)
    {
        if (product.commodity_id == null)
        {
            throw new InvalidOperationException($"商品 {product.sku} 缺少 ERP 商品ID");
        }
        var commodity = await ReadCommodityAsync(product.commodity_id.Value)
            ?? throw new InvalidOperationException($"ERP 商品 {product.commodity_id} 不存在或已删除");
        var mappedSkuId = await ScalarAsync<int?>(
            "SELECT wms_sku_id FROM wms_erp_commodity_map WHERE tenant_id=@tenantId AND erp_commodity_id=@commodityId LIMIT 1 FOR UPDATE",
            ("@tenantId", currentUser.tenant_id), ("@commodityId", product.commodity_id));
        if (mappedSkuId != null)
        {
            await ExecuteAsync(
                """
                UPDATE wms_sku s
                JOIN wms_erp_commodity_map m ON m.wms_sku_id=s.id
                   SET s.sku_code=@sku,s.sku_name=@name,s.weight=@weight,s.lenght=@length,
                       s.width=@width,s.height=@height,s.volume=@volume,s.unit=@unit,
                       s.cost=@cost,s.price=@cost,s.last_update_time=@now,
                       m.commodity_sku=@sku,m.last_sync_time=@now
                 WHERE m.tenant_id=@tenantId AND m.erp_commodity_id=@commodityId
                """,
                ("@sku", commodity.Sku), ("@name", commodity.Name), ("@weight", commodity.Weight),
                ("@length", commodity.Length), ("@width", commodity.Width), ("@height", commodity.Height),
                ("@volume", commodity.Length * commodity.Width * commodity.Height),
                ("@unit", commodity.Unit), ("@cost", commodity.Cost), ("@now", now),
                ("@tenantId", currentUser.tenant_id), ("@commodityId", product.commodity_id));
            return mappedSkuId.Value;
        }

        var spuCode = string.IsNullOrWhiteSpace(commodity.Spu) ? commodity.Sku : commodity.Spu;
        await ExecuteAsync(
            """
            INSERT INTO wms_spu
                (spu_code,spu_name,spu_description,supplier_id,supplier_name,brand,origin,
                 length_unit,volume_unit,weight_unit,creator,create_time,last_update_time,is_valid,tenant_id)
            VALUES (@code,@name,'',0,'','','',0,0,0,@creator,@now,@now,1,@tenantId)
            """,
            ("@code", spuCode), ("@name", string.IsNullOrWhiteSpace(commodity.SpuName) ? commodity.Name : commodity.SpuName),
            ("@creator", currentUser.user_name),
            ("@now", now), ("@tenantId", currentUser.tenant_id));
        var spuId = await ScalarAsync<int>("SELECT LAST_INSERT_ID()");
        await ExecuteAsync(
            """
            INSERT INTO wms_sku
                (spu_id,sku_code,sku_name,bar_code,weight,lenght,width,height,volume,unit,cost,price,
                 create_time,last_update_time)
            VALUES (@spuId,@sku,@name,'',@weight,@length,@width,@height,@volume,@unit,@cost,@cost,@now,@now)
            """,
            ("@spuId", spuId), ("@sku", commodity.Sku), ("@name", commodity.Name),
            ("@weight", commodity.Weight), ("@length", commodity.Length), ("@width", commodity.Width),
            ("@height", commodity.Height), ("@volume", commodity.Length * commodity.Width * commodity.Height),
            ("@unit", commodity.Unit), ("@cost", commodity.Cost), ("@now", now));
        var skuId = await ScalarAsync<int>("SELECT LAST_INSERT_ID()");
        await ExecuteAsync(
            """
            INSERT INTO wms_erp_commodity_map
                (erp_commodity_id,wms_spu_id,wms_sku_id,commodity_sku,last_sync_time,tenant_id)
            VALUES (@commodityId,@spuId,@skuId,@sku,@now,@tenantId)
            """,
            ("@commodityId", product.commodity_id), ("@spuId", spuId), ("@skuId", skuId),
            ("@sku", commodity.Sku), ("@now", now), ("@tenantId", currentUser.tenant_id));
        return skuId;
    }

    private async Task<CommoditySnapshot?> ReadCommodityAsync(long commodityId)
    {
        await using var command = CreateCommand(
            """
            SELECT COALESCE(sku,''),COALESCE(name,''),COALESCE(spu,''),COALESCE(spu_name,''),
                   COALESCE(weight,0),COALESCE(length,0),COALESCE(width,0),COALESCE(height,0),
                   COALESCE(unit,'件'),COALESCE(purchase_cost,0)
              FROM erp_commodity
             WHERE id=@id AND deleted=b'0'
             LIMIT 1
            """,
            ("@id", commodityId.ToString()));
        await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleRow);
        if (!await reader.ReadAsync())
        {
            return null;
        }
        return new CommoditySnapshot(
            reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
            reader.GetDecimal(4), reader.GetDecimal(5), reader.GetDecimal(6), reader.GetDecimal(7),
            reader.GetString(8), reader.GetDecimal(9));
    }

    private async Task<int> EnsureGoodsOwnerAsync(
        ErpPendingReceiptProductViewModel product,
        CurrentUser currentUser,
        DateTime now)
    {
        var deptId = product.dept_id ?? 0;
        var userId = product.order_user_id ?? 0;
        var mappedOwnerId = await ScalarAsync<int?>(
            "SELECT wms_goods_owner_id FROM wms_erp_goods_owner_map WHERE tenant_id=@tenantId AND erp_dept_id=@deptId AND erp_order_user_id=@userId LIMIT 1 FOR UPDATE",
            ("@tenantId", currentUser.tenant_id), ("@deptId", deptId), ("@userId", userId));
        if (mappedOwnerId != null)
        {
            return mappedOwnerId.Value;
        }
        var ownerName = string.Join(" / ", new[] { product.dept_name, product.order_user_name }
            .Where(t => !string.IsNullOrWhiteSpace(t)));
        if (string.IsNullOrWhiteSpace(ownerName))
        {
            ownerName = $"ERP货主 {deptId}-{userId}";
        }
        await ExecuteAsync(
            """
            INSERT INTO wms_goodsowner
                (goods_owner_name,city,address,manager,contact_tel,creator,create_time,last_update_time,is_valid,tenant_id)
            VALUES (@name,'','','','',@creator,@now,@now,1,@tenantId)
            """,
            ("@name", ownerName), ("@creator", currentUser.user_name), ("@now", now),
            ("@tenantId", currentUser.tenant_id));
        var ownerId = await ScalarAsync<int>("SELECT LAST_INSERT_ID()");
        await ExecuteAsync(
            """
            INSERT INTO wms_erp_goods_owner_map
                (erp_dept_id,erp_order_user_id,wms_goods_owner_id,dept_name,order_user_name,last_sync_time,tenant_id)
            VALUES (@deptId,@userId,@ownerId,@deptName,@userName,@now,@tenantId)
            """,
            ("@deptId", deptId), ("@userId", userId), ("@ownerId", ownerId),
            ("@deptName", product.dept_name), ("@userName", product.order_user_name),
            ("@now", now), ("@tenantId", currentUser.tenant_id));
        return ownerId;
    }

    private async Task<int> PostWmsStockAsync(
        long shipmentId,
        int itemIndex,
        int skuId,
        int locationId,
        int ownerId,
        long quantity,
        CurrentUser currentUser,
        DateTime now)
    {
        if (quantity > int.MaxValue)
        {
            throw new InvalidOperationException("单商品入库数量超过 WMS 库存字段上限");
        }
        var stockPrice = await ScalarAsync<decimal>("SELECT cost FROM wms_sku WHERE id=@skuId", ("@skuId", skuId));
        var stockId = await ScalarAsync<int?>(
            """
            SELECT id FROM wms_stock
             WHERE sku_id=@skuId AND goods_location_id=@locationId AND goods_owner_id=@ownerId
               AND tenant_id=@tenantId AND is_freeze=0 AND series_number=''
               AND expiry_date='9999-12-31 00:00:00' AND price=@price
             LIMIT 1 FOR UPDATE
            """,
            ("@skuId", skuId), ("@locationId", locationId), ("@ownerId", ownerId),
            ("@tenantId", currentUser.tenant_id), ("@price", stockPrice));
        var beforeQty = 0L;
        if (stockId == null)
        {
            await ExecuteAsync(
                """
                INSERT INTO wms_stock
                    (sku_id,goods_location_id,qty,goods_owner_id,is_freeze,last_update_time,tenant_id,
                     series_number,expiry_date,price,putaway_date)
                VALUES (@skuId,@locationId,@qty,@ownerId,0,@now,@tenantId,'','9999-12-31',@price,@putawayDate)
                """,
                ("@skuId", skuId), ("@locationId", locationId), ("@qty", (int)quantity),
                ("@ownerId", ownerId), ("@now", now), ("@tenantId", currentUser.tenant_id),
                ("@price", stockPrice), ("@putawayDate", now.Date));
            stockId = await ScalarAsync<int>("SELECT LAST_INSERT_ID()");
        }
        else
        {
            beforeQty = await ScalarAsync<int>("SELECT qty FROM wms_stock WHERE id=@id FOR UPDATE", ("@id", stockId.Value));
            if (beforeQty + quantity > int.MaxValue)
            {
                throw new InvalidOperationException("WMS 库存累计数量超过字段上限");
            }
            await ExecuteAsync(
                "UPDATE wms_stock SET qty=@qty,last_update_time=@now WHERE id=@id",
                ("@qty", (int)(beforeQty + quantity)), ("@now", now), ("@id", stockId.Value));
        }
        await ExecuteAsync(
            """
            INSERT INTO wms_stock_record
                (record_no,biz_type,biz_id,biz_item_id,stock_id,sku_id,goods_location_id,goods_owner_id,
                 change_qty,before_qty,after_qty,direction,operator_id,operator_name,remark,operate_time,tenant_id)
            VALUES (@recordNo,'RECEIPT_IN',@shipmentId,@itemIndex,@stockId,@skuId,@locationId,@ownerId,
                    @qty,@beforeQty,@afterQty,'IN',@operatorId,@operatorName,'确认签收入库',@now,@tenantId)
            """,
            ("@recordNo", $"MWMS-RI-{shipmentId}-{itemIndex}"), ("@shipmentId", shipmentId),
            ("@itemIndex", itemIndex), ("@stockId", stockId.Value), ("@skuId", skuId),
            ("@locationId", locationId), ("@ownerId", ownerId), ("@qty", quantity),
            ("@beforeQty", beforeQty), ("@afterQty", beforeQty + quantity),
            ("@operatorId", currentUser.user_id), ("@operatorName", Truncate(currentUser.user_name, 128)),
            ("@now", now), ("@tenantId", currentUser.tenant_id));
        return stockId.Value;
    }
    private async Task SynchronizeSourceAsync(
        ErpLogisticsInfoEntity shipment,
        long actualReceiptQty,
        long lossQty,
        CurrentUser currentUser,
        DateTime now)
    {
        if (string.Equals(shipment.source_type, PurchaseTaskSourceType, StringComparison.OrdinalIgnoreCase)
            && shipment.source_task_id != null)
        {
            var targetReceiptQty = actualReceiptQty;
            var existedReceiptQty = await ScalarAsync<long>(
                """
                SELECT COALESCE(SUM(receipt_qty),0) FROM erp_purchase_task_receipt_record
                 WHERE shipment_batch_id=@batchId AND deleted=b'0'
                """,
                ("@batchId", shipment.source_shipment_batch_id));
            var receiptRecordChanged = targetReceiptQty > existedReceiptQty;
            if (receiptRecordChanged)
            {
                var diffQtyForRecord = Math.Max((shipment.shipment_qty ?? 0) - targetReceiptQty, 0);
                await ExecuteAsync(
                    """
                    INSERT INTO erp_purchase_task_receipt_record
                        (task_id,shipment_batch_id,receipt_type,receipt_qty,receipt_time,diff_qty,diff_reason,
                         confirmed_by_id,confirmed_by_name,confirmed_role,confirmed_time,remark,
                         creator,create_time,updater,update_time,deleted)
                    VALUES (@taskId,@batchId,@receiptType,@receiptQty,@now,@diffQty,@diffReason,
                            @operatorId,@operatorName,@operatorRole,@now,'ModernWMS确认签收入库',
                            @operatorName,@now,@operatorName,@now,b'0')
                    """,
                    ("@taskId", shipment.source_task_id), ("@batchId", shipment.source_shipment_batch_id),
                    ("@receiptType", string.IsNullOrWhiteSpace(shipment.shipment_type)
                        ? (object)DBNull.Value : shipment.shipment_type.Trim()),
                    ("@receiptQty", targetReceiptQty - existedReceiptQty), ("@now", now),
                    ("@diffQty", diffQtyForRecord),
                    ("@diffReason", diffQtyForRecord > 0 ? "统一待收货确认短收" : (object)DBNull.Value),
                    ("@operatorId", currentUser.user_id), ("@operatorName", Truncate(currentUser.user_name, 64)),
                    ("@operatorRole", Truncate(currentUser.user_role, 64)));
            }

            var shipmentQty = shipment.shipment_qty ?? 0;
            // ERP regards the recorded shortfall as a resolved receipt difference, so the batch is complete.
            var targetBatchStatus = StatusAllSigned;
            var batchChanged = await ExecuteAsync(
                "UPDATE erp_purchase_task_shipment_batch SET status=@status,updater=@name,update_time=@now WHERE id=@id AND deleted=b'0' AND status <> @status",
                ("@status", targetBatchStatus), ("@name", Truncate(currentUser.user_name, 64)),
                ("@now", now), ("@id", shipment.source_shipment_batch_id)) > 0;

            var (taskChanged, fromAction, toAction) =
                await RefreshPurchaseTaskAsync(shipment.source_task_id.Value, currentUser, now);

            if (receiptRecordChanged || batchChanged || taskChanged)
            {
                var diffQty = Math.Max(shipmentQty - targetReceiptQty, 0);
                var payload = new Dictionary<string, object?>
                {
                    ["logisticsInfoId"] = shipment.id,
                    ["shipmentBatchId"] = shipment.source_shipment_batch_id,
                    ["shipmentBatchNo"] = shipment.shipment_batch_no,
                    ["shipmentType"] = shipment.shipment_type,
                    ["shipmentQty"] = shipmentQty,
                    ["actualReceiptQty"] = targetReceiptQty,
                    ["receiptQty"] = targetReceiptQty - existedReceiptQty,
                    ["diffQty"] = diffQty,
                    ["receiptRemark"] = shipment.receipt_remark ?? string.Empty,
                    ["receiptTime"] = now,
                    ["operatorRole"] = currentUser.user_role,
                    ["receiptRecordChanged"] = receiptRecordChanged,
                    ["shipmentBatchChanged"] = batchChanged,
                    ["taskLifecycleChanged"] = taskChanged,
                    ["fromAction"] = fromAction,
                    ["toAction"] = toAction
                };
                var remark = "物流待收货确认入库：批次"
                    + (string.IsNullOrWhiteSpace(shipment.shipment_batch_no) ? "-" : shipment.shipment_batch_no)
                    + "，发货" + shipmentQty
                    + "，实收" + targetReceiptQty
                    + (diffQty > 0 ? "，短少" + diffQty : "");
                await WritePurchaseActionLogAsync(
                    shipment.source_task_id.Value,
                    fromAction,
                    toAction,
                    remark,
                    payload,
                    currentUser,
                    now);
            }
        }
        else if (string.Equals(shipment.source_type, "STOCK_DISPATCH", StringComparison.OrdinalIgnoreCase)
                 && shipment.source_stock_move_id != null)
        {
            var moveStatus = await ScalarAsync<string?>(
                "SELECT status FROM trk_stock_move WHERE id=@moveId AND deleted=b'0' FOR UPDATE",
                ("@moveId", shipment.source_stock_move_id));
            if (!string.Equals(moveStatus, "WAIT_RECEIPT", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("调度来源不是待收货状态，签收入库已回滚");
            }
            var invalidCount = await ScalarAsync<long>(
                """
                SELECT COUNT(*)
                  FROM (SELECT COALESCE(i.from_stock_id,i.stock_id) stock_id,SUM(i.qty) qty
                          FROM trk_stock_move_item i
                         WHERE i.stock_move_id=@moveId AND i.deleted=b'0'
                           AND NOT EXISTS (SELECT 1 FROM trk_stock_record r
                                            WHERE r.biz_type='DISPATCH_SHIP_OUT' AND r.biz_id=i.stock_move_id
                                              AND r.biz_item_id=i.id AND r.stock_id=COALESCE(i.from_stock_id,i.stock_id)
                                              AND r.deleted=b'0')
                         GROUP BY COALESCE(i.from_stock_id,i.stock_id)) i
                  JOIN trk_stock s ON s.id=i.stock_id
                 WHERE s.deleted=b'1' OR s.occupied_qty<i.qty OR s.total_qty<i.qty
                """,
                ("@moveId", shipment.source_stock_move_id));
            if (invalidCount > 0)
            {
                throw new InvalidOperationException("调度来源冻结库存不足，签收入库已回滚");
            }
            await ExecuteAsync(
                """
                INSERT INTO trk_stock_record
                    (record_no,biz_type,biz_id,biz_item_id,biz_no,stock_id,freight_forwarder_id,
                     warehouse_id,dept_id,order_user_id,commodity_id,commodity_sku,commodity_name,
                     change_qty,before_qty,after_qty,direction,operate_time,operator_id,operator_name,
                     remark,creator,create_time,updater,update_time,deleted)
                SELECT CONCAT('MWMS-DSO-',m.id,'-',i.id),'DISPATCH_SHIP_OUT',m.id,i.id,m.no,s.id,
                       s.freight_forwarder_id,s.warehouse_id,s.dept_id,s.order_user_id,s.commodity_id,
                       s.commodity_sku,s.commodity_name,-i.qty,s.total_qty,s.total_qty-i.qty,'OUT',@now,
                       @operatorId,@operatorName,'目标仓物理收货，结转来源冻结库存',@operatorName,@now,
                       @operatorName,@now,b'0'
                  FROM trk_stock_move m
                  JOIN trk_stock_move_item i ON i.stock_move_id=m.id AND i.deleted=b'0'
                  JOIN trk_stock s ON s.id=COALESCE(i.from_stock_id,i.stock_id) AND s.deleted=b'0'
                 WHERE m.id=@moveId AND m.deleted=b'0'
                   AND NOT EXISTS (SELECT 1 FROM trk_stock_record r
                                    WHERE r.biz_type='DISPATCH_SHIP_OUT' AND r.biz_id=m.id
                                      AND r.biz_item_id=i.id AND r.stock_id=s.id AND r.deleted=b'0')
                """,
                ("@now", now), ("@operatorId", currentUser.user_id),
                ("@operatorName", Truncate(currentUser.user_name, 64)),
                ("@moveId", shipment.source_stock_move_id));
            await ExecuteAsync(
                """
                UPDATE trk_stock s
                JOIN (SELECT COALESCE(i.from_stock_id,i.stock_id) stock_id,SUM(i.qty) qty
                        FROM trk_stock_move_item i
                       WHERE i.stock_move_id=@moveId AND i.deleted=b'0'
                         AND NOT EXISTS (SELECT 1 FROM trk_stock_record r
                                          WHERE r.biz_type='DISPATCH_SHIP_OUT' AND r.biz_id=i.stock_move_id
                                            AND r.biz_item_id=i.id AND r.stock_id=COALESCE(i.from_stock_id,i.stock_id)
                                            AND r.deleted=b'0' AND r.record_no NOT LIKE 'MWMS-DSO-%')
                       GROUP BY COALESCE(i.from_stock_id,i.stock_id)) i ON i.stock_id=s.id
                   SET s.occupied_qty=s.occupied_qty-i.qty,s.total_qty=s.total_qty-i.qty,
                       s.updater=@name,s.update_time=@now
                 WHERE s.deleted=b'0'
                """,
                ("@name", Truncate(currentUser.user_name, 64)), ("@now", now),
                ("@moveId", shipment.source_stock_move_id));
            await ExecuteAsync(
                "UPDATE trk_stock_move_item SET occupied_qty=0,updater=@name,update_time=@now WHERE stock_move_id=@moveId AND deleted=b'0'",
                ("@name", Truncate(currentUser.user_name, 64)), ("@now", now),
                ("@moveId", shipment.source_stock_move_id));
            var moveAffected = await ExecuteAsync(
                "UPDATE trk_stock_move SET status='COMPLETED',shipment_status='COMPLETED',shipment_status_time=@now,updater=@name,update_time=@now WHERE id=@id AND status='WAIT_RECEIPT' AND deleted=b'0'",
                ("@name", Truncate(currentUser.user_name, 64)), ("@now", now),
                ("@id", shipment.source_stock_move_id));
            if (moveAffected != 1)
            {
                throw new InvalidOperationException("调度来源状态已变化，签收入库已回滚");
            }
        }
    }
    private DbCommand CreateCommand(string sql, params (string Name, object? Value)[] parameters)
    {
        var command = _ruoyiDbContext.Database.GetDbConnection().CreateCommand();
        command.CommandText = sql;
        command.Transaction = _ruoyiDbContext.Database.CurrentTransaction?.GetDbTransaction();
        foreach (var (name, value) in parameters)
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = name;
            parameter.Value = value ?? DBNull.Value;
            command.Parameters.Add(parameter);
        }
        return command;
    }

    private async Task<int> ExecuteAsync(string sql, params (string Name, object? Value)[] parameters)
    {
        await using var command = CreateCommand(sql, parameters);
        return await command.ExecuteNonQueryAsync();
    }

    private async Task<T> ScalarAsync<T>(string sql, params (string Name, object? Value)[] parameters)
    {
        await using var command = CreateCommand(sql, parameters);
        var value = await command.ExecuteScalarAsync();
        if (value == null || value == DBNull.Value)
        {
            return default!;
        }
        var targetType = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);
        return (T)Convert.ChangeType(value, targetType);
    }

    private sealed record CommoditySnapshot(
        string Sku,
        string Name,
        string Spu,
        string SpuName,
        decimal Weight,
        decimal Length,
        decimal Width,
        decimal Height,
        string Unit,
        decimal Cost);
}
