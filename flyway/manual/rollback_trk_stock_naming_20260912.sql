-- Manual rollback only after this migration fully completed and all writers are paused.
-- For partial DDL completion, inspect INFORMATION_SCHEMA and reverse only completed statements.
-- Coordinate application rollback; never restore quantity rows from a stale snapshot.

ALTER TABLE `wms_trk_stock_allocation`
  RENAME COLUMN `trk_stock_id` TO `erp_stock_id`;

ALTER TABLE `wms_trk_stock_reservation_allocation`
  RENAME COLUMN `trk_stock_id` TO `erp_stock_id`;

ALTER TABLE `wms_trk_stock_allocation_log`
  RENAME COLUMN `trk_stock_id` TO `erp_stock_id`,
  RENAME COLUMN `trk_stock_record_id` TO `erp_stock_record_id`;

ALTER TABLE `wms_inventory_operation`
  RENAME COLUMN `trk_stock_id` TO `erp_stock_id`,
  RENAME COLUMN `trk_stock_record_id` TO `erp_stock_record_id`;

ALTER TABLE `wms_dispatchpicklist`
  RENAME COLUMN `trk_stock_id` TO `erp_stock_id`;

ALTER TABLE `wms_packing_task_stock_selection`
  RENAME COLUMN `trk_stock_id` TO `erp_stock_id`;

ALTER TABLE `wms_erp_receipt_item`
  RENAME COLUMN `trk_stock_id` TO `erp_stock_id`;

ALTER TABLE `wms_stockadjust`
  RENAME COLUMN `trk_stock_id` TO `erp_stock_id`;

ALTER TABLE `wms_stockmove`
  RENAME COLUMN `trk_stock_id` TO `erp_stock_id`;

ALTER TABLE `wms_stockfreeze`
  RENAME COLUMN `trk_stock_id` TO `erp_stock_id`;

ALTER TABLE `wms_stockprocessdetail`
  RENAME COLUMN `trk_stock_id` TO `erp_stock_id`;

ALTER TABLE `wms_stocktaking`
  RENAME COLUMN `trk_stock_id` TO `erp_stock_id`;

ALTER TABLE `wms_weighing_box_item`
  RENAME COLUMN `trk_stock_id` TO `erp_stock_id`;

RENAME TABLE
  `wms_trk_stock_allocation` TO `wms_erp_stock_allocation`,
  `wms_trk_stock_reservation_allocation` TO `wms_erp_stock_reservation_allocation`,
  `wms_trk_stock_allocation_log` TO `wms_erp_stock_allocation_log`;
