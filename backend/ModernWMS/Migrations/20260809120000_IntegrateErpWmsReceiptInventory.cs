using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ModernWMS.Core.DBContext;
using MySql.EntityFrameworkCore.Metadata;

#nullable disable

namespace ModernWMS.Migrations;

/// <summary>
/// Adds product-level receipt details and ERP-to-WMS inventory mappings.
/// </summary>
[DbContext(typeof(SqlDBContext))]
[Migration("20260809120000_IntegrateErpWmsReceiptInventory")]
public partial class IntegrateErpWmsReceiptInventory : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_wms_warehouse_erp_warehouse_id",
            table: "wms_warehouse");
        migrationBuilder.CreateIndex(
            name: "IX_wms_warehouse_erp_warehouse_id",
            table: "wms_warehouse",
            column: "erp_warehouse_id",
            unique: true);

        migrationBuilder.CreateTable(
            name: "wms_erp_commodity_map",
            columns: table => new
            {
                id = table.Column<int>(type: "int", nullable: false)
                    .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                erp_commodity_id = table.Column<long>(type: "bigint", nullable: false),
                wms_spu_id = table.Column<int>(type: "int", nullable: false),
                wms_sku_id = table.Column<int>(type: "int", nullable: false),
                commodity_sku = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false),
                last_sync_time = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                tenant_id = table.Column<long>(type: "bigint", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_wms_erp_commodity_map", x => x.id));

        migrationBuilder.CreateTable(
            name: "wms_erp_goods_owner_map",
            columns: table => new
            {
                id = table.Column<int>(type: "int", nullable: false)
                    .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                erp_dept_id = table.Column<long>(type: "bigint", nullable: false),
                erp_order_user_id = table.Column<long>(type: "bigint", nullable: false),
                wms_goods_owner_id = table.Column<int>(type: "int", nullable: false),
                dept_name = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false),
                order_user_name = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false),
                last_sync_time = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                tenant_id = table.Column<long>(type: "bigint", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_wms_erp_goods_owner_map", x => x.id));

        migrationBuilder.CreateTable(
            name: "wms_erp_receipt_item",
            columns: table => new
            {
                id = table.Column<int>(type: "int", nullable: false)
                    .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                receipt_id = table.Column<int>(type: "int", nullable: false),
                shipment_id = table.Column<long>(type: "bigint", nullable: false),
                source_item_key = table.Column<string>(type: "varchar(160)", maxLength: 160, nullable: false),
                task_item_id = table.Column<long>(type: "bigint", nullable: true),
                allocation_id = table.Column<long>(type: "bigint", nullable: true),
                commodity_id = table.Column<long>(type: "bigint", nullable: true),
                commodity_sku = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false),
                commodity_name = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: false),
                dept_id = table.Column<long>(type: "bigint", nullable: true),
                order_user_id = table.Column<long>(type: "bigint", nullable: true),
                shipment_qty = table.Column<long>(type: "bigint", nullable: false),
                actual_receipt_qty = table.Column<long>(type: "bigint", nullable: false),
                loss_qty = table.Column<long>(type: "bigint", nullable: false),
                inbound_qty = table.Column<long>(type: "bigint", nullable: false),
                erp_stock_id = table.Column<long>(type: "bigint", nullable: false),
                wms_sku_id = table.Column<int>(type: "int", nullable: false),
                wms_stock_id = table.Column<int>(type: "int", nullable: false),
                create_time = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                tenant_id = table.Column<long>(type: "bigint", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_wms_erp_receipt_item", x => x.id));

        migrationBuilder.CreateTable(
            name: "wms_stock_record",
            columns: table => new
            {
                id = table.Column<int>(type: "int", nullable: false)
                    .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                record_no = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                biz_type = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                biz_id = table.Column<long>(type: "bigint", nullable: false),
                biz_item_id = table.Column<long>(type: "bigint", nullable: false),
                stock_id = table.Column<int>(type: "int", nullable: false),
                sku_id = table.Column<int>(type: "int", nullable: false),
                goods_location_id = table.Column<int>(type: "int", nullable: false),
                goods_owner_id = table.Column<int>(type: "int", nullable: false),
                change_qty = table.Column<long>(type: "bigint", nullable: false),
                before_qty = table.Column<long>(type: "bigint", nullable: false),
                after_qty = table.Column<long>(type: "bigint", nullable: false),
                direction = table.Column<string>(type: "varchar(8)", maxLength: 8, nullable: false),
                operator_id = table.Column<int>(type: "int", nullable: false),
                operator_name = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false),
                remark = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: false),
                operate_time = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                tenant_id = table.Column<long>(type: "bigint", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_wms_stock_record", x => x.id));

        migrationBuilder.CreateIndex(
            name: "IX_wms_erp_commodity_map_tenant_id_erp_commodity_id",
            table: "wms_erp_commodity_map",
            columns: new[] { "tenant_id", "erp_commodity_id" },
            unique: true);
        migrationBuilder.CreateIndex(
            name: "UX_wms_owner_map_erp_owner",
            table: "wms_erp_goods_owner_map",
            columns: new[] { "tenant_id", "erp_dept_id", "erp_order_user_id" },
            unique: true);
        migrationBuilder.CreateIndex(
            name: "IX_wms_erp_receipt_item_receipt_id_source_item_key",
            table: "wms_erp_receipt_item",
            columns: new[] { "receipt_id", "source_item_key" },
            unique: true);
        migrationBuilder.CreateIndex(
            name: "UX_wms_stock_record_biz",
            table: "wms_stock_record",
            columns: new[] { "biz_type", "biz_id", "biz_item_id", "stock_id" },
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "wms_erp_receipt_item");
        migrationBuilder.DropTable(name: "wms_stock_record");
        migrationBuilder.DropTable(name: "wms_erp_goods_owner_map");
        migrationBuilder.DropTable(name: "wms_erp_commodity_map");
        migrationBuilder.DropIndex(
            name: "IX_wms_warehouse_erp_warehouse_id",
            table: "wms_warehouse");
        migrationBuilder.CreateIndex(
            name: "IX_wms_warehouse_erp_warehouse_id",
            table: "wms_warehouse",
            column: "erp_warehouse_id");
    }
}
