using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ModernWMS.Core.DBContext;

#nullable disable

namespace ModernWMS.Migrations;

[DbContext(typeof(SqlDBContext))]
[Migration("20260810170000_AddDispatchWeighingMeasurements")]
public class AddDispatchWeighingMeasurements : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<decimal>(
            name: "weighing_length",
            table: "wms_dispatchlist",
            type: "decimal(18,2)",
            nullable: false,
            defaultValue: 0m,
            comment: "本次称重实测长度(cm)");

        migrationBuilder.AddColumn<decimal>(
            name: "weighing_width",
            table: "wms_dispatchlist",
            type: "decimal(18,2)",
            nullable: false,
            defaultValue: 0m,
            comment: "本次称重实测宽度(cm)");

        migrationBuilder.AddColumn<decimal>(
            name: "weighing_height",
            table: "wms_dispatchlist",
            type: "decimal(18,2)",
            nullable: false,
            defaultValue: 0m,
            comment: "本次称重实测高度(cm)");

        migrationBuilder.AddColumn<decimal>(
            name: "weighing_volume",
            table: "wms_dispatchlist",
            type: "decimal(18,2)",
            nullable: false,
            defaultValue: 0m,
            comment: "按实测长宽高自动计算的体积(cm³)");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "weighing_height", table: "wms_dispatchlist");
        migrationBuilder.DropColumn(name: "weighing_length", table: "wms_dispatchlist");
        migrationBuilder.DropColumn(name: "weighing_volume", table: "wms_dispatchlist");
        migrationBuilder.DropColumn(name: "weighing_width", table: "wms_dispatchlist");
    }
}
