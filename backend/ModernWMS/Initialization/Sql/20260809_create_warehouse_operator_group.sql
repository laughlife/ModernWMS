CREATE TABLE IF NOT EXISTS `wms_warehouse_operator_group` (
  `id` BIGINT NOT NULL AUTO_INCREMENT COMMENT '主键',
  `tenant_id` BIGINT NOT NULL COMMENT 'ModernWMS租户ID',
  `warehouse_id` INT NOT NULL COMMENT 'ModernWMS仓库ID（跨库逻辑外键）',
  `dept_id` BIGINT NOT NULL COMMENT 'system_dept.id（操作小组）',
  `creator` VARCHAR(64) NOT NULL DEFAULT '' COMMENT '创建者',
  `create_time` DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP COMMENT '创建时间',
  PRIMARY KEY (`id`),
  UNIQUE KEY `uk_tenant_warehouse_dept` (`tenant_id`, `warehouse_id`, `dept_id`),
  KEY `idx_tenant_warehouse` (`tenant_id`, `warehouse_id`),
  KEY `idx_dept_id` (`dept_id`)
) ENGINE=InnoDB
  DEFAULT CHARSET=utf8mb4
  COLLATE=utf8mb4_0900_ai_ci
  COMMENT='ModernWMS仓库与ERP操作小组绑定关系';
