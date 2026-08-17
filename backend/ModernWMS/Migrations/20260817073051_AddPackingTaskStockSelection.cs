using System;
using Microsoft.EntityFrameworkCore.Migrations;
using MySql.EntityFrameworkCore.Metadata;

#nullable disable

namespace ModernWMS.Migrations
{
    /// <inheritdoc />
    public partial class AddPackingTaskStockSelection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "wms_packing_task_stock_selection",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    tenant_id = table.Column<long>(type: "bigint", nullable: false),
                    sellfox_task_id = table.Column<long>(type: "bigint", nullable: false),
                    sellfox_item_id = table.Column<long>(type: "bigint", nullable: false),
                    wms_sku_id = table.Column<int>(type: "int", nullable: false),
                    stock_id = table.Column<int>(type: "int", nullable: false),
                    qty = table.Column<int>(type: "int", nullable: false),
                    goods_location_id = table.Column<int>(type: "int", nullable: false),
                    goods_owner_id = table.Column<int>(type: "int", nullable: false),
                    sku_code = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    selected_by = table.Column<long>(type: "bigint", nullable: false),
                    selected_by_name = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false),
                    create_time = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    last_update_time = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_wms_packing_task_stock_selection", x => x.id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "wms_packing_task_stock_selection");
        }
    }
}
