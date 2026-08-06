using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ModernWMS.Migrations
{
    public partial class AddWarehouseareaSort : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "sort",
                table: "warehousearea",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.Sql(
                "UPDATE `warehousearea` " +
                "SET `sort` = CAST(SUBSTRING_INDEX(`area_name`, '.', 1) AS SIGNED) " +
                "WHERE `area_name` REGEXP '^[0-9]+[.]'");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "sort",
                table: "warehousearea");
        }
    }
}
