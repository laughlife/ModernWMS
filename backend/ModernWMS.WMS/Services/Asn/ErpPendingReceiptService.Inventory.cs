using System.Data;
using System.Data.Common;
using Dapper;
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
        shipment.loss_qty = lossQty;
        shipment.loss_reason = lossQty > 0 ? input.loss_reason.Trim() : string.Empty;
        var inputItems = input.items.ToDictionary(t => t.source_item_key, StringComparer.Ordinal);

        // Establish one deterministic stock-lock order before resolving any location allocation.
        await LockWarehousePoolStocksAsync(shipment.to_warehouse_id!.Value);

        for (var index = 0; index < products.Count; index++)
        {
            var product = products[index];
            var item = inputItems[product.source_item_key];
            var itemInboundQty = checked(item.actual_receipt_qty - item.loss_qty);
            long? erpStockId = null;
            long? primaryStockAllocationId = null;
            var wmsSkuId = await EnsureWmsSkuAsync(product, currentUser, now);
            var allocations = await BuildReceiptAllocationsAsync(
                shipment,
                product,
                item,
                itemInboundQty,
                currentUser,
                now);
            var primaryArea = allocations.FirstOrDefault();

            if (itemInboundQty > 0)
            {
                var posting = await PostErpStockAsync(
                    shipment,
                    product,
                    itemInboundQty,
                    index + 1,
                    currentUser,
                    now);
                erpStockId = posting.StockId;
                await LockStockAllocationsInIdOrderAsync(erpStockId.Value, currentUser.tenant_id);
                var erpStockRecordId = await WriteErpStockReceiptRecordAsync(
                    posting,
                    shipment,
                    product,
                    itemInboundQty,
                    index + 1,
                    currentUser,
                    now);
                for (var allocIndex = 0; allocIndex < allocations.Count; allocIndex++)
                {
                    var allocation = allocations[allocIndex];
                    var stockAllocationId = await PostStockAllocationAsync(
                        posting,
                        erpStockRecordId,
                        shipment,
                        index + 1,
                        wmsSkuId,
                        allocation,
                        currentUser,
                        now);
                    if (allocIndex == 0)
                    {
                        primaryStockAllocationId = stockAllocationId;
                    }
                }
                await EnsureStockAllocationInvariantAsync(erpStockId.Value, currentUser.tenant_id);
            }

            await ExecuteAsync(
                """
                INSERT INTO wms_erp_receipt_item
                    (receipt_id, shipment_id, source_item_key, task_item_id, allocation_id,
                     commodity_id, commodity_sku, commodity_name, dept_id, order_user_id,
                     dept_name, order_user_name, warehouse_area_id, warehouse_area_name,
                     shipment_qty, actual_receipt_qty, loss_qty, inbound_qty, erp_stock_id,
                     wms_sku_id, wms_stock_id, primary_stock_allocation_id,
                     receipt_time, total_weight, total_volume,
                     create_time, tenant_id)
                VALUES
                    (@receiptId, @shipmentId, @sourceItemKey, @taskItemId, @allocationId,
                     @commodityId, @sku, @name, @deptId, @orderUserId,
                     @deptName, @orderUserName, @areaId, @areaName,
                     @shipmentQty, @actualQty, @lossQty, @inboundQty, @erpStockId,
                     @wmsSkuId, NULL, @primaryStockAllocationId,
                     @now, NULL, NULL, @now, @tenantId)
                """,
                ("@receiptId", receiptId), ("@shipmentId", shipment.id),
                ("@sourceItemKey", product.source_item_key), ("@taskItemId", product.task_item_id),
                ("@allocationId", product.allocation_id), ("@commodityId", product.commodity_id),
                ("@sku", product.sku), ("@name", product.product_name),
                ("@deptId", product.dept_id), ("@orderUserId", product.order_user_id),
                ("@deptName", Truncate(primaryArea?.OperatorGroupName ?? product.dept_name, 128)),
                ("@orderUserName", Truncate(product.order_user_name, 128)),
                ("@areaId", primaryArea?.AreaId ?? 0), ("@areaName", Truncate(primaryArea?.AreaName, 128)),
                ("@shipmentQty", item.shipment_qty), ("@actualQty", item.actual_receipt_qty),
                ("@lossQty", item.loss_qty), ("@inboundQty", itemInboundQty),
                ("@erpStockId", erpStockId), ("@wmsSkuId", wmsSkuId),
                ("@primaryStockAllocationId", primaryStockAllocationId),
                ("@now", now), ("@tenantId", currentUser.tenant_id));

            if (allocations.Count > 0)
            {
                var receiptItemId = await ScalarAsync<int>("SELECT LAST_INSERT_ID()");
                foreach (var allocation in allocations.Where(t => t.Qty > 0))
                {
                    await ExecuteAsync(
                        """
                        INSERT INTO wms_receipt_item_owner
                            (receipt_item_id, warehouse_area_id, warehouse_area_name,
                             goods_owner_id, goods_owner_name, qty, create_time, tenant_id)
                        VALUES
                            (@receiptItemId, @areaId, @areaName,
                             @ownerId, @ownerName, @qty, @now, @tenantId)
                        """,
                        ("@receiptItemId", receiptItemId),
                        ("@areaId", allocation.AreaId), ("@areaName", Truncate(allocation.AreaName, 128)),
                        ("@ownerId", allocation.GoodsOwnerId), ("@ownerName", Truncate(allocation.GoodsOwnerName, 255)),
                        ("@qty", allocation.Qty), ("@now", now),
                        ("@tenantId", currentUser.tenant_id));
                }
            }
        }

        await SynchronizeSourceAsync(shipment, actualReceiptQty, lossQty, inboundQty, currentUser, now);

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
            ("@operatorName", ShenzhenSelfWarehouseSigner),
            ("@operatorRole", "SELF_WAREHOUSE"),
            ("@remark", BuildReceiptAccountingRemark(
                shipment.shipment_qty ?? 0,
                actualReceiptQty,
                lossQty,
                inboundQty,
                lossQty > 0 ? shipment.loss_reason : null)));
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

    private async Task<ReceiptAreaLocation> EnsureReceiptAreaLocationAsync(
        ErpLogisticsInfoEntity shipment,
        ErpPendingReceiptProductViewModel product,
        CurrentUser currentUser,
        DateTime now,
        int? explicitAreaId = null)
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
            throw new InvalidOperationException($"收货仓库 {shipment.to_warehouse_name} 尚未映射到WMS仓库");
        }

        int areaId;
        if (explicitAreaId != null)
        {
            var areaValid = await ScalarAsync<bool?>(
                "SELECT is_valid FROM wms_warehousearea WHERE id=@areaId AND warehouse_id=@warehouseId AND tenant_id=@tenantId LIMIT 1",
                ("@areaId", explicitAreaId.Value), ("@warehouseId", warehouseId.Value),
                ("@tenantId", currentUser.tenant_id));
            if (areaValid != true)
            {
                throw new InvalidOperationException("所选库区无效或不属于当前收货仓库");
            }
            areaId = explicitAreaId.Value;
        }
        else
        {
            var resolvedArea = await ResolveDefaultAreaAsync(warehouseId.Value, product.dept_id, currentUser);
            if (resolvedArea == null)
            {
                var deptLabel = string.IsNullOrWhiteSpace(product.dept_name)
                    ? product.dept_id?.ToString() ?? "未知小组"
                    : product.dept_name;
                throw new InvalidOperationException($"商品 {product.sku} 所属小组 {deptLabel} 未找到可用库区");
            }
            areaId = resolvedArea.Id;
        }

        var areaName = await ScalarAsync<string>(
            "SELECT area_name FROM wms_warehousearea WHERE id=@areaId",
            ("@areaId", areaId));
        var areaProperty = await ScalarAsync<byte>(
            "SELECT area_property FROM wms_warehousearea WHERE id=@areaId",
            ("@areaId", areaId));
        var operatorGroupName = await ScalarAsync<string>(
            """
            WITH RECURSIVE dept_chain AS
            (
                SELECT id,parent_id,name,0 AS depth
                  FROM system_dept
                 WHERE id=@deptId AND deleted=b'0' AND status=0
                UNION ALL
                SELECT parent.id,parent.parent_id,parent.name,child.depth+1
                  FROM system_dept parent
                  JOIN dept_chain child ON child.parent_id=parent.id
                 WHERE parent.deleted=b'0' AND parent.status=0 AND child.depth < 20
            )
            SELECT chain.name
              FROM dept_chain chain
              JOIN wms_warehousearea_operator_group binding
                ON binding.dept_id=chain.id AND binding.tenant_id=@tenantId
             WHERE binding.warehouse_area_id=@areaId
             ORDER BY chain.depth
             LIMIT 1
            """,
            ("@deptId", product.dept_id), ("@tenantId", currentUser.tenant_id),
            ("@areaId", areaId));
        var autoLocationTag = $"AREA-AUTO-{areaId}";

        var locationId = await ScalarAsync<int?>(
            "SELECT id FROM wms_goodslocation WHERE warehouse_id=@warehouseId AND warehouse_area_id=@areaId AND tag_number=@tag AND tenant_id=@tenantId AND is_valid=1 LIMIT 1 FOR UPDATE",
            ("@warehouseId", warehouseId.Value), ("@areaId", areaId),
            ("@tag", autoLocationTag), ("@tenantId", currentUser.tenant_id));
        if (locationId != null)
        {
            return new ReceiptAreaLocation(locationId.Value, areaId, areaName, operatorGroupName);
        }

        await ExecuteAsync(
            """
            INSERT INTO wms_goodslocation
                (warehouse_id,warehouse_name,warehouse_area_name,warehouse_area_property,location_name,
                 location_length,location_width,location_heigth,location_volume,location_load,
                 roadway_number,shelf_number,layer_number,tag_number,create_time,last_update_time,
                 is_valid,tenant_id,warehouse_area_id)
            VALUES (@warehouseId,@warehouseName,@areaName,@areaProperty,@areaName,0,0,0,0,0,
                    '','','',@tag,@now,@now,1,@tenantId,@areaId)
            """,
            ("@warehouseId", warehouseId.Value), ("@warehouseName", shipment.to_warehouse_name ?? "有座山深圳仓"),
            ("@areaName", areaName), ("@areaProperty", areaProperty), ("@tag", autoLocationTag),
            ("@now", now), ("@tenantId", currentUser.tenant_id), ("@areaId", areaId));
        var createdLocationId = await ScalarAsync<int>("SELECT LAST_INSERT_ID()");
        return new ReceiptAreaLocation(createdLocationId, areaId, areaName, operatorGroupName);
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
        await using var connectionLease = await OpenConnectionLeaseAsync();
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

    private async Task SynchronizeSourceAsync(
        ErpLogisticsInfoEntity shipment,
        long actualReceiptQty,
        long lossQty,
        long inboundQty,
        CurrentUser currentUser,
        DateTime now)
    {
        if (string.Equals(shipment.source_type, PurchaseTaskSourceType, StringComparison.OrdinalIgnoreCase)
            && shipment.source_task_id != null)
        {
            // ERP receipt quantity must match the quantity posted to the canonical ERP balance.
            var targetReceiptQty = inboundQty;
            var existedReceiptQty = await ScalarAsync<long>(
                """
                SELECT COALESCE(SUM(receipt_qty),0) FROM erp_purchase_task_receipt_record
                 WHERE shipment_batch_id=@batchId AND deleted=b'0'
                """,
                ("@batchId", shipment.source_shipment_batch_id));
            var existedReceiptRecordCount = await ScalarAsync<long>(
                "SELECT COUNT(*) FROM erp_purchase_task_receipt_record WHERE shipment_batch_id=@batchId AND deleted=b'0'",
                ("@batchId", shipment.source_shipment_batch_id));
            var receiptQtyDelta = Math.Max(targetReceiptQty - existedReceiptQty, 0);
            var receiptRecordChanged = receiptQtyDelta > 0 || existedReceiptRecordCount == 0;
            if (receiptRecordChanged)
            {
                var shipmentQtyForRecord = shipment.shipment_qty ?? 0;
                var diffQtyForRecord = Math.Max(shipmentQtyForRecord - targetReceiptQty, 0);
                var accountingRemark = BuildReceiptAccountingRemark(
                    shipmentQtyForRecord,
                    actualReceiptQty,
                    lossQty,
                    inboundQty,
                    lossQty > 0 ? shipment.loss_reason : null);
                var receiptRecordRemark = Truncate(accountingRemark, 512);
                await ExecuteAsync(
                    """
                    INSERT INTO erp_purchase_task_receipt_record
                        (task_id,shipment_batch_id,receipt_type,receipt_qty,receipt_time,diff_qty,diff_reason,
                         confirmed_by_id,confirmed_by_name,confirmed_role,confirmed_time,remark,
                         creator,create_time,updater,update_time,deleted)
                    VALUES (@taskId,@batchId,@receiptType,@receiptQty,@now,@diffQty,@diffReason,
                            @operatorId,@signerName,@signerRole,@now,@remark,
                            @creator,@now,@creator,@now,b'0')
                    """,
                    ("@taskId", shipment.source_task_id), ("@batchId", shipment.source_shipment_batch_id),
                    ("@receiptType", string.IsNullOrWhiteSpace(shipment.shipment_type)
                        ? (object)DBNull.Value : shipment.shipment_type.Trim()),
                    ("@receiptQty", receiptQtyDelta), ("@now", now),
                    ("@diffQty", diffQtyForRecord),
                    ("@diffReason", diffQtyForRecord > 0 ? receiptRecordRemark : (object)DBNull.Value),
                    ("@operatorId", currentUser.user_id), ("@signerName", ShenzhenSelfWarehouseSigner),
                    ("@signerRole", "SELF_WAREHOUSE"), ("@remark", receiptRecordRemark),
                    ("@creator", Truncate(currentUser.user_name, 64)));
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
                    ["actualReceiptQty"] = actualReceiptQty,
                    ["lossQty"] = lossQty,
                    ["inboundQty"] = inboundQty,
                    ["receiptQty"] = receiptQtyDelta,
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
                var remark = BuildReceiptAccountingRemark(
                    shipmentQty,
                    actualReceiptQty,
                    lossQty,
                    inboundQty,
                    lossQty > 0 ? shipment.loss_reason : null);
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
            throw new InvalidOperationException(
                "调度收货必须先完成来源allocation扣减迁移；当前禁止绕过统一库存Mutation直接结转ERP库存");
        }
    }

    private static string BuildReceiptAccountingRemark(
        long shipmentQty,
        long actualReceiptQty,
        long lossQty,
        long inboundQty,
        string? lossReason)
    {
        var remark = $"签收人：{ShenzhenSelfWarehouseSigner}；发货{shipmentQty}，实收{actualReceiptQty}，损耗{lossQty}，实际入库{inboundQty}";
        return lossQty > 0 && !string.IsNullOrWhiteSpace(lossReason)
            ? $"{remark}；损耗原因：{lossReason.Trim()}"
            : remark;
    }

    private sealed record ReceiptAreaLocation(
        int LocationId,
        int AreaId,
        string AreaName,
        string OperatorGroupName);

    private sealed record ReceiptAllocation(
        int LocationId,
        int AreaId,
        string AreaName,
        string OperatorGroupName,
        int GoodsOwnerId,
        string GoodsOwnerName,
        long Qty);

    private sealed record AreaReference(int Id, string Name);

    private static DynamicParameters CreateParameters(params (string Name, object? Value)[] parameters)
    {
        var result = new DynamicParameters();
        foreach (var (name, value) in parameters)
        {
            result.Add(name.TrimStart('@'), value is DBNull ? null : value);
        }
        return result;
    }

    private MySqlConnector.MySqlConnection? _helperConnection;

    private DbCommand CreateCommand(string sql, params (string Name, object? Value)[] parameters)
    {
        var connection = _helperConnection ?? _activeConnection
            ?? throw new InvalidOperationException("数据库连接尚未打开");
        var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Transaction = _activeTransaction;
        foreach (var (name, value) in parameters)
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = name;
            parameter.Value = value ?? DBNull.Value;
            command.Parameters.Add(parameter);
        }
        return command;
    }

    private async Task<ConnectionLease> OpenConnectionLeaseAsync()
    {
        if (_activeConnection != null)
        {
            _helperConnection = _activeConnection;
            return new ConnectionLease(this, null);
        }
        var connection = await _connectionFactory.OpenConnectionAsync();
        _helperConnection = connection;
        return new ConnectionLease(this, connection);
    }

    private sealed class ConnectionLease(
        ErpPendingReceiptService owner,
        MySqlConnector.MySqlConnection? ownedConnection) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            owner._helperConnection = null;
            if (ownedConnection != null) await ownedConnection.DisposeAsync();
        }
    }

    private async Task<int> ExecuteAsync(string sql, params (string Name, object? Value)[] parameters)
    {
        if (_activeConnection != null)
            return await _activeConnection.ExecuteAsync(sql, CreateParameters(parameters), _activeTransaction);
        await using var connection = await _connectionFactory.OpenConnectionAsync();
        return await connection.ExecuteAsync(sql, CreateParameters(parameters));
    }

    private async Task<T> ScalarAsync<T>(string sql, params (string Name, object? Value)[] parameters)
    {
        if (_activeConnection != null)
            return await _activeConnection.ExecuteScalarAsync<T>(sql, CreateParameters(parameters), _activeTransaction);
        await using var connection = await _connectionFactory.OpenConnectionAsync();
        return await connection.ExecuteScalarAsync<T>(sql, CreateParameters(parameters));
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
