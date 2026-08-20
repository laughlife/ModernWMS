CREATE TABLE `wms_erp_stock_reservation_allocation` (
  `id` bigint NOT NULL AUTO_INCREMENT,
  `tenant_id` bigint NOT NULL,
  `reservation_item_id` bigint NOT NULL COMMENT '逻辑引用 trk_stock_reservation_item.id',
  `erp_stock_id` bigint NOT NULL COMMENT '逻辑引用 trk_stock.id',
  `stock_allocation_id` bigint NOT NULL COMMENT '逻辑引用 wms_erp_stock_allocation.id',
  `reserved_qty` bigint NOT NULL DEFAULT '0',
  `released_qty` bigint NOT NULL DEFAULT '0',
  `consumed_qty` bigint NOT NULL DEFAULT '0',
  `remaining_qty` bigint NOT NULL DEFAULT '0',
  `status` varchar(24) CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL,
  `row_version` bigint NOT NULL DEFAULT '0',
  `creator` varchar(128) CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL DEFAULT '',
  `create_time` datetime(6) NOT NULL,
  `updater` varchar(128) CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL DEFAULT '',
  `update_time` datetime(6) NOT NULL,
  `deleted` bit(1) NOT NULL DEFAULT b'0',
  PRIMARY KEY (`id`),
  UNIQUE KEY `uk_stock_reservation_allocation_owner`
    (`tenant_id`,`reservation_item_id`,`stock_allocation_id`,`deleted`),
  KEY `idx_stock_reservation_allocation_item`
    (`tenant_id`,`reservation_item_id`,`deleted`),
  KEY `idx_stock_reservation_allocation_stock`
    (`tenant_id`,`erp_stock_id`,`deleted`),
  KEY `idx_stock_reservation_allocation_location`
    (`tenant_id`,`stock_allocation_id`,`deleted`),
  CONSTRAINT `ck_stock_reservation_allocation_quantities_nonnegative` CHECK (
    `reserved_qty` >= 0 AND `released_qty` >= 0
    AND `consumed_qty` >= 0 AND `remaining_qty` >= 0
  ),
  CONSTRAINT `ck_stock_reservation_allocation_quantity_conservation` CHECK (
    `reserved_qty` = `released_qty` + `consumed_qty` + `remaining_qty`
  ),
  CONSTRAINT `ck_stock_reservation_allocation_status` CHECK (
    `status` IN ('ACTIVE','PARTIALLY_SETTLED','RELEASED','CONSUMED','MIXED_CLOSED','ORPHANED')
  )
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci
  COMMENT='ERP库存预占在WMS库位上的持有分解，不是独立库存余额';

ALTER TABLE `wms_inventory_operation`
  ADD COLUMN `shared_command_id` bigint DEFAULT NULL COMMENT '逻辑引用 trk_stock_reservation_command.id' AFTER `operation_key`,
  ADD COLUMN `reservation_id` bigint DEFAULT NULL COMMENT '逻辑引用 trk_stock_reservation.id' AFTER `shared_command_id`,
  ADD COLUMN `reservation_item_id` bigint DEFAULT NULL COMMENT '逻辑引用 trk_stock_reservation_item.id' AFTER `reservation_id`,
  ADD KEY `idx_inventory_operation_shared_command` (`tenant_id`,`shared_command_id`),
  ADD KEY `idx_inventory_operation_reservation_item` (`tenant_id`,`reservation_item_id`);

ALTER TABLE `wms_erp_stock_allocation_log`
  ADD COLUMN `shared_command_id` bigint DEFAULT NULL COMMENT '逻辑引用 trk_stock_reservation_command.id' AFTER `operation_key`,
  ADD COLUMN `reservation_id` bigint DEFAULT NULL COMMENT '逻辑引用 trk_stock_reservation.id' AFTER `shared_command_id`,
  ADD COLUMN `reservation_item_id` bigint DEFAULT NULL COMMENT '逻辑引用 trk_stock_reservation_item.id' AFTER `reservation_id`,
  ADD KEY `idx_stock_allocation_log_shared_command` (`tenant_id`,`shared_command_id`),
  ADD KEY `idx_stock_allocation_log_reservation_item` (`tenant_id`,`reservation_item_id`);

ALTER TABLE `wms_packing_task_stock_selection`
  ADD COLUMN `reservation_id` bigint DEFAULT NULL COMMENT '逻辑引用 trk_stock_reservation.id' AFTER `stock_allocation_id`,
  ADD COLUMN `reservation_item_id` bigint DEFAULT NULL COMMENT '逻辑引用 trk_stock_reservation_item.id' AFTER `reservation_id`,
  ADD KEY `idx_packing_selection_reservation_item` (`tenant_id`,`reservation_item_id`);

ALTER TABLE `wms_dispatchpicklist`
  ADD COLUMN `reservation_id` bigint DEFAULT NULL COMMENT '逻辑引用 trk_stock_reservation.id' AFTER `stock_allocation_id`,
  ADD COLUMN `reservation_item_id` bigint DEFAULT NULL COMMENT '逻辑引用 trk_stock_reservation_item.id' AFTER `reservation_id`,
  ADD KEY `idx_dispatchpicklist_reservation_item` (`reservation_item_id`);

ALTER TABLE `wms_stockfreeze`
  ADD COLUMN `reservation_id` bigint DEFAULT NULL COMMENT '逻辑引用 trk_stock_reservation.id' AFTER `stock_allocation_id`,
  ADD COLUMN `reservation_item_id` bigint DEFAULT NULL COMMENT '逻辑引用 trk_stock_reservation_item.id' AFTER `reservation_id`,
  ADD KEY `idx_stockfreeze_reservation_item` (`tenant_id`,`reservation_item_id`);
