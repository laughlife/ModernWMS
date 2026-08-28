ALTER TABLE `wms_dispatch_packing_task`
  ADD COLUMN `consume_status` varchar(24) NOT NULL DEFAULT 'NOT_REQUIRED' AFTER `packing_plan_status`;

CREATE TABLE `wms_packing_consume_outbox` (
  `id` bigint NOT NULL AUTO_INCREMENT,
  `dispatch_order_id` int NOT NULL,
  `packing_task_id` int NOT NULL,
  `sellfox_task_id` bigint NOT NULL,
  `sellfox_item_id` bigint NOT NULL,
  `request_id` varchar(64) NOT NULL,
  `payload_json` json NOT NULL,
  `status` varchar(24) NOT NULL DEFAULT 'PENDING',
  `attempt_count` int NOT NULL DEFAULT 0,
  `last_error` varchar(500) NOT NULL DEFAULT '',
  `consumed_at` datetime(6) NULL,
  `create_time` datetime(6) NOT NULL,
  `last_update_time` datetime(6) NOT NULL,
  `row_version` bigint NOT NULL DEFAULT 0,
  PRIMARY KEY (`id`),
  UNIQUE KEY `uk_packing_consume_outbox_request` (`request_id`),
  KEY `idx_packing_consume_outbox_pending` (`status`,`create_time`),
  KEY `idx_packing_consume_outbox_task` (`packing_task_id`,`status`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
