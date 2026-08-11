using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ModernWMS.Core.DBContext;

#nullable disable

namespace ModernWMS.Migrations;

[DbContext(typeof(SqlDBContext))]
[Migration("20260811170000_AddDispatchPickStockIdentity")]
public class AddDispatchPickStockIdentity : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(
            name: "stock_id",
            table: "wms_dispatchpicklist",
            type: "int",
            nullable: false,
            defaultValue: 0,
            comment: "拣货分配时选中的WMS库存行ID，历史数据为0时按原业务键兼容匹配");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "stock_id", table: "wms_dispatchpicklist");
    }
}
