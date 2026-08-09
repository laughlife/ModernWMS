CREATE TABLE IF NOT EXISTS `wms_warehousearea_operator_group` (
  `id` INT NOT NULL AUTO_INCREMENT COMMENT '主键',
  `tenant_id` BIGINT NOT NULL COMMENT 'ModernWMS租户ID',
  `warehouse_area_id` INT NOT NULL COMMENT 'wms_warehousearea.id',
  `dept_id` BIGINT NOT NULL COMMENT 'system_dept.id（操作小组）',
  `creator` VARCHAR(64) NOT NULL DEFAULT '' COMMENT '创建者',
  `create_time` DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP COMMENT '创建时间',
  PRIMARY KEY (`id`),
  UNIQUE KEY `uk_tenant_area_dept` (`tenant_id`, `warehouse_area_id`, `dept_id`),
  KEY `idx_tenant_area` (`tenant_id`, `warehouse_area_id`),
  KEY `idx_dept_id` (`dept_id`)
) ENGINE=InnoDB
  DEFAULT CHARSET=utf8mb4
  COLLATE=utf8mb4_0900_ai_ci
  COMMENT='ModernWMS库区与ERP操作小组绑定关系';
