using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ModernWMS.Core.DBContext;
using MySql.EntityFrameworkCore.Metadata;

#nullable disable

namespace ModernWMS.Migrations;

[DbContext(typeof(SqlDBContext))]
[Migration("20260810193000_CreateDispatchWeighingBox")]
public class CreateDispatchWeighingBox : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "wms_dispatch_weighing_box",
            columns: table => new
            {
                id = table.Column<int>(type: "int", nullable: false)
                    .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                tenant_id = table.Column<long>(type: "bigint", nullable: false),
                dispatch_no = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                fba_shipment_id = table.Column<long>(type: "bigint", nullable: false),
                fba_no = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                erp_box_id = table.Column<long>(type: "bigint", nullable: false),
                box_no = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                box_index = table.Column<int>(type: "int", nullable: false),
                tracking_id = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false),
                weighing_weight = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                weighing_length = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                weighing_width = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                weighing_height = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                weighing_volume = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                weighing_person_id = table.Column<int>(type: "int", nullable: false),
                weighing_person = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                weighing_time = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                copied_from_erp_box_id = table.Column<long>(type: "bigint", nullable: true),
                create_time = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                last_update_time = table.Column<DateTime>(type: "datetime(6)", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_wms_dispatch_weighing_box", x => x.id));

        migrationBuilder.CreateIndex("IX_wms_dispatch_weighing_box_tenant_id_dispatch_no",
            "wms_dispatch_weighing_box", new[] { "tenant_id", "dispatch_no" });
        migrationBuilder.CreateIndex("IX_wms_dispatch_weighing_box_tenant_id_fba_shipment_id",
            "wms_dispatch_weighing_box", new[] { "tenant_id", "fba_shipment_id" });
        migrationBuilder.CreateIndex("IX_wms_dispatch_weighing_box_tenant_id_erp_box_id",
            "wms_dispatch_weighing_box", new[] { "tenant_id", "erp_box_id" }, unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropTable(name: "wms_dispatch_weighing_box");
}
