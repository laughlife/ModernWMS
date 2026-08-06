using System;
using Microsoft.EntityFrameworkCore.Migrations;
using MySql.EntityFrameworkCore.Metadata;

#nullable disable

namespace ModernWMS.Migrations
{
    [Migration("20260806120000_RemoveCustomer")]
    public partial class RemoveCustomer : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DELETE FROM `rolemenu` WHERE `menu_id` = 30;");
            migrationBuilder.Sql("DELETE FROM `menu` WHERE `id` = 30 OR `menu_name` = 'customer';");

            migrationBuilder.DropColumn(
                name: "customer_id",
                table: "dispatchlist");

            migrationBuilder.DropColumn(
                name: "customer_name",
                table: "dispatchlist");

            migrationBuilder.DropTable(
                name: "customer");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "customer",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    customer_name = table.Column<string>(type: "longtext", nullable: false),
                    city = table.Column<string>(type: "longtext", nullable: false),
                    address = table.Column<string>(type: "longtext", nullable: false),
                    email = table.Column<string>(type: "longtext", nullable: false),
                    manager = table.Column<string>(type: "longtext", nullable: false),
                    contact_tel = table.Column<string>(type: "longtext", nullable: false),
                    creator = table.Column<string>(type: "longtext", nullable: false),
                    create_time = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    last_update_time = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    is_valid = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    tenant_id = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_customer", x => x.id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.AddColumn<int>(
                name: "customer_id",
                table: "dispatchlist",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "customer_name",
                table: "dispatchlist",
                type: "longtext",
                nullable: false,
                defaultValue: "");
        }
    }
}
