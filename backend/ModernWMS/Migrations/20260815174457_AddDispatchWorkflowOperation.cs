using System;
using Microsoft.EntityFrameworkCore.Migrations;
using MySql.EntityFrameworkCore.Metadata;

#nullable disable

namespace ModernWMS.Migrations
{
    /// <inheritdoc />
    public partial class AddDispatchWorkflowOperation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "wms_dispatch_workflow_operation",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    dispatch_order_id = table.Column<int>(type: "int", nullable: false),
                    operation = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    request_id = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    result_status = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    result_order_status = table.Column<byte>(type: "tinyint unsigned", nullable: true),
                    result_row_version = table.Column<long>(type: "bigint", nullable: true),
                    create_operator = table.Column<int>(type: "int", nullable: false),
                    create_operator_name = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false),
                    create_time = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_wms_dispatch_workflow_operation", x => x.id);
                    table.ForeignKey(
                        name: "FK_wms_dispatch_workflow_operation_wms_dispatch_order_dispatch_~",
                        column: x => x.dispatch_order_id,
                        principalTable: "wms_dispatch_order",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_wms_dispatch_workflow_operation_dispatch_order_id_create_time",
                table: "wms_dispatch_workflow_operation",
                columns: new[] { "dispatch_order_id", "create_time" });

            migrationBuilder.CreateIndex(
                name: "IX_wms_dispatch_workflow_operation_dispatch_order_id_operation_~",
                table: "wms_dispatch_workflow_operation",
                columns: new[] { "dispatch_order_id", "operation", "request_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "wms_dispatch_workflow_operation");
        }
    }
}
