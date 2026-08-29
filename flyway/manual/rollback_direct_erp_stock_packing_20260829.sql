-- Manual rollback for V20260829120000__direct_erp_stock_packing.sql.
-- Run only together with rollback to the pre-Plan-A application version.
-- This script intentionally restores compatibility sentinels; it does not
-- attempt to reconstruct position allocations for Plan-A business rows.

ALTER TABLE `wms_weighing_box_item`
  DROP INDEX `idx_weighing_box_item_erp_stock`;

ALTER TABLE `wms_packing_task_stock_selection`
  DROP INDEX `idx_packing_selection_erp_stock_status`;

UPDATE `wms_packing_task_stock_selection`
   SET `wms_sku_id`=COALESCE(`wms_sku_id`,0),
       `stock_id`=COALESCE(`stock_id`,0),
       `goods_location_id`=COALESCE(`goods_location_id`,0),
       `goods_owner_id`=COALESCE(`goods_owner_id`,0);

UPDATE `wms_dispatchpicklist`
   SET `stock_id`=COALESCE(`stock_id`,0),
       `goods_owner_id`=COALESCE(`goods_owner_id`,0),
       `goods_location_id`=COALESCE(`goods_location_id`,0),
       `sku_id`=COALESCE(`sku_id`,0);

UPDATE `wms_weighing_box_item`
   SET `wms_sku_id`=COALESCE(`wms_sku_id`,0),
       `stock_allocation_id`=COALESCE(`stock_allocation_id`,0),
       `goods_owner_id`=COALESCE(`goods_owner_id`,0),
       `goods_location_id`=COALESCE(`goods_location_id`,0);

ALTER TABLE `wms_packing_task_stock_selection`
  MODIFY COLUMN `wms_sku_id` int NOT NULL DEFAULT 0,
  MODIFY COLUMN `stock_id` int NOT NULL DEFAULT 0,
  MODIFY COLUMN `goods_location_id` int NOT NULL DEFAULT 0,
  MODIFY COLUMN `goods_owner_id` int NOT NULL DEFAULT 0;

ALTER TABLE `wms_dispatchpicklist`
  MODIFY COLUMN `stock_id` int NOT NULL DEFAULT 0,
  MODIFY COLUMN `goods_owner_id` int NOT NULL DEFAULT 0,
  MODIFY COLUMN `goods_location_id` int NOT NULL DEFAULT 0,
  MODIFY COLUMN `sku_id` int NOT NULL DEFAULT 0;

ALTER TABLE `wms_weighing_box_item`
  MODIFY COLUMN `wms_sku_id` int NOT NULL DEFAULT 0,
  MODIFY COLUMN `stock_allocation_id` bigint NOT NULL DEFAULT 0,
  MODIFY COLUMN `goods_owner_id` int NOT NULL DEFAULT 0,
  MODIFY COLUMN `goods_location_id` int NOT NULL DEFAULT 0;

RENAME TABLE `wms_inventory_runtime_config_retired_20260829`
  TO `wms_inventory_runtime_config`;
