using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ModernWMS.Migrations
{
    /// <inheritdoc />
    public partial class AddDispatchOrderSigningFacts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "damaged_qty",
                table: "wms_dispatch_order",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "notification_attempt_count",
                table: "wms_dispatch_order",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "notification_last_error",
                table: "wms_dispatch_order",
                type: "varchar(500)",
                maxLength: 500,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTime>(
                name: "notification_sent_at",
                table: "wms_dispatch_order",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<byte>(
                name: "notification_status",
                table: "wms_dispatch_order",
                type: "tinyint unsigned",
                nullable: false,
                defaultValue: (byte)0);

            migrationBuilder.AddColumn<DateTime>(
                name: "notification_updated_at",
                table: "wms_dispatch_order",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "signed_at",
                table: "wms_dispatch_order",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "signed_by",
                table: "wms_dispatch_order",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "signed_by_name",
                table: "wms_dispatch_order",
                type: "varchar(128)",
                maxLength: 128,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "signed_qty",
                table: "wms_dispatch_order",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_wms_dispatch_order_notification_status_notification_updated_~",
                table: "wms_dispatch_order",
                columns: new[] { "notification_status", "notification_updated_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_wms_dispatch_order_notification_status_notification_updated_~",
                table: "wms_dispatch_order");

            migrationBuilder.DropColumn(
                name: "damaged_qty",
                table: "wms_dispatch_order");

            migrationBuilder.DropColumn(
                name: "notification_attempt_count",
                table: "wms_dispatch_order");

            migrationBuilder.DropColumn(
                name: "notification_last_error",
                table: "wms_dispatch_order");

            migrationBuilder.DropColumn(
                name: "notification_sent_at",
                table: "wms_dispatch_order");

            migrationBuilder.DropColumn(
                name: "notification_status",
                table: "wms_dispatch_order");

            migrationBuilder.DropColumn(
                name: "notification_updated_at",
                table: "wms_dispatch_order");

            migrationBuilder.DropColumn(
                name: "signed_at",
                table: "wms_dispatch_order");

            migrationBuilder.DropColumn(
                name: "signed_by",
                table: "wms_dispatch_order");

            migrationBuilder.DropColumn(
                name: "signed_by_name",
                table: "wms_dispatch_order");

            migrationBuilder.DropColumn(
                name: "signed_qty",
                table: "wms_dispatch_order");
        }
    }
}
