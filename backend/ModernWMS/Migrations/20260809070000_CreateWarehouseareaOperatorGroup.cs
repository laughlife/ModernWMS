using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ModernWMS.Core.DBContext;

#nullable disable

namespace ModernWMS.Migrations;

/// <summary>
/// Creates the ModernWMS warehouse-area and operator-group relation table.
/// </summary>
[DbContext(typeof(SqlDBContext))]
[Migration("20260809070000_CreateWarehouseareaOperatorGroup")]
public partial class CreateWarehouseareaOperatorGroup : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
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
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // 关系表可能已经包含业务绑定，回滚时不自动删除。
    }
}
