ALTER TABLE `wms_packing_task_stock_selection`
  ADD COLUMN `status` varchar(16) NOT NULL DEFAULT 'ACTIVE'
    COMMENT '绑定生命周期：ACTIVE/CANCELLED/TRANSFERRED' AFTER `last_update_time`,
  ADD COLUMN `cancelled_by` bigint DEFAULT NULL
    COMMENT '取消或回退操作人ID' AFTER `status`,
  ADD COLUMN `cancelled_by_name` varchar(128) DEFAULT NULL
    COMMENT '取消或回退操作人名称快照' AFTER `cancelled_by`,
  ADD COLUMN `cancelled_at` datetime(6) DEFAULT NULL
    COMMENT '取消或回退时间' AFTER `cancelled_by_name`,
  ADD COLUMN `cancel_reason` varchar(255) DEFAULT NULL
    COMMENT '取消或回退原因' AFTER `cancelled_at`,
  ADD COLUMN `row_version` bigint NOT NULL DEFAULT 0
    COMMENT '生命周期并发版本' AFTER `cancel_reason`,
  ADD COLUMN `operation_source` varchar(32) NOT NULL DEFAULT 'MODERN_WMS'
    COMMENT '最后一次生命周期操作来源' AFTER `row_version`,
  ADD KEY `idx_packing_selection_task_item_status`
    (`tenant_id`,`sellfox_task_id`,`sellfox_item_id`,`status`),
  ADD CONSTRAINT `ck_packing_selection_status`
    CHECK (`status` IN ('ACTIVE','CANCELLED','TRANSFERRED'));

UPDATE `wms_packing_task_stock_selection`
   SET `status`='ACTIVE',
       `operation_source`='MIGRATION_BACKFILL'
 WHERE `status`='ACTIVE';
