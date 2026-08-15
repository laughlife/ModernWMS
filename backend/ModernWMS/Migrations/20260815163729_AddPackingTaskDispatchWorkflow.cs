using Microsoft.EntityFrameworkCore.Migrations;
using MySql.EntityFrameworkCore.Metadata;

#nullable disable

namespace ModernWMS.Migrations;

public partial class AddPackingTaskDispatchWorkflow : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "wms_dispatch_order",
            columns: table => new
            {
                id = table.Column<int>(type: "int", nullable: false).Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                dispatch_no = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                create_idempotency_key = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                warehouse_id = table.Column<long>(type: "bigint", nullable: false),
                status = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                source_version = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                source_snapshot = table.Column<string>(type: "longtext", nullable: false),
                source_change_pending = table.Column<bool>(type: "tinyint(1)", nullable: false),
                source_change_snapshot = table.Column<string>(type: "longtext", nullable: false),
                accepted_source_version = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                adjudicated_source_version = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                adjudicated_by = table.Column<int>(type: "int", nullable: true),
                adjudicated_by_name = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false),
                adjudicated_at = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                adjudication_reason = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: false),
                tenant_id = table.Column<long>(type: "bigint", nullable: false),
                created_by = table.Column<int>(type: "int", nullable: false),
                creator = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false),
                create_time = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                last_update_time = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                row_version = table.Column<long>(type: "bigint", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_wms_dispatch_order", x => x.id))
            .Annotation("MySQL:Charset", "utf8mb4");

        migrationBuilder.CreateTable(
            name: "wms_role_warehouse",
            columns: table => new
            {
                id = table.Column<int>(type: "int", nullable: false).Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                role_id = table.Column<int>(type: "int", nullable: false),
                warehouse_id = table.Column<long>(type: "bigint", nullable: false),
                tenant_id = table.Column<long>(type: "bigint", nullable: false),
                created_by = table.Column<int>(type: "int", nullable: false),
                create_time = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                last_update_time = table.Column<DateTime>(type: "datetime(6)", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_wms_role_warehouse", x => x.id))
            .Annotation("MySQL:Charset", "utf8mb4");

        migrationBuilder.CreateTable(
            name: "wms_dispatch_packing_task",
            columns: table => new
            {
                id = table.Column<int>(type: "int", nullable: false).Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                dispatch_order_id = table.Column<int>(type: "int", nullable: false),
                active_source_task_id = table.Column<long>(type: "bigint", nullable: true),
                task_no = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                source_task_id = table.Column<long>(type: "bigint", nullable: false),
                source_task_no = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                source_cartons_json = table.Column<string>(type: "longtext", nullable: false),
                status = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                measured_box_count = table.Column<int>(type: "int", nullable: false),
                expected_box_count = table.Column<int>(type: "int", nullable: false),
                source_version = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                stable_box_identity_verified = table.Column<bool>(type: "tinyint(1)", nullable: false),
                box_identity_validation_error = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: false),
                is_active = table.Column<bool>(type: "tinyint(1)", nullable: false),
                source_cancelled_at = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                writeback_status = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                writeback_request_hash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                writeback_response = table.Column<string>(type: "longtext", nullable: false),
                writeback_retry_count = table.Column<int>(type: "int", nullable: false),
                writeback_last_attempt_at = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                create_time = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                last_update_time = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                row_version = table.Column<long>(type: "bigint", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_wms_dispatch_packing_task", x => x.id))
            .Annotation("MySQL:Charset", "utf8mb4");

        migrationBuilder.CreateTable(
            name: "wms_dispatch_packing_task_item",
            columns: table => new
            {
                id = table.Column<int>(type: "int", nullable: false).Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                packing_task_id = table.Column<int>(type: "int", nullable: false),
                source_item_id = table.Column<long>(type: "bigint", nullable: false),
                source_commodity_id = table.Column<long>(type: "bigint", nullable: true),
                wms_sku_id = table.Column<int>(type: "int", nullable: true),
                commodity_sku = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: false),
                commodity_name = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: false),
                fn_sku = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false),
                msku = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: false),
                required_qty = table.Column<int>(type: "int", nullable: true),
                source_quantity_shipped = table.Column<int>(type: "int", nullable: true),
                source_stock_available = table.Column<int>(type: "int", nullable: true),
                source_version = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                source_snapshot = table.Column<string>(type: "longtext", nullable: false),
                is_active = table.Column<bool>(type: "tinyint(1)", nullable: false),
                create_time = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                last_update_time = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                row_version = table.Column<long>(type: "bigint", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_wms_dispatch_packing_task_item", x => x.id))
            .Annotation("MySQL:Charset", "utf8mb4");

        migrationBuilder.CreateTable(
            name: "wms_dispatch_source_change_event",
            columns: table => new
            {
                id = table.Column<int>(type: "int", nullable: false).Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                dispatch_order_id = table.Column<int>(type: "int", nullable: false),
                source_version = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                event_idempotency_key = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                decision = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                operator_id = table.Column<int>(type: "int", nullable: false),
                operator_name = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false),
                decision_time = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                reason = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: false),
                diff_snapshot = table.Column<string>(type: "longtext", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_wms_dispatch_source_change_event", x => x.id))
            .Annotation("MySQL:Charset", "utf8mb4");

        migrationBuilder.CreateTable(
            name: "wms_weighing_box",
            columns: table => new
            {
                id = table.Column<int>(type: "int", nullable: false).Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                packing_task_id = table.Column<int>(type: "int", nullable: false),
                box_identity = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                source_box_identity = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: false),
                box_sequence = table.Column<int>(type: "int", nullable: false),
                weight = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                length = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                width = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                height = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                measurement_status = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false),
                measured_by = table.Column<int>(type: "int", nullable: true),
                measured_by_name = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false),
                measured_at = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                copied_from_box_id = table.Column<int>(type: "int", nullable: true),
                source_snapshot = table.Column<string>(type: "longtext", nullable: false),
                is_invalidated = table.Column<bool>(type: "tinyint(1)", nullable: false),
                invalidated_at = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                create_time = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                last_update_time = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                row_version = table.Column<long>(type: "bigint", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_wms_weighing_box", x => x.id))
            .Annotation("MySQL:Charset", "utf8mb4");

        migrationBuilder.AddColumn<int>("dispatch_order_id", "wms_dispatchlist", "int", nullable: true);
        migrationBuilder.AddColumn<int>("packing_task_id", "wms_dispatchlist", "int", nullable: true);
        migrationBuilder.AddColumn<int>("packing_task_item_id", "wms_dispatchlist", "int", nullable: true);
        migrationBuilder.AddColumn<int>("packing_task_item_id", "wms_dispatchpicklist", "int", nullable: true);
        migrationBuilder.CreateIndex("IX_wms_dispatch_order_create_idempotency_key", "wms_dispatch_order", "create_idempotency_key", unique: true);
        migrationBuilder.CreateIndex("IX_wms_dispatch_order_dispatch_no", "wms_dispatch_order", "dispatch_no", unique: true);
        migrationBuilder.CreateIndex("IX_wms_dispatch_order_warehouse_id_status", "wms_dispatch_order", new[] { "warehouse_id", "status" });
        migrationBuilder.CreateIndex("IX_wms_role_warehouse_role_id_warehouse_id", "wms_role_warehouse", new[] { "role_id", "warehouse_id" }, unique: true);
        migrationBuilder.CreateIndex("IX_wms_role_warehouse_warehouse_id", "wms_role_warehouse", "warehouse_id");
        migrationBuilder.CreateIndex("IX_wms_dispatch_packing_task_dispatch_order_id_is_active", "wms_dispatch_packing_task", new[] { "dispatch_order_id", "is_active" });
        migrationBuilder.CreateIndex("IX_wms_dispatch_packing_task_dispatch_order_id_source_task_id", "wms_dispatch_packing_task", new[] { "dispatch_order_id", "source_task_id" }, unique: true);
        migrationBuilder.CreateIndex("IX_wms_dispatch_packing_task_active_source_task_id", "wms_dispatch_packing_task", "active_source_task_id", unique: true);
        migrationBuilder.CreateIndex("IX_wms_dispatch_packing_task_item_packing_task_id_is_active", "wms_dispatch_packing_task_item", new[] { "packing_task_id", "is_active" });
        migrationBuilder.CreateIndex("IX_wms_dispatch_packing_task_item_packing_task_id_source_item_id", "wms_dispatch_packing_task_item", new[] { "packing_task_id", "source_item_id" }, unique: true);
        migrationBuilder.CreateIndex("IX_wms_dispatch_source_change_event_dispatch_order_id_source_ve~", "wms_dispatch_source_change_event", new[] { "dispatch_order_id", "source_version" }, unique: true);
        migrationBuilder.CreateIndex("IX_wms_dispatch_source_change_event_event_idempotency_key", "wms_dispatch_source_change_event", "event_idempotency_key", unique: true);
        migrationBuilder.CreateIndex("IX_wms_weighing_box_copied_from_box_id", "wms_weighing_box", "copied_from_box_id");
        migrationBuilder.CreateIndex("IX_wms_weighing_box_packing_task_id_measurement_status", "wms_weighing_box", new[] { "packing_task_id", "measurement_status" });
        migrationBuilder.CreateIndex("IX_wms_weighing_box_packing_task_id_source_box_identity", "wms_weighing_box", new[] { "packing_task_id", "source_box_identity" }, unique: true);
        migrationBuilder.CreateIndex("IX_wms_dispatchlist_dispatch_order_id_packing_task_id", "wms_dispatchlist", new[] { "dispatch_order_id", "packing_task_id" });
        migrationBuilder.CreateIndex("IX_wms_dispatchlist_packing_task_id", "wms_dispatchlist", "packing_task_id");
        migrationBuilder.CreateIndex("IX_wms_dispatchlist_packing_task_item_id", "wms_dispatchlist", "packing_task_item_id");
        migrationBuilder.CreateIndex("IX_wms_dispatchpicklist_packing_task_item_id", "wms_dispatchpicklist", "packing_task_item_id");
        migrationBuilder.AddForeignKey("FK_wms_dispatch_packing_task_wms_dispatch_order_dispatch_order_~", "wms_dispatch_packing_task", "dispatch_order_id", "wms_dispatch_order", principalColumn: "id", onDelete: ReferentialAction.Cascade);
        migrationBuilder.AddForeignKey("FK_wms_dispatch_packing_task_item_wms_dispatch_packing_task_pac~", "wms_dispatch_packing_task_item", "packing_task_id", "wms_dispatch_packing_task", principalColumn: "id", onDelete: ReferentialAction.Cascade);
        migrationBuilder.AddForeignKey("FK_wms_dispatch_source_change_event_wms_dispatch_order_dispatch~", "wms_dispatch_source_change_event", "dispatch_order_id", "wms_dispatch_order", principalColumn: "id", onDelete: ReferentialAction.Restrict);
        migrationBuilder.AddForeignKey("FK_wms_dispatchlist_wms_dispatch_order_dispatch_order_id", "wms_dispatchlist", "dispatch_order_id", "wms_dispatch_order", principalColumn: "id");
        migrationBuilder.AddForeignKey("FK_wms_dispatchlist_wms_dispatch_packing_task_packing_task_id", "wms_dispatchlist", "packing_task_id", "wms_dispatch_packing_task", principalColumn: "id");
        migrationBuilder.AddForeignKey("FK_wms_dispatchlist_wms_dispatch_packing_task_item_packing_task~", "wms_dispatchlist", "packing_task_item_id", "wms_dispatch_packing_task_item", principalColumn: "id");
        migrationBuilder.AddForeignKey("FK_wms_dispatchpicklist_wms_dispatch_packing_task_item_packing_~", "wms_dispatchpicklist", "packing_task_item_id", "wms_dispatch_packing_task_item", principalColumn: "id");
        migrationBuilder.AddForeignKey("FK_wms_role_warehouse_wms_userrole_role_id", "wms_role_warehouse", "role_id", "wms_userrole", principalColumn: "id", onDelete: ReferentialAction.Cascade);
        migrationBuilder.AddForeignKey("FK_wms_weighing_box_wms_dispatch_packing_task_packing_task_id", "wms_weighing_box", "packing_task_id", "wms_dispatch_packing_task", principalColumn: "id", onDelete: ReferentialAction.Cascade);
        migrationBuilder.AddForeignKey("FK_wms_weighing_box_wms_weighing_box_copied_from_box_id", "wms_weighing_box", "copied_from_box_id", "wms_weighing_box", principalColumn: "id");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey("FK_wms_dispatch_packing_task_wms_dispatch_order_dispatch_order_~", "wms_dispatch_packing_task");
        migrationBuilder.DropForeignKey("FK_wms_dispatch_packing_task_item_wms_dispatch_packing_task_pac~", "wms_dispatch_packing_task_item");
        migrationBuilder.DropForeignKey("FK_wms_dispatch_source_change_event_wms_dispatch_order_dispatch~", "wms_dispatch_source_change_event");
        migrationBuilder.DropForeignKey("FK_wms_dispatchlist_wms_dispatch_order_dispatch_order_id", "wms_dispatchlist");
        migrationBuilder.DropForeignKey("FK_wms_dispatchlist_wms_dispatch_packing_task_packing_task_id", "wms_dispatchlist");
        migrationBuilder.DropForeignKey("FK_wms_dispatchlist_wms_dispatch_packing_task_item_packing_task~", "wms_dispatchlist");
        migrationBuilder.DropForeignKey("FK_wms_dispatchpicklist_wms_dispatch_packing_task_item_packing_~", "wms_dispatchpicklist");
        migrationBuilder.DropForeignKey("FK_wms_role_warehouse_wms_userrole_role_id", "wms_role_warehouse");
        migrationBuilder.DropForeignKey("FK_wms_weighing_box_wms_dispatch_packing_task_packing_task_id", "wms_weighing_box");
        migrationBuilder.DropForeignKey("FK_wms_weighing_box_wms_weighing_box_copied_from_box_id", "wms_weighing_box");
        migrationBuilder.DropIndex("IX_wms_dispatchlist_dispatch_order_id_packing_task_id", "wms_dispatchlist");
        migrationBuilder.DropIndex("IX_wms_dispatchlist_packing_task_id", "wms_dispatchlist");
        migrationBuilder.DropIndex("IX_wms_dispatchlist_packing_task_item_id", "wms_dispatchlist");
        migrationBuilder.DropIndex("IX_wms_dispatchpicklist_packing_task_item_id", "wms_dispatchpicklist");
        migrationBuilder.DropColumn("dispatch_order_id", "wms_dispatchlist");
        migrationBuilder.DropColumn("packing_task_id", "wms_dispatchlist");
        migrationBuilder.DropColumn("packing_task_item_id", "wms_dispatchlist");
        migrationBuilder.DropColumn("packing_task_item_id", "wms_dispatchpicklist");
        migrationBuilder.DropTable("wms_weighing_box");
        migrationBuilder.DropTable("wms_dispatch_source_change_event");
        migrationBuilder.DropTable("wms_dispatch_packing_task_item");
        migrationBuilder.DropTable("wms_dispatch_packing_task");
        migrationBuilder.DropTable("wms_role_warehouse");
        migrationBuilder.DropTable("wms_dispatch_order");
    }
}
