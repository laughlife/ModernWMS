-- MySQL 8: metadata-only naming change; no inventory/reservation DML.
-- Requires all earlier WMS migrations and coordinated ERP/WMS deployment.
-- trk_stock, trk_stock_record and trk_stock_reservation* are unchanged.
-- RENAME COLUMN preserves data types, defaults, comments and index/constraint definitions.
-- DDL commits implicitly. Do not run while old applications are accessing these objects.

RENAME TABLE
  `wms_erp_stock_allocation` TO `wms_trk_stock_allocation`,
  `wms_erp_stock_reservation_allocation` TO `wms_trk_stock_reservation_allocation`,
  `wms_erp_stock_allocation_log` TO `wms_trk_stock_allocation_log`;

ALTER TABLE `wms_trk_stock_allocation`
  RENAME COLUMN `erp_stock_id` TO `trk_stock_id`;

ALTER TABLE `wms_trk_stock_reservation_allocation`
  RENAME COLUMN `erp_stock_id` TO `trk_stock_id`;

ALTER TABLE `wms_trk_stock_allocation_log`
  RENAME COLUMN `erp_stock_id` TO `trk_stock_id`,
  RENAME COLUMN `erp_stock_record_id` TO `trk_stock_record_id`;

ALTER TABLE `wms_inventory_operation`
  RENAME COLUMN `erp_stock_id` TO `trk_stock_id`,
  RENAME COLUMN `erp_stock_record_id` TO `trk_stock_record_id`;

ALTER TABLE `wms_dispatchpicklist`
  RENAME COLUMN `erp_stock_id` TO `trk_stock_id`;

ALTER TABLE `wms_packing_task_stock_selection`
  RENAME COLUMN `erp_stock_id` TO `trk_stock_id`;

ALTER TABLE `wms_erp_receipt_item`
  RENAME COLUMN `erp_stock_id` TO `trk_stock_id`;

ALTER TABLE `wms_stockadjust`
  RENAME COLUMN `erp_stock_id` TO `trk_stock_id`;

ALTER TABLE `wms_stockmove`
  RENAME COLUMN `erp_stock_id` TO `trk_stock_id`;

ALTER TABLE `wms_stockfreeze`
  RENAME COLUMN `erp_stock_id` TO `trk_stock_id`;

ALTER TABLE `wms_stockprocessdetail`
  RENAME COLUMN `erp_stock_id` TO `trk_stock_id`;

ALTER TABLE `wms_stocktaking`
  RENAME COLUMN `erp_stock_id` TO `trk_stock_id`;

ALTER TABLE `wms_weighing_box_item`
  RENAME COLUMN `erp_stock_id` TO `trk_stock_id`;

