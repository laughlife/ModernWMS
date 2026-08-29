-- Plan A: trk_stock is the only authoritative packing inventory balance.
-- Preserve the removed runtime-gate table under a retired name so a later,
-- separately approved cleanup can drop it after the observation window.
RENAME TABLE `wms_inventory_runtime_config`
  TO `wms_inventory_runtime_config_retired_20260829`;

-- Existing columns remain readable for historical rows only. New packing
-- selections use erp_stock_id plus shared reservation identities.
ALTER TABLE `wms_packing_task_stock_selection`
  MODIFY COLUMN `wms_sku_id` int NULL DEFAULT NULL,
  MODIFY COLUMN `stock_id` int NULL DEFAULT NULL,
  MODIFY COLUMN `goods_location_id` int NULL DEFAULT NULL,
  MODIFY COLUMN `goods_owner_id` int NULL DEFAULT NULL,
  ADD KEY `idx_packing_selection_erp_stock_status`
    (`erp_stock_id`,`status`,`sellfox_task_id`,`sellfox_item_id`);

-- Dispatch picks keep old position fields nullable only for reading historical
-- orders. New rows are identified by erp_stock_id and reservation_item_id.
ALTER TABLE `wms_dispatchpicklist`
  MODIFY COLUMN `stock_id` int NULL DEFAULT NULL,
  MODIFY COLUMN `goods_owner_id` int NULL DEFAULT NULL,
  MODIFY COLUMN `goods_location_id` int NULL DEFAULT NULL,
  MODIFY COLUMN `sku_id` int NULL DEFAULT NULL;

-- Actual box contents are keyed by the ERP stock row, never by an allocation,
-- owner, area or location.
ALTER TABLE `wms_weighing_box_item`
  MODIFY COLUMN `wms_sku_id` int NULL DEFAULT NULL,
  MODIFY COLUMN `stock_allocation_id` bigint NULL DEFAULT NULL,
  MODIFY COLUMN `goods_owner_id` int NULL DEFAULT NULL,
  MODIFY COLUMN `goods_location_id` int NULL DEFAULT NULL,
  ADD KEY `idx_weighing_box_item_erp_stock` (`erp_stock_id`);

