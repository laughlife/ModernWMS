-- 收货库存采用“仓库必填，库区/库位逐级可空”的真实层级。
-- 不创建占位库区或假库位；库位存在时必须属于已保存的库区。

ALTER TABLE `wms_erp_stock_allocation`
  DROP INDEX `uk_erp_stock_allocation_dimension`,
  DROP INDEX `uk_erp_stock_allocation_unlocated_owner`,
  DROP CHECK `ck_erp_stock_allocation_location_required`,
  DROP COLUMN `unlocated_goods_owner_id`,
  ADD COLUMN `storage_scope_key` varchar(64)
    GENERATED ALWAYS AS (
      CASE
        WHEN `goods_location_id` IS NOT NULL THEN concat('L:',`goods_location_id`)
        WHEN `warehouse_area_id` IS NOT NULL THEN concat('A:',`warehouse_area_id`)
        ELSE 'W'
      END
    ) STORED AFTER `location_state`,
  ADD UNIQUE KEY `uk_erp_stock_allocation_storage_dimension`
    (`tenant_id`,`erp_stock_id`,`storage_scope_key`,`goods_owner_id`,
     `series_number`,`expiry_date`,`price`,`putaway_date`),
  ADD CONSTRAINT `ck_erp_stock_allocation_storage_hierarchy` CHECK (
    (`goods_location_id` IS NULL OR `warehouse_area_id` IS NOT NULL)
    AND (
      (`location_state`='ACTIVE')
      OR (`location_state`='UNLOCATED' AND `goods_location_id` IS NULL)
      OR (`location_state`='RETIRED')
    )
  );

ALTER TABLE `wms_goodslocation`
  ADD KEY `idx_receipt_location_scope`
    (`tenant_id`,`warehouse_id`,`warehouse_area_id`,`is_valid`,`id`);

ALTER TABLE `wms_packing_task_stock_selection`
  MODIFY COLUMN `goods_location_id` int DEFAULT NULL COMMENT '可选真实库位；仓库级或库区级库存为空';

ALTER TABLE `wms_dispatchpicklist`
  MODIFY COLUMN `goods_location_id` int DEFAULT NULL COMMENT '可选真实库位；以stock_allocation_id标识库存作用域';

ALTER TABLE `wms_erp_receipt_item`
  MODIFY COLUMN `warehouse_area_id` int DEFAULT NULL COMMENT '可选wms_warehousearea.id；未绑定库区时为空',
  ADD COLUMN `goods_location_id` int DEFAULT NULL COMMENT '可选wms_goodslocation.id；无真实库位时为空'
    AFTER `warehouse_area_name`,
  ADD COLUMN `goods_location_name` varchar(128) NOT NULL DEFAULT '' COMMENT '库位名称快照；无库位时为空串'
    AFTER `goods_location_id`,
  ADD KEY `idx_erp_receipt_item_location` (`goods_location_id`),
  ADD CONSTRAINT `ck_erp_receipt_item_storage_hierarchy` CHECK (
    `goods_location_id` IS NULL OR `warehouse_area_id` IS NOT NULL
  );

ALTER TABLE `wms_receipt_item_owner`
  DROP INDEX `uk_receipt_area_owner`,
  MODIFY COLUMN `warehouse_area_id` int DEFAULT NULL COMMENT '可选wms_warehousearea.id；未绑定库区时为空',
  ADD COLUMN `goods_location_id` int DEFAULT NULL COMMENT '可选wms_goodslocation.id；无真实库位时为空'
    AFTER `warehouse_area_name`,
  ADD COLUMN `goods_location_name` varchar(128) NOT NULL DEFAULT '' COMMENT '库位名称快照；无库位时为空串'
    AFTER `goods_location_id`,
  ADD COLUMN `storage_scope_key` varchar(64)
    GENERATED ALWAYS AS (
      CASE
        WHEN `goods_location_id` IS NOT NULL THEN concat('L:',`goods_location_id`)
        WHEN `warehouse_area_id` IS NOT NULL THEN concat('A:',`warehouse_area_id`)
        ELSE 'W'
      END
    ) STORED AFTER `goods_location_name`,
  ADD UNIQUE KEY `uk_receipt_storage_owner`
    (`receipt_item_id`,`storage_scope_key`,`goods_owner_id`),
  ADD KEY `idx_tenant_location` (`tenant_id`,`goods_location_id`),
  ADD CONSTRAINT `ck_receipt_item_owner_storage_hierarchy` CHECK (
    `goods_location_id` IS NULL OR `warehouse_area_id` IS NOT NULL
  );
