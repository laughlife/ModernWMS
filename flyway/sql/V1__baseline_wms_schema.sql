-- ModernWMS WMS-owned schema baseline.
-- Generated from the authorized local development schema; contains no ERP tables or data.
SET FOREIGN_KEY_CHECKS=0;

/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `wms_action_log` (
  `id` int NOT NULL AUTO_INCREMENT,
  `vue_path` longtext CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL,
  `user_name` longtext CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL,
  `action_content` longtext CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL,
  `action_time` datetime(6) NOT NULL,
  `tenant_id` bigint NOT NULL,
  PRIMARY KEY (`id`) USING BTREE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci ROW_FORMAT=DYNAMIC;
/*!40101 SET character_set_client = @saved_cs_client */;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `wms_asn` (
  `id` int NOT NULL AUTO_INCREMENT,
  `asnmaster_id` int NOT NULL,
  `asn_no` longtext CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL,
  `asn_status` tinyint unsigned NOT NULL,
  `spu_id` int NOT NULL,
  `sku_id` int NOT NULL,
  `asn_qty` int NOT NULL,
  `actual_qty` int NOT NULL,
  `arrival_time` datetime(6) NOT NULL,
  `unload_time` datetime(6) NOT NULL,
  `unload_person_id` int NOT NULL,
  `unload_person` longtext CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL,
  `sorted_qty` int NOT NULL,
  `shortage_qty` int NOT NULL,
  `more_qty` int NOT NULL,
  `damage_qty` int NOT NULL,
  `weight` decimal(18,2) NOT NULL,
  `volume` decimal(18,2) NOT NULL,
  `supplier_id` int NOT NULL,
  `supplier_name` longtext CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL,
  `goods_owner_id` int NOT NULL,
  `goods_owner_name` longtext CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL,
  `creator` longtext CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL,
  `create_time` datetime(6) NOT NULL,
  `last_update_time` datetime(6) NOT NULL,
  `is_valid` tinyint(1) NOT NULL,
  `tenant_id` bigint NOT NULL,
  `expiry_date` datetime(6) NOT NULL,
  `price` decimal(18,2) NOT NULL,
  PRIMARY KEY (`id`) USING BTREE,
  KEY `IX_wms_asn_asnmaster_id` (`asnmaster_id`) USING BTREE,
  CONSTRAINT `FK_wms_asn_wms_asnmaster_asnmaster_id` FOREIGN KEY (`asnmaster_id`) REFERENCES `wms_asnmaster` (`id`) ON DELETE CASCADE ON UPDATE RESTRICT
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci ROW_FORMAT=DYNAMIC;
/*!40101 SET character_set_client = @saved_cs_client */;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `wms_asnmaster` (
  `id` int NOT NULL AUTO_INCREMENT,
  `asn_no` longtext CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL,
  `asn_batch` longtext CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL,
  `estimated_arrival_time` datetime(6) NOT NULL,
  `asn_status` tinyint unsigned NOT NULL,
  `weight` decimal(18,2) NOT NULL,
  `volume` decimal(18,2) NOT NULL,
  `goods_owner_id` int NOT NULL,
  `goods_owner_name` longtext CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL,
  `creator` longtext CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL,
  `create_time` datetime(6) NOT NULL,
  `last_update_time` datetime(6) NOT NULL,
  `tenant_id` bigint NOT NULL,
  PRIMARY KEY (`id`) USING BTREE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci ROW_FORMAT=DYNAMIC;
/*!40101 SET character_set_client = @saved_cs_client */;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `wms_asnsort` (
  `id` int NOT NULL AUTO_INCREMENT,
  `asn_id` int NOT NULL,
  `sorted_qty` int NOT NULL,
  `series_number` longtext CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL,
  `putaway_qty` int NOT NULL,
  `creator` longtext CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL,
  `create_time` datetime(6) NOT NULL,
  `last_update_time` datetime(6) NOT NULL,
  `is_valid` tinyint(1) NOT NULL,
  `tenant_id` bigint NOT NULL,
  PRIMARY KEY (`id`) USING BTREE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci ROW_FORMAT=DYNAMIC;
/*!40101 SET character_set_client = @saved_cs_client */;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `wms_company` (
  `id` int NOT NULL AUTO_INCREMENT,
  `company_name` longtext CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL,
  `city` longtext CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL,
  `address` longtext CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL,
  `manager` longtext CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL,
  `contact_tel` longtext CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL,
  `create_time` datetime(6) NOT NULL,
  `last_update_time` datetime(6) NOT NULL,
  `tenant_id` bigint NOT NULL,
  PRIMARY KEY (`id`) USING BTREE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci ROW_FORMAT=DYNAMIC;
/*!40101 SET character_set_client = @saved_cs_client */;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `wms_dispatch_order` (
  `id` int NOT NULL AUTO_INCREMENT,
  `dispatch_no` varchar(64) CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL,
  `create_idempotency_key` varchar(64) CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL,
  `warehouse_id` bigint NOT NULL,
  `status` tinyint unsigned NOT NULL,
  `source_version` varchar(64) CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL,
  `source_snapshot` longtext CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL,
  `source_change_pending` tinyint(1) NOT NULL,
  `source_change_snapshot` longtext CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL,
  `accepted_source_version` varchar(64) CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL,
  `adjudicated_source_version` varchar(64) CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL,
  `adjudicated_by` int DEFAULT NULL,
  `adjudicated_by_name` varchar(128) CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL,
  `adjudicated_at` datetime(6) DEFAULT NULL,
  `adjudication_reason` varchar(500) CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL,
  `tenant_id` bigint NOT NULL,
  `created_by` int NOT NULL,
  `creator` varchar(128) CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL,
  `create_time` datetime(6) NOT NULL,
  `last_update_time` datetime(6) NOT NULL,
  `row_version` bigint NOT NULL,
  `damaged_qty` int DEFAULT NULL,
  `notification_attempt_count` int NOT NULL DEFAULT '0',
  `notification_last_error` varchar(500) CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL DEFAULT '',
  `notification_sent_at` datetime(6) DEFAULT NULL,
  `notification_status` tinyint unsigned NOT NULL DEFAULT '0',
  `notification_updated_at` datetime(6) DEFAULT NULL,
  `signed_at` datetime(6) DEFAULT NULL,
  `signed_by` int DEFAULT NULL,
  `signed_by_name` varchar(128) CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL DEFAULT '',
  `signed_qty` int DEFAULT NULL,
  `pending_source_version` varchar(64) CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL DEFAULT '',
  PRIMARY KEY (`id`) USING BTREE,
  UNIQUE KEY `IX_wms_dispatch_order_create_idempotency_key` (`create_idempotency_key`) USING BTREE,
  UNIQUE KEY `IX_wms_dispatch_order_dispatch_no` (`dispatch_no`) USING BTREE,
  KEY `IX_wms_dispatch_order_warehouse_id_status` (`warehouse_id`,`status`) USING BTREE,
  KEY `IX_wms_dispatch_order_notification_status_notification_updated_~` (`notification_status`,`notification_updated_at`) USING BTREE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci ROW_FORMAT=DYNAMIC;
/*!40101 SET character_set_client = @saved_cs_client */;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `wms_dispatch_packing_task` (
  `id` int NOT NULL AUTO_INCREMENT,
  `dispatch_order_id` int NOT NULL,
  `active_source_task_id` bigint DEFAULT NULL,
  `task_no` varchar(64) CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL,
  `source_task_id` bigint NOT NULL,
  `source_task_no` varchar(64) CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL,
  `source_cartons_json` longtext CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL,
  `status` tinyint unsigned NOT NULL,
  `measured_box_count` int NOT NULL,
  `expected_box_count` int NOT NULL,
  `source_version` varchar(64) CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL,
  `stable_box_identity_verified` tinyint(1) NOT NULL,
  `box_identity_validation_error` varchar(500) CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL,
  `is_active` tinyint(1) NOT NULL,
  `source_cancelled_at` datetime(6) DEFAULT NULL,
  `writeback_status` varchar(32) CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL,
  `writeback_request_hash` varchar(64) CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL,
  `writeback_response` longtext CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL,
  `writeback_retry_count` int NOT NULL,
  `writeback_last_attempt_at` datetime(6) DEFAULT NULL,
  `create_time` datetime(6) NOT NULL,
  `last_update_time` datetime(6) NOT NULL,
  `row_version` bigint NOT NULL,
  PRIMARY KEY (`id`) USING BTREE,
  UNIQUE KEY `IX_wms_dispatch_packing_task_dispatch_order_id_source_task_id` (`dispatch_order_id`,`source_task_id`) USING BTREE,
  UNIQUE KEY `IX_wms_dispatch_packing_task_active_source_task_id` (`active_source_task_id`) USING BTREE,
  KEY `IX_wms_dispatch_packing_task_dispatch_order_id_is_active` (`dispatch_order_id`,`is_active`) USING BTREE,
  CONSTRAINT `FK_wms_dispatch_packing_task_wms_dispatch_order_dispatch_order_~` FOREIGN KEY (`dispatch_order_id`) REFERENCES `wms_dispatch_order` (`id`) ON DELETE CASCADE ON UPDATE RESTRICT
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci ROW_FORMAT=DYNAMIC;
/*!40101 SET character_set_client = @saved_cs_client */;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `wms_dispatch_packing_task_item` (
  `id` int NOT NULL AUTO_INCREMENT,
  `packing_task_id` int NOT NULL,
  `source_item_id` bigint NOT NULL,
  `source_commodity_id` bigint DEFAULT NULL,
  `wms_sku_id` int DEFAULT NULL,
  `commodity_sku` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL,
  `commodity_name` varchar(500) CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL,
  `fn_sku` varchar(128) CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL,
  `msku` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL,
  `required_qty` int DEFAULT NULL,
  `source_quantity_shipped` int DEFAULT NULL,
  `source_stock_available` int DEFAULT NULL,
  `source_version` varchar(64) CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL,
  `source_snapshot` longtext CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL,
  `is_active` tinyint(1) NOT NULL,
  `create_time` datetime(6) NOT NULL,
  `last_update_time` datetime(6) NOT NULL,
  `row_version` bigint NOT NULL,
  PRIMARY KEY (`id`) USING BTREE,
  UNIQUE KEY `IX_wms_dispatch_packing_task_item_packing_task_id_source_item_id` (`packing_task_id`,`source_item_id`) USING BTREE,
  KEY `IX_wms_dispatch_packing_task_item_packing_task_id_is_active` (`packing_task_id`,`is_active`) USING BTREE,
  CONSTRAINT `FK_wms_dispatch_packing_task_item_wms_dispatch_packing_task_pac~` FOREIGN KEY (`packing_task_id`) REFERENCES `wms_dispatch_packing_task` (`id`) ON DELETE CASCADE ON UPDATE RESTRICT
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci ROW_FORMAT=DYNAMIC;
/*!40101 SET character_set_client = @saved_cs_client */;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `wms_dispatch_source_change_event` (
  `id` int NOT NULL AUTO_INCREMENT,
  `dispatch_order_id` int NOT NULL,
  `source_version` varchar(64) CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL,
  `event_idempotency_key` varchar(64) CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL,
  `decision` tinyint unsigned NOT NULL,
  `operator_id` int NOT NULL,
  `operator_name` varchar(128) CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL,
  `decision_time` datetime(6) NOT NULL,
  `reason` varchar(500) CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL,
  `diff_snapshot` longtext CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL,
  PRIMARY KEY (`id`) USING BTREE,
  UNIQUE KEY `IX_wms_dispatch_source_change_event_event_idempotency_key` (`event_idempotency_key`) USING BTREE,
  UNIQUE KEY `IX_wms_dispatch_source_change_event_dispatch_order_id_source_ve~` (`dispatch_order_id`,`source_version`,`decision`) USING BTREE,
  CONSTRAINT `FK_wms_dispatch_source_change_event_wms_dispatch_order_dispatch~` FOREIGN KEY (`dispatch_order_id`) REFERENCES `wms_dispatch_order` (`id`) ON DELETE RESTRICT ON UPDATE RESTRICT
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci ROW_FORMAT=DYNAMIC;
/*!40101 SET character_set_client = @saved_cs_client */;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `wms_dispatch_weighing_box` (
  `id` int NOT NULL AUTO_INCREMENT,
  `tenant_id` bigint NOT NULL,
  `dispatch_no` varchar(32) CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL,
  `fba_shipment_id` bigint NOT NULL,
  `fba_no` varchar(64) CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL,
  `erp_box_id` bigint NOT NULL,
  `box_no` varchar(64) CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL,
  `box_index` int NOT NULL,
  `tracking_id` varchar(128) CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL,
  `weighing_weight` decimal(18,2) NOT NULL,
  `weighing_length` decimal(18,2) NOT NULL,
  `weighing_width` decimal(18,2) NOT NULL,
  `weighing_height` decimal(18,2) NOT NULL,
  `weighing_volume` decimal(18,2) NOT NULL,
  `weighing_person_id` int NOT NULL,
  `weighing_person` varchar(64) CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL,
  `weighing_time` datetime(6) NOT NULL,
  `copied_from_erp_box_id` bigint DEFAULT NULL,
  `create_time` datetime(6) NOT NULL,
  `last_update_time` datetime(6) NOT NULL,
  PRIMARY KEY (`id`) USING BTREE,
  UNIQUE KEY `IX_wms_dispatch_weighing_box_tenant_id_erp_box_id` (`tenant_id`,`erp_box_id`) USING BTREE,
  KEY `IX_wms_dispatch_weighing_box_tenant_id_dispatch_no` (`tenant_id`,`dispatch_no`) USING BTREE,
  KEY `IX_wms_dispatch_weighing_box_tenant_id_fba_shipment_id` (`tenant_id`,`fba_shipment_id`) USING BTREE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci ROW_FORMAT=DYNAMIC;
/*!40101 SET character_set_client = @saved_cs_client */;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `wms_dispatch_workflow_operation` (
  `id` int NOT NULL AUTO_INCREMENT,
  `dispatch_order_id` int NOT NULL,
  `operation` tinyint unsigned NOT NULL,
  `request_id` varchar(64) CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL,
  `result_status` tinyint unsigned NOT NULL,
  `result_order_status` tinyint unsigned DEFAULT NULL,
  `result_row_version` bigint DEFAULT NULL,
  `create_operator` int NOT NULL,
  `create_operator_name` varchar(128) CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL,
  `create_time` datetime(6) NOT NULL,
  PRIMARY KEY (`id`) USING BTREE,
  UNIQUE KEY `IX_wms_dispatch_workflow_operation_dispatch_order_id_operation_~` (`dispatch_order_id`,`operation`,`request_id`) USING BTREE,
  KEY `IX_wms_dispatch_workflow_operation_dispatch_order_id_create_time` (`dispatch_order_id`,`create_time`) USING BTREE,
  CONSTRAINT `FK_wms_dispatch_workflow_operation_wms_dispatch_order_dispatch_~` FOREIGN KEY (`dispatch_order_id`) REFERENCES `wms_dispatch_order` (`id`) ON DELETE RESTRICT ON UPDATE RESTRICT
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci ROW_FORMAT=DYNAMIC;
/*!40101 SET character_set_client = @saved_cs_client */;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `wms_dispatchlist` (
  `id` int NOT NULL AUTO_INCREMENT,
  `dispatch_no` longtext CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL,
  `dispatch_status` tinyint unsigned NOT NULL,
  `sku_id` int NOT NULL,
  `qty` int NOT NULL,
  `weight` decimal(18,2) NOT NULL,
  `volume` decimal(18,2) NOT NULL,
  `creator` longtext CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL,
  `create_time` datetime(6) NOT NULL,
  `damage_qty` int NOT NULL,
  `lock_qty` int NOT NULL,
  `picked_qty` int NOT NULL,
  `intrasit_qty` int NOT NULL,
  `package_qty` int NOT NULL,
  `weighing_qty` int NOT NULL,
  `actual_qty` int NOT NULL,
  `sign_qty` int NOT NULL,
  `package_no` longtext CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL,
  `package_person` longtext CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL,
  `package_time` datetime(6) NOT NULL,
  `weighing_no` longtext CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL,
  `weighing_person` longtext CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL,
  `weighing_weight` decimal(18,2) NOT NULL,
  `waybill_no` longtext CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL,
  `carrier` longtext CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL,
  `freightfee` decimal(18,2) NOT NULL,
  `last_update_time` datetime(6) NOT NULL,
  `tenant_id` bigint NOT NULL,
  `pick_checker_id` int NOT NULL,
  `pick_checker` longtext CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL,
  `weighing_length` decimal(18,2) NOT NULL DEFAULT '0.00',
  `weighing_width` decimal(18,2) NOT NULL DEFAULT '0.00',
  `weighing_height` decimal(18,2) NOT NULL DEFAULT '0.00',
  `weighing_volume` decimal(18,2) NOT NULL DEFAULT '0.00',
  `carrier_warehouse_id` bigint DEFAULT NULL,
  `volume_divisor` int DEFAULT NULL COMMENT '材积重计算除数，允许5000/6000/7000/8000',
  `carrier_unit` varchar(256) CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL DEFAULT '' COMMENT '承运单位ERP国内仓库名称快照',
  `dispatch_order_id` int DEFAULT NULL,
  `packing_task_id` int DEFAULT NULL,
  `packing_task_item_id` int DEFAULT NULL,
  PRIMARY KEY (`id`) USING BTREE,
  KEY `IX_wms_dispatchlist_dispatch_order_id_packing_task_id` (`dispatch_order_id`,`packing_task_id`) USING BTREE,
  KEY `IX_wms_dispatchlist_packing_task_id` (`packing_task_id`) USING BTREE,
  KEY `IX_wms_dispatchlist_packing_task_item_id` (`packing_task_item_id`) USING BTREE,
  CONSTRAINT `FK_wms_dispatchlist_wms_dispatch_order_dispatch_order_id` FOREIGN KEY (`dispatch_order_id`) REFERENCES `wms_dispatch_order` (`id`) ON DELETE RESTRICT ON UPDATE RESTRICT,
  CONSTRAINT `FK_wms_dispatchlist_wms_dispatch_packing_task_item_packing_task~` FOREIGN KEY (`packing_task_item_id`) REFERENCES `wms_dispatch_packing_task_item` (`id`) ON DELETE RESTRICT ON UPDATE RESTRICT,
  CONSTRAINT `FK_wms_dispatchlist_wms_dispatch_packing_task_packing_task_id` FOREIGN KEY (`packing_task_id`) REFERENCES `wms_dispatch_packing_task` (`id`) ON DELETE RESTRICT ON UPDATE RESTRICT
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci ROW_FORMAT=DYNAMIC;
/*!40101 SET character_set_client = @saved_cs_client */;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `wms_dispatchpicklist` (
  `id` int NOT NULL AUTO_INCREMENT,
  `dispatchlist_id` int NOT NULL,
  `goods_owner_id` int NOT NULL,
  `goods_location_id` int NOT NULL,
  `sku_id` int NOT NULL,
  `pick_qty` int NOT NULL,
  `picked_qty` int NOT NULL,
  `is_update_stock` tinyint(1) NOT NULL,
  `last_update_time` datetime(6) NOT NULL,
  `series_number` longtext CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL,
  `picker_id` int NOT NULL,
  `picker` longtext CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL,
  `expiry_date` datetime(6) NOT NULL,
  `price` decimal(18,2) NOT NULL,
  `putaway_date` datetime(6) NOT NULL,
  `stock_id` int NOT NULL DEFAULT '0',
  `packing_task_item_id` int DEFAULT NULL,
  PRIMARY KEY (`id`) USING BTREE,
  KEY `IX_wms_dispatchpicklist_dispatchlist_id` (`dispatchlist_id`) USING BTREE,
  KEY `IX_wms_dispatchpicklist_packing_task_item_id` (`packing_task_item_id`) USING BTREE,
  CONSTRAINT `FK_wms_dispatchpicklist_wms_dispatch_packing_task_item_packing_~` FOREIGN KEY (`packing_task_item_id`) REFERENCES `wms_dispatch_packing_task_item` (`id`) ON DELETE RESTRICT ON UPDATE RESTRICT,
  CONSTRAINT `FK_wms_dispatchpicklist_wms_dispatchlist_dispatchlist_id` FOREIGN KEY (`dispatchlist_id`) REFERENCES `wms_dispatchlist` (`id`) ON DELETE CASCADE ON UPDATE RESTRICT
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci ROW_FORMAT=DYNAMIC;
/*!40101 SET character_set_client = @saved_cs_client */;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `wms_erp_commodity_map` (
  `id` int NOT NULL AUTO_INCREMENT,
  `erp_commodity_id` bigint NOT NULL,
  `wms_spu_id` int NOT NULL,
  `wms_sku_id` int NOT NULL,
  `commodity_sku` varchar(128) CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL,
  `last_sync_time` datetime(6) NOT NULL,
  `tenant_id` bigint NOT NULL,
  PRIMARY KEY (`id`) USING BTREE,
  UNIQUE KEY `IX_wms_erp_commodity_map_tenant_id_erp_commodity_id` (`tenant_id`,`erp_commodity_id`) USING BTREE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci ROW_FORMAT=DYNAMIC;
/*!40101 SET character_set_client = @saved_cs_client */;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `wms_erp_goods_owner_map` (
  `id` int NOT NULL AUTO_INCREMENT,
  `erp_dept_id` bigint NOT NULL,
  `erp_order_user_id` bigint NOT NULL,
  `wms_goods_owner_id` int NOT NULL,
  `dept_name` varchar(128) CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL,
  `order_user_name` varchar(128) CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL,
  `last_sync_time` datetime(6) NOT NULL,
  `tenant_id` bigint NOT NULL,
  PRIMARY KEY (`id`) USING BTREE,
  UNIQUE KEY `UX_wms_owner_map_erp_owner` (`tenant_id`,`erp_dept_id`,`erp_order_user_id`) USING BTREE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci ROW_FORMAT=DYNAMIC;
/*!40101 SET character_set_client = @saved_cs_client */;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `wms_erp_receipt` (
  `id` int NOT NULL AUTO_INCREMENT,
  `shipment_id` bigint NOT NULL COMMENT 'ERP货件ID',
  `source_version` int NOT NULL DEFAULT '0' COMMENT 'ERP货件源版本',
  `actual_receipt_qty` bigint NOT NULL DEFAULT '0' COMMENT '实际收货数量',
  `loss_qty` bigint NOT NULL DEFAULT '0' COMMENT '损耗数量',
  `inbound_qty` bigint NOT NULL DEFAULT '0' COMMENT '入库数量=实际收货数量-损耗数量',
  `receipt_freight_payment_status` varchar(16) CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL DEFAULT '' COMMENT '运费支付状态：NO_PAY/PAY',
  `receipt_freight_amount` decimal(18,2) DEFAULT NULL COMMENT '支付运费金额',
  `receipt_freight_files_json` longtext CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL COMMENT '运费附图JSON',
  `receipt_files_json` longtext CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL COMMENT '收货附件JSON',
  `loss_reason` varchar(500) CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL DEFAULT '' COMMENT '损耗原因',
  `loss_files_json` longtext CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL COMMENT '损耗附件JSON',
  `receipt_remark` varchar(500) CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL DEFAULT '' COMMENT '收货备注',
  `creator` varchar(64) CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL DEFAULT '' COMMENT '创建者',
  `create_time` datetime NOT NULL DEFAULT CURRENT_TIMESTAMP COMMENT '创建时间',
  `last_update_time` datetime NOT NULL DEFAULT CURRENT_TIMESTAMP COMMENT '更新时间',
  `tenant_id` bigint NOT NULL DEFAULT '1' COMMENT '租户ID',
  PRIMARY KEY (`id`) USING BTREE,
  UNIQUE KEY `IX_wms_erp_receipt_shipment_id` (`shipment_id`) USING BTREE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci ROW_FORMAT=DYNAMIC COMMENT='ERP货件验货签收记录';
/*!40101 SET character_set_client = @saved_cs_client */;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `wms_erp_receipt_item` (
  `id` int NOT NULL AUTO_INCREMENT,
  `receipt_id` int NOT NULL,
  `shipment_id` bigint NOT NULL,
  `source_item_key` varchar(160) CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL,
  `task_item_id` bigint DEFAULT NULL,
  `allocation_id` bigint DEFAULT NULL,
  `commodity_id` bigint DEFAULT NULL,
  `commodity_sku` varchar(128) CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL,
  `commodity_name` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL,
  `dept_id` bigint DEFAULT NULL,
  `order_user_id` bigint DEFAULT NULL,
  `dept_name` varchar(128) CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL DEFAULT '',
  `order_user_name` varchar(128) CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL DEFAULT '',
  `warehouse_area_id` int NOT NULL DEFAULT '0',
  `warehouse_area_name` varchar(128) CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL DEFAULT '',
  `shipment_qty` bigint NOT NULL,
  `actual_receipt_qty` bigint NOT NULL,
  `loss_qty` bigint NOT NULL,
  `inbound_qty` bigint NOT NULL,
  `erp_stock_id` bigint NOT NULL,
  `wms_sku_id` int NOT NULL,
  `wms_stock_id` int NOT NULL,
  `receipt_time` datetime(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
  `total_weight` decimal(18,6) DEFAULT NULL,
  `total_volume` decimal(18,6) DEFAULT NULL,
  `create_time` datetime(6) NOT NULL,
  `tenant_id` bigint NOT NULL,
  PRIMARY KEY (`id`) USING BTREE,
  UNIQUE KEY `IX_wms_erp_receipt_item_receipt_id_source_item_key` (`receipt_id`,`source_item_key`) USING BTREE,
  KEY `IX_wms_erp_receipt_item_tenant_time` (`tenant_id`,`receipt_time`) USING BTREE,
  KEY `IX_wms_erp_receipt_item_area` (`warehouse_area_id`) USING BTREE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci ROW_FORMAT=DYNAMIC;
/*!40101 SET character_set_client = @saved_cs_client */;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `wms_flowset` (
  `id` int NOT NULL AUTO_INCREMENT,
  `flowsetmain_id` int NOT NULL,
  `is_origin` tinyint(1) NOT NULL,
  `is_end` tinyint(1) NOT NULL,
  `node_guid` longtext CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL,
  `node_name` longtext CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL,
  `prev_node_guid` longtext CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL,
  PRIMARY KEY (`id`) USING BTREE,
  KEY `IX_wms_flowset_flowsetmain_id` (`flowsetmain_id`) USING BTREE,
  CONSTRAINT `FK_wms_flowset_wms_flowsetmain_flowsetmain_id` FOREIGN KEY (`flowsetmain_id`) REFERENCES `wms_flowsetmain` (`id`) ON DELETE CASCADE ON UPDATE RESTRICT
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci ROW_FORMAT=DYNAMIC;
/*!40101 SET character_set_client = @saved_cs_client */;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `wms_flowsetfilter` (
  `id` int NOT NULL AUTO_INCREMENT,
  `flowset_id` int NOT NULL,
  `flowsetmain_id` int NOT NULL,
  `node_guid` longtext CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL,
  `logic` longtext CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL,
  `c1` longtext CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL,
  `col_label` longtext CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL,
  `col_name` longtext CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL,
  `compare` longtext CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL,
  `content` longtext CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL,
  `c2` longtext CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL,
  `sort` int NOT NULL,
  `condition_group` longtext CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL,
  `formulas` longtext CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL,
  `assert_mode` longtext CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL,
  `table_name` longtext CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL,
  `scheme_name` longtext CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL,
  PRIMARY KEY (`id`) USING BTREE,
  KEY `IX_wms_flowsetfilter_flowset_id` (`flowset_id`) USING BTREE,
  CONSTRAINT `FK_wms_flowsetfilter_wms_flowset_flowset_id` FOREIGN KEY (`flowset_id`) REFERENCES `wms_flowset` (`id`) ON DELETE CASCADE ON UPDATE RESTRICT
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci ROW_FORMAT=DYNAMIC;
/*!40101 SET character_set_client = @saved_cs_client */;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `wms_flowsetmain` (
  `id` int NOT NULL AUTO_INCREMENT,
  `menu` longtext CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL,
  `flow_name` longtext CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL,
  `creator` longtext CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL,
  `create_time` datetime(6) NOT NULL,
  `tenant_id` bigint NOT NULL,
  PRIMARY KEY (`id`) USING BTREE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci ROW_FORMAT=DYNAMIC;
/*!40101 SET character_set_client = @saved_cs_client */;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `wms_flowsetusers` (
  `id` int NOT NULL AUTO_INCREMENT,
  `flowset_id` int NOT NULL,
  `flowsetmain_id` int NOT NULL,
  `node_guid` longtext CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL,
  `user_id` int NOT NULL,
  PRIMARY KEY (`id`) USING BTREE,
  KEY `IX_wms_flowsetusers_flowset_id` (`flowset_id`) USING BTREE,
  CONSTRAINT `FK_wms_flowsetusers_wms_flowset_flowset_id` FOREIGN KEY (`flowset_id`) REFERENCES `wms_flowset` (`id`) ON DELETE CASCADE ON UPDATE RESTRICT
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci ROW_FORMAT=DYNAMIC;
/*!40101 SET character_set_client = @saved_cs_client */;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `wms_freightfee` (
  `id` int NOT NULL AUTO_INCREMENT,
  `carrier` longtext CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL,
  `departure_city` longtext CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL,
  `arrival_city` longtext CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL,
  `price_per_weight` decimal(18,2) NOT NULL,
  `price_per_volume` decimal(18,2) NOT NULL,
  `min_payment` decimal(18,2) NOT NULL,
  `creator` longtext CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL,
  `create_time` datetime(6) NOT NULL,
  `last_update_time` datetime(6) NOT NULL,
  `is_valid` tinyint(1) NOT NULL,
  `tenant_id` bigint NOT NULL,
  PRIMARY KEY (`id`) USING BTREE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci ROW_FORMAT=DYNAMIC;
/*!40101 SET character_set_client = @saved_cs_client */;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `wms_global_unique_serial` (
  `id` int NOT NULL AUTO_INCREMENT,
  `table_name` longtext CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL,
  `prefix_char` longtext CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL,
  `reset_rule` longtext CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL,
  `current_no` int NOT NULL,
  `last_update_time` datetime(6) NOT NULL,
  `tenant_id` bigint NOT NULL,
  PRIMARY KEY (`id`) USING BTREE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci ROW_FORMAT=DYNAMIC;
/*!40101 SET character_set_client = @saved_cs_client */;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `wms_goodslocation` (
  `id` int NOT NULL AUTO_INCREMENT,
  `warehouse_id` int NOT NULL,
  `warehouse_name` longtext CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL,
  `warehouse_area_name` longtext CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL,
  `warehouse_area_property` tinyint unsigned NOT NULL,
  `location_name` longtext CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL,
  `location_length` decimal(18,2) NOT NULL,
  `location_width` decimal(18,2) NOT NULL,
  `location_heigth` decimal(18,2) NOT NULL,
  `location_volume` decimal(18,2) NOT NULL,
  `location_load` decimal(18,2) NOT NULL,
  `roadway_number` longtext CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL,
  `shelf_number` longtext CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL,
  `layer_number` longtext CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL,
  `tag_number` longtext CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL,
  `create_time` datetime(6) NOT NULL,
  `last_update_time` datetime(6) NOT NULL,
  `is_valid` tinyint(1) NOT NULL,
  `tenant_id` bigint NOT NULL,
  `warehouse_area_id` int NOT NULL,
  PRIMARY KEY (`id`) USING BTREE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci ROW_FORMAT=DYNAMIC;
/*!40101 SET character_set_client = @saved_cs_client */;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `wms_goodsowner` (
  `id` int NOT NULL AUTO_INCREMENT,
  `goods_owner_name` longtext CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL,
  `city` longtext CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL,
  `address` longtext CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL,
  `manager` longtext CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL,
  `contact_tel` longtext CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL,
  `creator` longtext CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL,
  `create_time` datetime(6) NOT NULL,
  `last_update_time` datetime(6) NOT NULL,
  `is_valid` tinyint(1) NOT NULL,
  `tenant_id` bigint NOT NULL,
  PRIMARY KEY (`id`) USING BTREE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci ROW_FORMAT=DYNAMIC;
/*!40101 SET character_set_client = @saved_cs_client */;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `wms_menu` (
  `id` int NOT NULL AUTO_INCREMENT,
  `menu_name` longtext CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL,
  `module` longtext CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL,
  `vue_path` longtext CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL,
  `vue_path_detail` longtext CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL,
  `vue_directory` longtext CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL,
  `sort` int NOT NULL,
  `tenant_id` bigint NOT NULL,
  `menu_actions` longtext CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL,
  PRIMARY KEY (`id`) USING BTREE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci ROW_FORMAT=DYNAMIC;
/*!40101 SET character_set_client = @saved_cs_client */;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `wms_packing_task_stock_selection` (
  `id` int NOT NULL AUTO_INCREMENT,
  `tenant_id` bigint NOT NULL,
  `sellfox_task_id` bigint NOT NULL,
  `sellfox_item_id` bigint NOT NULL,
  `wms_sku_id` int NOT NULL,
  `stock_id` int NOT NULL,
  `qty` int NOT NULL,
  `goods_location_id` int NOT NULL,
  `goods_owner_id` int NOT NULL,
  `sku_code` varchar(64) NOT NULL,
  `selected_by` bigint NOT NULL,
  `selected_by_name` varchar(128) NOT NULL,
  `create_time` datetime(6) NOT NULL,
  `last_update_time` datetime(6) NOT NULL,
  PRIMARY KEY (`id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
/*!40101 SET character_set_client = @saved_cs_client */;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `wms_receipt_item_owner` (
  `id` int NOT NULL AUTO_INCREMENT COMMENT '主键',
  `receipt_item_id` int NOT NULL COMMENT 'wms_erp_receipt_item.id',
  `warehouse_area_id` int NOT NULL COMMENT 'wms_warehousearea.id',
  `warehouse_area_name` varchar(128) CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL DEFAULT '' COMMENT '库区名称快照',
  `goods_owner_id` int NOT NULL COMMENT 'wms_goodsowner.id',
  `goods_owner_name` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL DEFAULT '' COMMENT '库存所属人名称快照',
  `qty` bigint NOT NULL DEFAULT '0' COMMENT '分配数量',
  `create_time` datetime(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6) COMMENT '创建时间',
  `tenant_id` bigint NOT NULL COMMENT '租户ID',
  PRIMARY KEY (`id`) USING BTREE,
  UNIQUE KEY `uk_receipt_area_owner` (`receipt_item_id`,`warehouse_area_id`,`goods_owner_id`) USING BTREE,
  KEY `idx_receipt_item_id` (`receipt_item_id`) USING BTREE,
  KEY `idx_tenant_area` (`tenant_id`,`warehouse_area_id`) USING BTREE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci ROW_FORMAT=DYNAMIC COMMENT='ModernWMS收货明细按库区/所属人分配数量';
/*!40101 SET character_set_client = @saved_cs_client */;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `wms_role_warehouse` (
  `id` int NOT NULL AUTO_INCREMENT,
  `role_id` int NOT NULL,
  `warehouse_id` bigint NOT NULL,
  `tenant_id` bigint NOT NULL,
  `created_by` int NOT NULL,
  `create_time` datetime(6) NOT NULL,
  `last_update_time` datetime(6) NOT NULL,
  PRIMARY KEY (`id`) USING BTREE,
  UNIQUE KEY `IX_wms_role_warehouse_role_id_warehouse_id` (`role_id`,`warehouse_id`) USING BTREE,
  KEY `IX_wms_role_warehouse_warehouse_id` (`warehouse_id`) USING BTREE,
  CONSTRAINT `FK_wms_role_warehouse_wms_userrole_role_id` FOREIGN KEY (`role_id`) REFERENCES `wms_userrole` (`id`) ON DELETE CASCADE ON UPDATE RESTRICT
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci ROW_FORMAT=DYNAMIC;
/*!40101 SET character_set_client = @saved_cs_client */;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `wms_rolemenu` (
  `id` int NOT NULL AUTO_INCREMENT,
  `userrole_id` int NOT NULL,
  `menu_id` int NOT NULL,
  `authority` tinyint unsigned NOT NULL,
  `create_time` datetime(6) NOT NULL,
  `last_update_time` datetime(6) NOT NULL,
  `tenant_id` bigint NOT NULL,
  `menu_actions_authority` longtext CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL,
  PRIMARY KEY (`id`) USING BTREE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci ROW_FORMAT=DYNAMIC;
/*!40101 SET character_set_client = @saved_cs_client */;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `wms_sku` (
  `id` int NOT NULL AUTO_INCREMENT,
  `spu_id` int NOT NULL,
  `sku_code` longtext CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL,
  `sku_name` longtext CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL,
  `bar_code` longtext CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL,
  `weight` decimal(18,2) NOT NULL,
  `lenght` decimal(18,2) NOT NULL,
  `width` decimal(18,2) NOT NULL,
  `height` decimal(18,2) NOT NULL,
  `volume` decimal(18,2) NOT NULL,
  `unit` longtext CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL,
  `cost` decimal(18,2) NOT NULL,
  `price` decimal(18,2) NOT NULL,
  `create_time` datetime(6) NOT NULL,
  `last_update_time` datetime(6) NOT NULL,
  PRIMARY KEY (`id`) USING BTREE,
  KEY `IX_wms_sku_spu_id` (`spu_id`) USING BTREE,
  CONSTRAINT `FK_wms_sku_wms_spu_spu_id` FOREIGN KEY (`spu_id`) REFERENCES `wms_spu` (`id`) ON DELETE CASCADE ON UPDATE RESTRICT
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci ROW_FORMAT=DYNAMIC;
/*!40101 SET character_set_client = @saved_cs_client */;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `wms_sku_safety_stock` (
  `id` int NOT NULL AUTO_INCREMENT,
  `sku_id` int NOT NULL,
  `warehouse_id` int NOT NULL,
  `safety_stock_qty` int NOT NULL,
  PRIMARY KEY (`id`) USING BTREE,
  KEY `IX_wms_sku_safety_stock_sku_id` (`sku_id`) USING BTREE,
  CONSTRAINT `FK_wms_sku_safety_stock_wms_sku_sku_id` FOREIGN KEY (`sku_id`) REFERENCES `wms_sku` (`id`) ON DELETE CASCADE ON UPDATE RESTRICT
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci ROW_FORMAT=DYNAMIC;
/*!40101 SET character_set_client = @saved_cs_client */;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `wms_spu` (
  `id` int NOT NULL AUTO_INCREMENT,
  `spu_code` longtext CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL,
  `spu_name` longtext CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL,
  `spu_description` longtext CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL,
  `supplier_id` int NOT NULL,
  `supplier_name` longtext CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL,
  `brand` longtext CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL,
  `origin` longtext CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL,
  `length_unit` tinyint unsigned NOT NULL,
  `volume_unit` tinyint unsigned NOT NULL,
  `weight_unit` tinyint unsigned NOT NULL,
  `creator` longtext CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL,
  `create_time` datetime(6) NOT NULL,
  `last_update_time` datetime(6) NOT NULL,
  `is_valid` tinyint(1) NOT NULL,
  `tenant_id` bigint NOT NULL,
  PRIMARY KEY (`id`) USING BTREE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci ROW_FORMAT=DYNAMIC;
/*!40101 SET character_set_client = @saved_cs_client */;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `wms_stock` (
  `id` int NOT NULL AUTO_INCREMENT,
  `sku_id` int NOT NULL,
  `goods_location_id` int NOT NULL,
  `qty` int NOT NULL,
  `goods_owner_id` int NOT NULL,
  `is_freeze` tinyint(1) NOT NULL,
  `last_update_time` datetime(6) NOT NULL,
  `tenant_id` bigint NOT NULL,
  `series_number` longtext CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL,
  `expiry_date` datetime(6) NOT NULL,
  `price` decimal(18,2) NOT NULL,
  `putaway_date` datetime(6) NOT NULL,
  PRIMARY KEY (`id`) USING BTREE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci ROW_FORMAT=DYNAMIC;
/*!40101 SET character_set_client = @saved_cs_client */;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `wms_stock_record` (
  `id` int NOT NULL AUTO_INCREMENT,
  `record_no` varchar(64) CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL,
  `biz_type` varchar(32) CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL,
  `biz_id` bigint NOT NULL,
  `biz_item_id` bigint NOT NULL,
  `stock_id` int NOT NULL,
  `sku_id` int NOT NULL,
  `goods_location_id` int NOT NULL,
  `goods_owner_id` int NOT NULL,
  `change_qty` bigint NOT NULL,
  `before_qty` bigint NOT NULL,
  `after_qty` bigint NOT NULL,
  `direction` varchar(8) CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL,
  `operator_id` int NOT NULL,
  `operator_name` varchar(128) CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL,
  `remark` varchar(500) CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL,
  `operate_time` datetime(6) NOT NULL,
  `tenant_id` bigint NOT NULL,
  PRIMARY KEY (`id`) USING BTREE,
  UNIQUE KEY `UX_wms_stock_record_biz` (`biz_type`,`biz_id`,`biz_item_id`,`stock_id`) USING BTREE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci ROW_FORMAT=DYNAMIC;
/*!40101 SET character_set_client = @saved_cs_client */;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `wms_stockadjust` (
  `id` int NOT NULL AUTO_INCREMENT,
  `job_code` longtext CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL,
  `sku_id` int NOT NULL,
  `goods_owner_id` int NOT NULL,
  `goods_location_id` int NOT NULL,
  `qty` int NOT NULL,
  `creator` longtext CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL,
  `create_time` datetime(6) NOT NULL,
  `last_update_time` datetime(6) NOT NULL,
  `tenant_id` bigint NOT NULL,
  `is_update_stock` tinyint(1) NOT NULL,
  `job_type` tinyint unsigned NOT NULL,
  `source_table_id` int NOT NULL,
  `series_number` longtext CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL,
  `expiry_date` datetime(6) NOT NULL,
  `price` decimal(18,2) NOT NULL,
  `putaway_date` datetime(6) NOT NULL,
  PRIMARY KEY (`id`) USING BTREE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci ROW_FORMAT=DYNAMIC;
/*!40101 SET character_set_client = @saved_cs_client */;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `wms_stockfreeze` (
  `id` int NOT NULL AUTO_INCREMENT,
  `job_code` longtext CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL,
  `job_type` tinyint(1) NOT NULL,
  `sku_id` int NOT NULL,
  `goods_owner_id` int NOT NULL,
  `goods_location_id` int NOT NULL,
  `handler` longtext CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL,
  `handle_time` datetime(6) NOT NULL,
  `last_update_time` datetime(6) NOT NULL,
  `tenant_id` bigint NOT NULL,
  `series_number` longtext CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL,
  PRIMARY KEY (`id`) USING BTREE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci ROW_FORMAT=DYNAMIC;
/*!40101 SET character_set_client = @saved_cs_client */;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `wms_stockmove` (
  `id` int NOT NULL AUTO_INCREMENT,
  `job_code` longtext CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL,
  `move_status` tinyint unsigned NOT NULL,
  `sku_id` int NOT NULL,
  `orig_goods_location_id` int NOT NULL,
  `dest_googs_location_id` int NOT NULL,
  `qty` int NOT NULL,
  `goods_owner_id` int NOT NULL,
  `handler` longtext CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL,
  `handle_time` datetime(6) NOT NULL,
  `creator` longtext CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL,
  `create_time` datetime(6) NOT NULL,
  `last_update_time` datetime(6) NOT NULL,
  `tenant_id` bigint NOT NULL,
  `series_number` longtext CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL,
  `expiry_date` datetime(6) NOT NULL,
  `price` decimal(18,2) NOT NULL,
  `putaway_date` datetime(6) NOT NULL,
  PRIMARY KEY (`id`) USING BTREE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci ROW_FORMAT=DYNAMIC;
/*!40101 SET character_set_client = @saved_cs_client */;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `wms_stockprocess` (
  `id` int NOT NULL AUTO_INCREMENT,
  `job_code` longtext CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL,
  `job_type` tinyint(1) NOT NULL,
  `process_status` tinyint(1) NOT NULL,
  `processor` longtext CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL,
  `process_time` datetime(6) NOT NULL,
  `creator` longtext CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL,
  `create_time` datetime(6) NOT NULL,
  `last_update_time` datetime(6) NOT NULL,
  `tenant_id` bigint NOT NULL,
  PRIMARY KEY (`id`) USING BTREE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci ROW_FORMAT=DYNAMIC;
/*!40101 SET character_set_client = @saved_cs_client */;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `wms_stockprocessdetail` (
  `id` int NOT NULL AUTO_INCREMENT,
  `stock_process_id` int NOT NULL,
  `sku_id` int NOT NULL,
  `goods_owner_id` int NOT NULL,
  `goods_location_id` int NOT NULL,
  `qty` int NOT NULL,
  `last_update_time` datetime(6) NOT NULL,
  `tenant_id` bigint NOT NULL,
  `is_source` tinyint(1) NOT NULL,
  `is_update_stock` tinyint(1) NOT NULL,
  `series_number` longtext CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL,
  `expiry_date` datetime(6) NOT NULL,
  `price` decimal(18,2) NOT NULL,
  `putaway_date` datetime(6) NOT NULL,
  PRIMARY KEY (`id`) USING BTREE,
  KEY `IX_wms_stockprocessdetail_stock_process_id` (`stock_process_id`) USING BTREE,
  CONSTRAINT `FK_wms_stockprocessdetail_wms_stockprocess_stock_process_id` FOREIGN KEY (`stock_process_id`) REFERENCES `wms_stockprocess` (`id`) ON DELETE CASCADE ON UPDATE RESTRICT
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci ROW_FORMAT=DYNAMIC;
/*!40101 SET character_set_client = @saved_cs_client */;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `wms_stocktaking` (
  `id` int NOT NULL AUTO_INCREMENT,
  `job_code` longtext CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL,
  `job_status` tinyint(1) NOT NULL,
  `sku_id` int NOT NULL,
  `goods_owner_id` int NOT NULL,
  `goods_location_id` int NOT NULL,
  `series_number` longtext CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL,
  `expiry_date` datetime(6) NOT NULL,
  `price` decimal(18,2) NOT NULL,
  `putaway_date` datetime(6) NOT NULL,
  `book_qty` int NOT NULL,
  `counted_qty` int NOT NULL,
  `difference_qty` int NOT NULL,
  `creator` longtext CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL,
  `create_time` datetime(6) NOT NULL,
  `last_update_time` datetime(6) NOT NULL,
  `tenant_id` bigint NOT NULL,
  `handler` longtext CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL,
  `handle_time` datetime(6) NOT NULL,
  PRIMARY KEY (`id`) USING BTREE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci ROW_FORMAT=DYNAMIC;
/*!40101 SET character_set_client = @saved_cs_client */;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `wms_supplier` (
  `id` int NOT NULL AUTO_INCREMENT,
  `supplier_name` longtext CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL,
  `city` longtext CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL,
  `address` longtext CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL,
  `email` longtext CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL,
  `manager` longtext CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL,
  `contact_tel` longtext CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL,
  `creator` longtext CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL,
  `create_time` datetime(6) NOT NULL,
  `last_update_time` datetime(6) NOT NULL,
  `is_valid` tinyint(1) NOT NULL,
  `tenant_id` bigint NOT NULL,
  PRIMARY KEY (`id`) USING BTREE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci ROW_FORMAT=DYNAMIC;
/*!40101 SET character_set_client = @saved_cs_client */;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `wms_user` (
  `id` int NOT NULL AUTO_INCREMENT,
  `user_num` longtext CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL,
  `user_name` longtext CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL,
  `contact_tel` longtext CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL,
  `user_role` longtext CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL,
  `sex` longtext CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL,
  `is_valid` tinyint(1) NOT NULL,
  `auth_string` longtext CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL,
  `email` longtext CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL,
  `creator` longtext CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL,
  `create_time` datetime(6) NOT NULL,
  `last_update_time` datetime(6) NOT NULL,
  `tenant_id` bigint NOT NULL,
  PRIMARY KEY (`id`) USING BTREE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci ROW_FORMAT=DYNAMIC;
/*!40101 SET character_set_client = @saved_cs_client */;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `wms_user_defined_print_solution` (
  `id` int NOT NULL AUTO_INCREMENT,
  `vue_path` longtext CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL,
  `tab_page` longtext CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL,
  `solution_name` longtext CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL,
  `config_json` longtext CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL,
  `report_length` decimal(18,2) NOT NULL,
  `report_width` decimal(18,2) NOT NULL,
  `report_direction` longtext CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL,
  `last_update_time` datetime(6) NOT NULL,
  `tenant_id` bigint NOT NULL,
  PRIMARY KEY (`id`) USING BTREE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci ROW_FORMAT=DYNAMIC;
/*!40101 SET character_set_client = @saved_cs_client */;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `wms_userrole` (
  `id` int NOT NULL AUTO_INCREMENT,
  `role_name` longtext CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL,
  `is_valid` tinyint(1) NOT NULL,
  `create_time` datetime(6) NOT NULL,
  `last_update_time` datetime(6) NOT NULL,
  `tenant_id` bigint NOT NULL,
  PRIMARY KEY (`id`) USING BTREE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci ROW_FORMAT=DYNAMIC;
/*!40101 SET character_set_client = @saved_cs_client */;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `wms_warehouse` (
  `id` int NOT NULL AUTO_INCREMENT,
  `warehouse_name` longtext CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL,
  `city` longtext CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL,
  `address` longtext CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL,
  `email` longtext CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL,
  `manager` longtext CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL,
  `contact_tel` longtext CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL,
  `creator` longtext CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL,
  `create_time` datetime(6) NOT NULL,
  `last_update_time` datetime(6) NOT NULL,
  `is_valid` tinyint(1) NOT NULL,
  `tenant_id` bigint NOT NULL,
  `erp_warehouse_id` bigint DEFAULT NULL,
  PRIMARY KEY (`id`) USING BTREE,
  UNIQUE KEY `IX_wms_warehouse_erp_warehouse_id` (`erp_warehouse_id`) USING BTREE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci ROW_FORMAT=DYNAMIC;
/*!40101 SET character_set_client = @saved_cs_client */;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `wms_warehouse_operator_group` (
  `id` bigint NOT NULL AUTO_INCREMENT COMMENT '主键',
  `tenant_id` bigint NOT NULL COMMENT 'ModernWMS租户ID',
  `warehouse_id` int NOT NULL COMMENT 'ModernWMS仓库ID（跨库逻辑外键）',
  `dept_id` bigint NOT NULL COMMENT 'system_dept.id（操作小组）',
  `creator` varchar(64) CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL DEFAULT '' COMMENT '创建者',
  `create_time` datetime NOT NULL DEFAULT CURRENT_TIMESTAMP COMMENT '创建时间',
  PRIMARY KEY (`id`) USING BTREE,
  UNIQUE KEY `uk_tenant_warehouse_dept` (`tenant_id`,`warehouse_id`,`dept_id`) USING BTREE,
  KEY `idx_tenant_warehouse` (`tenant_id`,`warehouse_id`) USING BTREE,
  KEY `idx_dept_id` (`dept_id`) USING BTREE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci ROW_FORMAT=DYNAMIC COMMENT='ModernWMS仓库与ERP操作小组绑定关系';
/*!40101 SET character_set_client = @saved_cs_client */;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `wms_warehousearea` (
  `id` int NOT NULL AUTO_INCREMENT,
  `warehouse_id` int NOT NULL,
  `area_name` longtext CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL,
  `parent_id` int NOT NULL,
  `create_time` datetime(6) NOT NULL,
  `last_update_time` datetime(6) NOT NULL,
  `is_valid` tinyint(1) NOT NULL,
  `tenant_id` bigint NOT NULL,
  `area_property` tinyint unsigned NOT NULL,
  `sort` int NOT NULL DEFAULT '0',
  PRIMARY KEY (`id`) USING BTREE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci ROW_FORMAT=DYNAMIC;
/*!40101 SET character_set_client = @saved_cs_client */;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `wms_warehousearea_operator_group` (
  `id` int NOT NULL AUTO_INCREMENT COMMENT '主键',
  `tenant_id` bigint NOT NULL COMMENT 'ModernWMS租户ID',
  `warehouse_area_id` int NOT NULL COMMENT 'wms_warehousearea.id',
  `dept_id` bigint NOT NULL COMMENT 'system_dept.id（操作小组）',
  `creator` varchar(64) CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL DEFAULT '' COMMENT '创建者',
  `create_time` datetime NOT NULL DEFAULT CURRENT_TIMESTAMP COMMENT '创建时间',
  PRIMARY KEY (`id`) USING BTREE,
  UNIQUE KEY `uk_tenant_area_dept` (`tenant_id`,`warehouse_area_id`,`dept_id`) USING BTREE,
  UNIQUE KEY `UX_wms_warehousearea_operator_group_tenant_dept` (`tenant_id`,`dept_id`) USING BTREE,
  KEY `idx_tenant_area` (`tenant_id`,`warehouse_area_id`) USING BTREE,
  KEY `idx_dept_id` (`dept_id`) USING BTREE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci ROW_FORMAT=DYNAMIC COMMENT='ModernWMS库区与ERP操作小组绑定关系';
/*!40101 SET character_set_client = @saved_cs_client */;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `wms_weighing_box` (
  `id` int NOT NULL AUTO_INCREMENT,
  `packing_task_id` int NOT NULL,
  `box_identity` varchar(64) CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL,
  `source_box_identity` varchar(256) CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL,
  `box_sequence` int NOT NULL,
  `weight` decimal(18,2) DEFAULT NULL,
  `length` decimal(18,2) DEFAULT NULL,
  `width` decimal(18,2) DEFAULT NULL,
  `height` decimal(18,2) DEFAULT NULL,
  `measurement_status` varchar(16) CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL,
  `measured_by` int DEFAULT NULL,
  `measured_by_name` varchar(128) CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL,
  `measured_at` datetime(6) DEFAULT NULL,
  `copied_from_box_id` int DEFAULT NULL,
  `source_snapshot` longtext CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL,
  `is_invalidated` tinyint(1) NOT NULL,
  `invalidated_at` datetime(6) DEFAULT NULL,
  `create_time` datetime(6) NOT NULL,
  `last_update_time` datetime(6) NOT NULL,
  `row_version` bigint NOT NULL,
  PRIMARY KEY (`id`) USING BTREE,
  UNIQUE KEY `IX_wms_weighing_box_packing_task_id_source_box_identity` (`packing_task_id`,`source_box_identity`) USING BTREE,
  KEY `IX_wms_weighing_box_copied_from_box_id` (`copied_from_box_id`) USING BTREE,
  KEY `IX_wms_weighing_box_packing_task_id_measurement_status` (`packing_task_id`,`measurement_status`) USING BTREE,
  CONSTRAINT `FK_wms_weighing_box_wms_dispatch_packing_task_packing_task_id` FOREIGN KEY (`packing_task_id`) REFERENCES `wms_dispatch_packing_task` (`id`) ON DELETE CASCADE ON UPDATE RESTRICT,
  CONSTRAINT `FK_wms_weighing_box_wms_weighing_box_copied_from_box_id` FOREIGN KEY (`copied_from_box_id`) REFERENCES `wms_weighing_box` (`id`) ON DELETE RESTRICT ON UPDATE RESTRICT
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci ROW_FORMAT=DYNAMIC;
/*!40101 SET character_set_client = @saved_cs_client */;
SET FOREIGN_KEY_CHECKS=1;
