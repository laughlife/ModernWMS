-- Actual box contents replace the former per-box planned quantity rows.
-- This forward migration is intentionally data-destructive only for development
-- weighing-box drafts; no production environment is authorized to run it.
DELETE FROM `wms_weighing_box_item`;

ALTER TABLE `wms_weighing_box_item`
  DROP INDEX `uk_weighing_box_item`,
  MODIFY COLUMN `packing_task_item_id` int NULL,
  CHANGE COLUMN `task_qty` `actual_qty` int NOT NULL,
  ADD COLUMN `client_line_key` varchar(64) NOT NULL AFTER `weighing_box_id`,
  ADD COLUMN `wms_sku_id` int NOT NULL AFTER `packing_task_item_id`,
  ADD COLUMN `erp_stock_id` bigint NOT NULL AFTER `wms_sku_id`,
  ADD COLUMN `stock_allocation_id` bigint NOT NULL AFTER `erp_stock_id`,
  ADD COLUMN `goods_owner_id` int NOT NULL AFTER `stock_allocation_id`,
  ADD COLUMN `goods_location_id` int NOT NULL AFTER `goods_owner_id`,
  ADD COLUMN `sku_code` varchar(255) NOT NULL AFTER `goods_location_id`,
  ADD COLUMN `commodity_name` varchar(500) NOT NULL AFTER `sku_code`,
  ADD COLUMN `dispatchpicklist_id` int NULL AFTER `actual_qty`,
  ADD UNIQUE KEY `uk_weighing_box_item_client_line` (`weighing_box_id`,`client_line_key`),
  ADD KEY `idx_weighing_box_item_stock_allocation` (`stock_allocation_id`),
  ADD KEY `idx_weighing_box_item_dispatch_pick` (`dispatchpicklist_id`),
  ADD CONSTRAINT `ck_weighing_box_item_actual_qty_positive` CHECK (`actual_qty` > 0);

ALTER TABLE `wms_erp_stock_allocation`
  DROP CHECK `ck_erp_stock_allocation_allocated_nonnegative`,
  DROP CHECK `ck_erp_stock_allocation_occupied_within_allocated`;

ALTER TABLE `wms_erp_stock_allocation_log`
  DROP CHECK `ck_erp_stock_allocation_log_before_nonnegative`,
  DROP CHECK `ck_erp_stock_allocation_log_after_nonnegative`,
  DROP CHECK `ck_erp_stock_allocation_log_before_occupied`,
  DROP CHECK `ck_erp_stock_allocation_log_after_occupied`,
  ADD CONSTRAINT `ck_erp_stock_allocation_log_before_occupied_nonnegative`
    CHECK (`before_occupied_qty` >= 0),
  ADD CONSTRAINT `ck_erp_stock_allocation_log_after_occupied_nonnegative`
    CHECK (`after_occupied_qty` >= 0);
