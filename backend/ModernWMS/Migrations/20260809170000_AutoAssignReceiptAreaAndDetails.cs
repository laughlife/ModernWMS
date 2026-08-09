using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ModernWMS.Core.DBContext;

#nullable disable

namespace ModernWMS.Migrations;

[DbContext(typeof(SqlDBContext))]
[Migration("20260809170000_AutoAssignReceiptAreaAndDetails")]
public partial class AutoAssignReceiptAreaAndDetails : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "dept_name",
            table: "wms_erp_receipt_item",
            type: "varchar(128)",
            maxLength: 128,
            nullable: false,
            defaultValue: "");
        migrationBuilder.AddColumn<string>(
            name: "order_user_name",
            table: "wms_erp_receipt_item",
            type: "varchar(128)",
            maxLength: 128,
            nullable: false,
            defaultValue: "");
        migrationBuilder.AddColumn<int>(
            name: "warehouse_area_id",
            table: "wms_erp_receipt_item",
            type: "int",
            nullable: false,
            defaultValue: 0);
        migrationBuilder.AddColumn<string>(
            name: "warehouse_area_name",
            table: "wms_erp_receipt_item",
            type: "varchar(128)",
            maxLength: 128,
            nullable: false,
            defaultValue: "");
        migrationBuilder.AddColumn<DateTime>(
            name: "receipt_time",
            table: "wms_erp_receipt_item",
            type: "datetime(6)",
            nullable: false,
            defaultValueSql: "CURRENT_TIMESTAMP(6)");
        migrationBuilder.AddColumn<decimal>(
            name: "total_weight",
            table: "wms_erp_receipt_item",
            type: "decimal(18,6)",
            nullable: true);
        migrationBuilder.AddColumn<decimal>(
            name: "total_volume",
            table: "wms_erp_receipt_item",
            type: "decimal(18,6)",
            nullable: true);
        migrationBuilder.CreateIndex(
            name: "IX_wms_erp_receipt_item_tenant_time",
            table: "wms_erp_receipt_item",
            columns: new[] { "tenant_id", "receipt_time" });
        migrationBuilder.CreateIndex(
            name: "IX_wms_erp_receipt_item_area",
            table: "wms_erp_receipt_item",
            column: "warehouse_area_id");
        migrationBuilder.CreateIndex(
            name: "UX_wms_warehousearea_operator_group_tenant_dept",
            table: "wms_warehousearea_operator_group",
            columns: new[] { "tenant_id", "dept_id" },
            unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "UX_wms_warehousearea_operator_group_tenant_dept",
            table: "wms_warehousearea_operator_group");
        migrationBuilder.DropIndex(
            name: "IX_wms_erp_receipt_item_area",
            table: "wms_erp_receipt_item");
        migrationBuilder.DropIndex(
            name: "IX_wms_erp_receipt_item_tenant_time",
            table: "wms_erp_receipt_item");
        migrationBuilder.DropColumn(name: "total_volume", table: "wms_erp_receipt_item");
        migrationBuilder.DropColumn(name: "total_weight", table: "wms_erp_receipt_item");
        migrationBuilder.DropColumn(name: "receipt_time", table: "wms_erp_receipt_item");
        migrationBuilder.DropColumn(name: "warehouse_area_name", table: "wms_erp_receipt_item");
        migrationBuilder.DropColumn(name: "warehouse_area_id", table: "wms_erp_receipt_item");
        migrationBuilder.DropColumn(name: "order_user_name", table: "wms_erp_receipt_item");
        migrationBuilder.DropColumn(name: "dept_name", table: "wms_erp_receipt_item");
    }
}
