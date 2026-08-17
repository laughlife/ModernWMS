using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ModernWMS.Core.DBContext;

#nullable disable

namespace ModernWMS.Migrations;

/// <summary>
/// Creates the receipt-item zone/owner allocation table.
/// </summary>
[DbContext(typeof(SqlDBContext))]
[Migration("20260809180000_CreateReceiptItemOwner")]
public partial class CreateReceiptItemOwner : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            CREATE TABLE IF NOT EXISTS `wms_receipt_item_owner` (
              `id` INT NOT NULL AUTO_INCREMENT COMMENT '主键',
              `receipt_item_id` INT NOT NULL COMMENT 'wms_erp_receipt_item.id',
              `warehouse_area_id` INT NOT NULL COMMENT 'wms_warehousearea.id',
              `warehouse_area_name` VARCHAR(128) NOT NULL DEFAULT '' COMMENT '库区名称快照',
              `goods_owner_id` INT NOT NULL COMMENT 'wms_goodsowner.id',
              `goods_owner_name` VARCHAR(255) NOT NULL DEFAULT '' COMMENT '库存所属人名称快照',
              `qty` BIGINT NOT NULL DEFAULT 0 COMMENT '分配数量',
              `create_time` DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6) COMMENT '创建时间',
              `tenant_id` BIGINT NOT NULL COMMENT '租户ID',
              PRIMARY KEY (`id`),
              UNIQUE KEY `uk_receipt_area_owner` (`receipt_item_id`, `warehouse_area_id`, `goods_owner_id`),
              KEY `idx_receipt_item_id` (`receipt_item_id`),
              KEY `idx_tenant_area` (`tenant_id`, `warehouse_area_id`)
            ) ENGINE=InnoDB
              DEFAULT CHARSET=utf8mb4
              COLLATE=utf8mb4_0900_ai_ci
              COMMENT='ModernWMS收货明细按库区/所属人分配数量';
            """);

        migrationBuilder.Sql(
            """
            UPDATE `wms_goodslocation`
               SET `location_name` = `warehouse_area_name`,
                   `last_update_time` = CURRENT_TIMESTAMP(6)
             WHERE `location_name` = '库区库存位'
               AND `tag_number` LIKE 'AREA-AUTO-%'
               AND `warehouse_area_name` <> '';
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // 分配明细属于已确认收货业务数据，回滚时不自动删除。
    }
}
