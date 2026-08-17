using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using MySql.EntityFrameworkCore.Metadata;
using ModernWMS.Core.DBContext;

#nullable disable

namespace ModernWMS.Migrations
{
    [DbContext(typeof(SqlDBContext))]
    [Migration("20260806090000_RemoveCategoryAndSpuCategory")]
    public partial class RemoveCategoryAndSpuCategory : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "category_id",
                table: "spu");

            migrationBuilder.DropTable(
                name: "category");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "category",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    category_name = table.Column<string>(type: "longtext", nullable: false),
                    parent_id = table.Column<int>(type: "int", nullable: false),
                    creator = table.Column<string>(type: "longtext", nullable: false),
                    create_time = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    last_update_time = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    is_valid = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    tenant_id = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_category", x => x.id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.AddColumn<int>(
                name: "category_id",
                table: "spu",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }
    }
}
