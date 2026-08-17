using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ModernWMS.Core.DBContext;

#nullable disable

namespace ModernWMS.Migrations
{
    /// <summary>
    /// Adds the optional logical foreign key from a WMS warehouse to an ERP warehouse.
    /// A physical foreign key cannot be created because the target belongs to another database context.
    /// </summary>
    [DbContext(typeof(SqlDBContext))]
    [Migration("20260808020000_AddWarehouseErpBinding")]
    public partial class AddWarehouseErpBinding : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "erp_warehouse_id",
                table: "warehouse",
                type: "bigint",
                nullable: true,
                comment: "Logical foreign key to ruoyi-vue-pro.erp_warehouse.id");

            migrationBuilder.CreateIndex(
                name: "IX_warehouse_erp_warehouse_id",
                table: "warehouse",
                column: "erp_warehouse_id");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_warehouse_erp_warehouse_id",
                table: "warehouse");

            migrationBuilder.DropColumn(
                name: "erp_warehouse_id",
                table: "warehouse");
        }
    }
}
