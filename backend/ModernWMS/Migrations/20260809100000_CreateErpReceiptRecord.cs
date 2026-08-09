using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ModernWMS.Core.DBContext;
using MySql.EntityFrameworkCore.Metadata;

#nullable disable

namespace ModernWMS.Migrations;

/// <summary>
/// Creates the ModernWMS-owned ERP logistics receipt record table.
/// </summary>
[DbContext(typeof(SqlDBContext))]
[Migration("20260809100000_CreateErpReceiptRecord")]
public partial class CreateErpReceiptRecord : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "wms_erp_receipt",
            columns: table => new
            {
                id = table.Column<int>(type: "int", nullable: false)
                    .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                shipment_id = table.Column<long>(type: "bigint", nullable: false),
                source_version = table.Column<int>(type: "int", nullable: false),
                actual_receipt_qty = table.Column<long>(type: "bigint", nullable: false),
                loss_qty = table.Column<long>(type: "bigint", nullable: false),
                inbound_qty = table.Column<long>(type: "bigint", nullable: false),
                receipt_freight_payment_status = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false),
                receipt_freight_amount = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                receipt_freight_files_json = table.Column<string>(type: "longtext", nullable: false),
                receipt_files_json = table.Column<string>(type: "longtext", nullable: false),
                loss_reason = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: false),
                loss_files_json = table.Column<string>(type: "longtext", nullable: false),
                receipt_remark = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: false),
                creator = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                create_time = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                last_update_time = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                tenant_id = table.Column<long>(type: "bigint", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_wms_erp_receipt", x => x.id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_wms_erp_receipt_shipment_id",
            table: "wms_erp_receipt",
            column: "shipment_id",
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "wms_erp_receipt");
    }
}
