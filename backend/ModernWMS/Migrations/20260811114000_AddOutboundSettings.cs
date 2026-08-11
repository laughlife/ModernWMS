using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ModernWMS.Core.DBContext;

#nullable disable

namespace ModernWMS.Migrations;

[DbContext(typeof(SqlDBContext))]
[Migration("20260811114000_AddOutboundSettings")]
public class AddOutboundSettings : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<long>(
            name: "carrier_warehouse_id",
            table: "wms_dispatchlist",
            type: "bigint",
            nullable: true,
            comment: "承运单位对应的ERP国内仓库ID");

        migrationBuilder.AddColumn<string>(
            name: "carrier_unit",
            table: "wms_dispatchlist",
            type: "varchar(256)",
            maxLength: 256,
            nullable: false,
            defaultValue: "",
            comment: "承运单位ERP国内仓库名称快照");

        migrationBuilder.AddColumn<int>(
            name: "volume_divisor",
            table: "wms_dispatchlist",
            type: "int",
            nullable: true,
            comment: "材积重计算除数，允许5000/6000/7000/8000");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "carrier_warehouse_id", table: "wms_dispatchlist");
        migrationBuilder.DropColumn(name: "carrier_unit", table: "wms_dispatchlist");
        migrationBuilder.DropColumn(name: "volume_divisor", table: "wms_dispatchlist");
    }
}
