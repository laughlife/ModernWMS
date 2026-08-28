ALTER TABLE `wms_dispatch_packing_task_item`
  ADD COLUMN `erp_stock_plan_row_version` bigint NULL AFTER `source_stock_available`;
