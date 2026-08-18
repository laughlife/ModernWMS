ALTER TABLE `wms_dispatch_packing_task`
  ADD COLUMN `packing_plan_status` varchar(24) NOT NULL DEFAULT 'DRAFT' AFTER `expected_box_count`,
  ADD COLUMN `actual_confirmed_at` datetime(6) NULL AFTER `packing_plan_status`,
  ADD COLUMN `actual_confirmed_by` int NULL AFTER `actual_confirmed_at`,
  ADD COLUMN `actual_confirmed_by_name` varchar(128) NOT NULL DEFAULT '' AFTER `actual_confirmed_by`;

ALTER TABLE `wms_dispatch_packing_task_item`
  ADD COLUMN `variant_qty` int NULL AFTER `source_stock_available`,
  ADD COLUMN `actual_packed_task_qty` int NULL AFTER `variant_qty`,
  ADD COLUMN `actual_packed_required_qty` int NULL AFTER `actual_packed_task_qty`;

UPDATE `wms_dispatch_packing_task_item`
SET `variant_qty` = `required_qty` DIV `source_quantity_shipped`
WHERE `required_qty` > 0
  AND `source_quantity_shipped` > 0
  AND MOD(`required_qty`, `source_quantity_shipped`) = 0;

CREATE TABLE `wms_weighing_box_item` (
  `id` int NOT NULL AUTO_INCREMENT,
  `weighing_box_id` int NOT NULL,
  `packing_task_item_id` int NOT NULL,
  `task_qty` int NOT NULL,
  `create_time` datetime(6) NOT NULL,
  `last_update_time` datetime(6) NOT NULL,
  `row_version` bigint NOT NULL DEFAULT 0,
  PRIMARY KEY (`id`),
  UNIQUE KEY `uk_weighing_box_item` (`weighing_box_id`, `packing_task_item_id`),
  KEY `idx_weighing_box_item_task_item` (`packing_task_item_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
