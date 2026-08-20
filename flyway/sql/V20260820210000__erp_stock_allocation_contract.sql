CREATE TABLE `wms_erp_stock_allocation` (
  `id` bigint NOT NULL AUTO_INCREMENT,
  `tenant_id` bigint NOT NULL,
  `erp_stock_id` bigint NOT NULL COMMENT '逻辑引用 trk_stock.id，不创建跨所有者物理外键',
  `warehouse_area_id` int DEFAULT NULL,
  `goods_location_id` int DEFAULT NULL,
  `goods_owner_id` int NOT NULL,
  `series_number` varchar(128) CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL DEFAULT '',
  `expiry_date` datetime(6) NOT NULL,
  `price` decimal(18,2) NOT NULL,
  `putaway_date` date NOT NULL,
  `allocated_qty` bigint NOT NULL COMMENT 'trk_stock.total_qty 的位置分解，不是独立库存余额',
  `occupied_qty` bigint NOT NULL DEFAULT '0' COMMENT 'trk_stock.occupied_qty 的位置分解',
  `location_state` varchar(16) CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL,
  `unlocated_goods_owner_id` int GENERATED ALWAYS AS (CASE WHEN `location_state` = 'UNLOCATED' THEN `goods_owner_id` ELSE NULL END) STORED COMMENT '仅用于保证每个ERP库存/货主至多一个待确认库位分配',
  `row_version` bigint NOT NULL DEFAULT '0',
  `creator` varchar(128) CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL DEFAULT '',
  `create_time` datetime(6) NOT NULL,
  `updater` varchar(128) CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL DEFAULT '',
  `update_time` datetime(6) NOT NULL,
  PRIMARY KEY (`id`),
  UNIQUE KEY `uk_erp_stock_allocation_dimension` (`tenant_id`,`erp_stock_id`,`goods_location_id`,`goods_owner_id`,`series_number`,`expiry_date`,`price`,`putaway_date`),
  UNIQUE KEY `uk_erp_stock_allocation_unlocated_owner` (`tenant_id`,`erp_stock_id`,`unlocated_goods_owner_id`),
  KEY `idx_erp_stock_allocation_stock_state` (`tenant_id`,`erp_stock_id`,`location_state`),
  KEY `idx_erp_stock_allocation_location_state` (`tenant_id`,`goods_location_id`,`location_state`),
  CONSTRAINT `ck_erp_stock_allocation_allocated_nonnegative` CHECK (`allocated_qty` >= 0),
  CONSTRAINT `ck_erp_stock_allocation_occupied_nonnegative` CHECK (`occupied_qty` >= 0),
  CONSTRAINT `ck_erp_stock_allocation_occupied_within_allocated` CHECK (`occupied_qty` <= `allocated_qty`),
  CONSTRAINT `ck_erp_stock_allocation_location_state` CHECK (`location_state` IN ('ACTIVE','UNLOCATED','RETIRED')),
  CONSTRAINT `ck_erp_stock_allocation_location_required` CHECK (
    (`location_state` = 'ACTIVE' AND `warehouse_area_id` IS NOT NULL AND `goods_location_id` IS NOT NULL)
    OR (`location_state` = 'UNLOCATED' AND `warehouse_area_id` IS NULL AND `goods_location_id` IS NULL)
    OR (`location_state` = 'RETIRED' AND ((`warehouse_area_id` IS NULL AND `goods_location_id` IS NULL) OR (`warehouse_area_id` IS NOT NULL AND `goods_location_id` IS NOT NULL)))
  )
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci COMMENT='ERP库存的位置、货主及批次属性分配';

CREATE TABLE `wms_erp_stock_allocation_log` (
  `id` bigint NOT NULL AUTO_INCREMENT,
  `tenant_id` bigint NOT NULL,
  `operation_key` varchar(64) CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL,
  `biz_type` varchar(32) CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL,
  `biz_id` bigint NOT NULL,
  `biz_item_id` bigint NOT NULL,
  `event_type` varchar(32) CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL,
  `erp_stock_id` bigint NOT NULL COMMENT '逻辑引用 trk_stock.id',
  `allocation_id` bigint NOT NULL COMMENT '逻辑引用 wms_erp_stock_allocation.id',
  `counterpart_allocation_id` bigint DEFAULT NULL COMMENT '移位等成对操作的对端分配ID',
  `erp_stock_record_id` bigint DEFAULT NULL COMMENT '涉及ERP余额或占用变化时关联 trk_stock_record.id',
  `allocated_delta` bigint NOT NULL,
  `occupied_delta` bigint NOT NULL,
  `before_allocated_qty` bigint NOT NULL,
  `after_allocated_qty` bigint NOT NULL,
  `before_occupied_qty` bigint NOT NULL,
  `after_occupied_qty` bigint NOT NULL,
  `operator` varchar(128) CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL DEFAULT '',
  `operate_time` datetime(6) NOT NULL,
  `remark` varchar(500) CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL DEFAULT '',
  PRIMARY KEY (`id`),
  UNIQUE KEY `uk_erp_stock_allocation_log_operation` (`tenant_id`,`operation_key`,`allocation_id`,`event_type`),
  KEY `idx_erp_stock_allocation_log_stock_time` (`tenant_id`,`erp_stock_id`,`operate_time`),
  KEY `idx_erp_stock_allocation_log_biz` (`tenant_id`,`biz_type`,`biz_id`,`biz_item_id`),
  KEY `idx_erp_stock_allocation_log_record` (`erp_stock_record_id`),
  CONSTRAINT `ck_erp_stock_allocation_log_before_nonnegative` CHECK (`before_allocated_qty` >= 0 AND `before_occupied_qty` >= 0),
  CONSTRAINT `ck_erp_stock_allocation_log_after_nonnegative` CHECK (`after_allocated_qty` >= 0 AND `after_occupied_qty` >= 0),
  CONSTRAINT `ck_erp_stock_allocation_log_before_occupied` CHECK (`before_occupied_qty` <= `before_allocated_qty`),
  CONSTRAINT `ck_erp_stock_allocation_log_after_occupied` CHECK (`after_occupied_qty` <= `after_allocated_qty`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci COMMENT='ERP库存位置分配审计，不作为库存余额流水';

CREATE TABLE `wms_inventory_runtime_config` (
  `id` bigint NOT NULL AUTO_INCREMENT,
  `tenant_id` bigint NOT NULL,
  `erp_warehouse_id` bigint NOT NULL,
  `mode` varchar(24) CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL DEFAULT 'LEGACY_READ',
  `maintenance_enabled` tinyint(1) NOT NULL DEFAULT '0',
  `cutover_time` datetime(6) DEFAULT NULL,
  `row_version` bigint NOT NULL DEFAULT '0',
  `creator` varchar(128) CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL DEFAULT '',
  `create_time` datetime(6) NOT NULL,
  `updater` varchar(128) CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL DEFAULT '',
  `update_time` datetime(6) NOT NULL,
  PRIMARY KEY (`id`),
  UNIQUE KEY `uk_inventory_runtime_config_warehouse` (`tenant_id`,`erp_warehouse_id`),
  CONSTRAINT `ck_inventory_runtime_config_mode` CHECK (`mode` IN ('LEGACY_READ','CANONICAL_ERP'))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci COMMENT='按ERP仓库控制库存读取模式和维护窗口门禁';

CREATE TABLE `wms_inventory_operation` (
  `id` bigint NOT NULL AUTO_INCREMENT,
  `tenant_id` bigint NOT NULL,
  `operation_key` varchar(64) CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL,
  `biz_type` varchar(32) CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL,
  `biz_id` bigint NOT NULL,
  `biz_item_id` bigint NOT NULL,
  `mutation_type` varchar(32) CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL,
  `erp_stock_id` bigint NOT NULL COMMENT '逻辑引用 trk_stock.id',
  `allocation_id` bigint NOT NULL COMMENT '逻辑引用 wms_erp_stock_allocation.id',
  `counterpart_allocation_id` bigint DEFAULT NULL COMMENT '移位等成对操作的对端分配ID',
  `quantity` bigint NOT NULL,
  `operator` varchar(64) CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL DEFAULT '',
  `result_status` varchar(16) CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL DEFAULT 'PENDING',
  `erp_stock_record_id` bigint DEFAULT NULL COMMENT '逻辑引用 trk_stock_record.id',
  `create_time` datetime(6) NOT NULL,
  `update_time` datetime(6) NOT NULL,
  PRIMARY KEY (`id`),
  UNIQUE KEY `uk_inventory_operation_key` (`tenant_id`,`operation_key`),
  KEY `idx_inventory_operation_stock_time` (`tenant_id`,`erp_stock_id`,`create_time`),
  CONSTRAINT `ck_inventory_operation_result_status` CHECK (`result_status` IN ('PENDING','SUCCEEDED'))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci COMMENT='库存变更全局幂等命令头';

ALTER TABLE `wms_dispatchpicklist`
  ADD COLUMN `erp_stock_id` bigint DEFAULT NULL AFTER `stock_id`,
  ADD COLUMN `stock_allocation_id` bigint DEFAULT NULL AFTER `erp_stock_id`,
  ADD KEY `idx_dispatchpicklist_erp_stock` (`erp_stock_id`),
  ADD KEY `idx_dispatchpicklist_stock_allocation` (`stock_allocation_id`);

ALTER TABLE `wms_packing_task_stock_selection`
  ADD COLUMN `erp_stock_id` bigint DEFAULT NULL AFTER `stock_id`,
  ADD COLUMN `stock_allocation_id` bigint DEFAULT NULL AFTER `erp_stock_id`,
  ADD KEY `idx_packing_stock_selection_erp_stock` (`tenant_id`,`erp_stock_id`),
  ADD KEY `idx_packing_stock_selection_allocation` (`tenant_id`,`stock_allocation_id`);

ALTER TABLE `wms_erp_receipt_item`
  MODIFY COLUMN `erp_stock_id` bigint DEFAULT NULL COMMENT '逻辑引用 trk_stock.id；零入库或全损耗时为空',
  MODIFY COLUMN `wms_stock_id` int DEFAULT NULL COMMENT '旧 wms_stock.id，仅供历史兼容',
  ADD COLUMN `primary_stock_allocation_id` bigint DEFAULT NULL AFTER `wms_stock_id`,
  ADD KEY `idx_erp_receipt_item_erp_stock` (`tenant_id`,`erp_stock_id`),
  ADD KEY `idx_erp_receipt_item_primary_allocation` (`tenant_id`,`primary_stock_allocation_id`);

ALTER TABLE `wms_stockadjust`
  ADD COLUMN `erp_stock_id` bigint DEFAULT NULL AFTER `source_table_id`,
  ADD COLUMN `stock_allocation_id` bigint DEFAULT NULL AFTER `erp_stock_id`,
  ADD KEY `idx_stockadjust_erp_stock` (`tenant_id`,`erp_stock_id`),
  ADD KEY `idx_stockadjust_allocation` (`tenant_id`,`stock_allocation_id`);

ALTER TABLE `wms_stockmove`
  ADD COLUMN `erp_stock_id` bigint DEFAULT NULL AFTER `tenant_id`,
  ADD COLUMN `stock_allocation_id` bigint DEFAULT NULL AFTER `erp_stock_id`,
  ADD KEY `idx_stockmove_erp_stock` (`tenant_id`,`erp_stock_id`),
  ADD KEY `idx_stockmove_allocation` (`tenant_id`,`stock_allocation_id`);

ALTER TABLE `wms_stockfreeze`
  ADD COLUMN `erp_stock_id` bigint DEFAULT NULL AFTER `tenant_id`,
  ADD COLUMN `stock_allocation_id` bigint DEFAULT NULL AFTER `erp_stock_id`,
  ADD COLUMN `source_freeze_id` bigint DEFAULT NULL AFTER `stock_allocation_id` COMMENT '逻辑引用原冻结单，不创建物理外键',
  ADD KEY `idx_stockfreeze_erp_stock` (`tenant_id`,`erp_stock_id`),
  ADD KEY `idx_stockfreeze_allocation` (`tenant_id`,`stock_allocation_id`),
  ADD KEY `idx_stockfreeze_source` (`tenant_id`,`source_freeze_id`);

ALTER TABLE `wms_stockprocessdetail`
  ADD COLUMN `erp_stock_id` bigint DEFAULT NULL AFTER `tenant_id`,
  ADD COLUMN `stock_allocation_id` bigint DEFAULT NULL AFTER `erp_stock_id`,
  ADD KEY `idx_stockprocessdetail_erp_stock` (`tenant_id`,`erp_stock_id`),
  ADD KEY `idx_stockprocessdetail_allocation` (`tenant_id`,`stock_allocation_id`);

ALTER TABLE `wms_stocktaking`
  ADD COLUMN `erp_stock_id` bigint DEFAULT NULL AFTER `tenant_id`,
  ADD COLUMN `stock_allocation_id` bigint DEFAULT NULL AFTER `erp_stock_id`,
  ADD KEY `idx_stocktaking_erp_stock` (`tenant_id`,`erp_stock_id`),
  ADD KEY `idx_stocktaking_allocation` (`tenant_id`,`stock_allocation_id`);
