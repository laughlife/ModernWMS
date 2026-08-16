using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ModernWMS.Migrations
{
    /// <inheritdoc />
    public partial class ExpandDispatchSourceChangeEventDecisionIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "DROP INDEX `IX_wms_dispatch_source_change_event_dispatch_order_id_source_ve~` " +
                "ON `wms_dispatch_source_change_event`;");

            migrationBuilder.CreateIndex(
                name: "IX_wms_dispatch_source_change_event_dispatch_order_id_source_ve~",
                table: "wms_dispatch_source_change_event",
                columns: new[] { "dispatch_order_id", "source_version", "decision" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "DROP INDEX `IX_wms_dispatch_source_change_event_dispatch_order_id_source_ve~` " +
                "ON `wms_dispatch_source_change_event`;");

            migrationBuilder.CreateIndex(
                name: "IX_wms_dispatch_source_change_event_dispatch_order_id_source_ve~",
                table: "wms_dispatch_source_change_event",
                columns: new[] { "dispatch_order_id", "source_version" },
                unique: true);
        }
    }
}
