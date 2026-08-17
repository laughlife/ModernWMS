using System;
using Microsoft.EntityFrameworkCore.Migrations;
using MySql.EntityFrameworkCore.Metadata;

#nullable disable

namespace ModernWMS.Migrations
{
    /// <inheritdoc />
    public partial class InitialMySql : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "action_log",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    vue_path = table.Column<string>(type: "longtext", nullable: false),
                    user_name = table.Column<string>(type: "longtext", nullable: false),
                    action_content = table.Column<string>(type: "longtext", nullable: false),
                    action_time = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    tenant_id = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_action_log", x => x.id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "asnmaster",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    asn_no = table.Column<string>(type: "longtext", nullable: false),
                    asn_batch = table.Column<string>(type: "longtext", nullable: false),
                    estimated_arrival_time = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    asn_status = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    weight = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    volume = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    goods_owner_id = table.Column<int>(type: "int", nullable: false),
                    goods_owner_name = table.Column<string>(type: "longtext", nullable: false),
                    creator = table.Column<string>(type: "longtext", nullable: false),
                    create_time = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    last_update_time = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    tenant_id = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_asnmaster", x => x.id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "asnsort",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    asn_id = table.Column<int>(type: "int", nullable: false),
                    sorted_qty = table.Column<int>(type: "int", nullable: false),
                    series_number = table.Column<string>(type: "longtext", nullable: false),
                    putaway_qty = table.Column<int>(type: "int", nullable: false),
                    creator = table.Column<string>(type: "longtext", nullable: false),
                    create_time = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    last_update_time = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    is_valid = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    tenant_id = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_asnsort", x => x.id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

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

            migrationBuilder.CreateTable(
                name: "company",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    company_name = table.Column<string>(type: "longtext", nullable: false),
                    city = table.Column<string>(type: "longtext", nullable: false),
                    address = table.Column<string>(type: "longtext", nullable: false),
                    manager = table.Column<string>(type: "longtext", nullable: false),
                    contact_tel = table.Column<string>(type: "longtext", nullable: false),
                    create_time = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    last_update_time = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    tenant_id = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_company", x => x.id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

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

            migrationBuilder.CreateTable(
                name: "dispatchlist",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    dispatch_no = table.Column<string>(type: "longtext", nullable: false),
                    dispatch_status = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    customer_id = table.Column<int>(type: "int", nullable: false),
                    customer_name = table.Column<string>(type: "longtext", nullable: false),
                    sku_id = table.Column<int>(type: "int", nullable: false),
                    qty = table.Column<int>(type: "int", nullable: false),
                    weight = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    volume = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    creator = table.Column<string>(type: "longtext", nullable: false),
                    create_time = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    damage_qty = table.Column<int>(type: "int", nullable: false),
                    lock_qty = table.Column<int>(type: "int", nullable: false),
                    picked_qty = table.Column<int>(type: "int", nullable: false),
                    intrasit_qty = table.Column<int>(type: "int", nullable: false),
                    package_qty = table.Column<int>(type: "int", nullable: false),
                    weighing_qty = table.Column<int>(type: "int", nullable: false),
                    actual_qty = table.Column<int>(type: "int", nullable: false),
                    sign_qty = table.Column<int>(type: "int", nullable: false),
                    package_no = table.Column<string>(type: "longtext", nullable: false),
                    package_person = table.Column<string>(type: "longtext", nullable: false),
                    package_time = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    weighing_no = table.Column<string>(type: "longtext", nullable: false),
                    weighing_person = table.Column<string>(type: "longtext", nullable: false),
                    weighing_weight = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    waybill_no = table.Column<string>(type: "longtext", nullable: false),
                    carrier = table.Column<string>(type: "longtext", nullable: false),
                    freightfee = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    last_update_time = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    tenant_id = table.Column<long>(type: "bigint", nullable: false),
                    pick_checker_id = table.Column<int>(type: "int", nullable: false),
                    pick_checker = table.Column<string>(type: "longtext", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_dispatchlist", x => x.id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "flowsetmain",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    menu = table.Column<string>(type: "longtext", nullable: false),
                    flow_name = table.Column<string>(type: "longtext", nullable: false),
                    creator = table.Column<string>(type: "longtext", nullable: false),
                    create_time = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    tenant_id = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_flowsetmain", x => x.id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "freightfee",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    carrier = table.Column<string>(type: "longtext", nullable: false),
                    departure_city = table.Column<string>(type: "longtext", nullable: false),
                    arrival_city = table.Column<string>(type: "longtext", nullable: false),
                    price_per_weight = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    price_per_volume = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    min_payment = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    creator = table.Column<string>(type: "longtext", nullable: false),
                    create_time = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    last_update_time = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    is_valid = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    tenant_id = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_freightfee", x => x.id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "global_unique_serial",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    table_name = table.Column<string>(type: "longtext", nullable: false),
                    prefix_char = table.Column<string>(type: "longtext", nullable: false),
                    reset_rule = table.Column<string>(type: "longtext", nullable: false),
                    current_no = table.Column<int>(type: "int", nullable: false),
                    last_update_time = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    tenant_id = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_global_unique_serial", x => x.id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "goodslocation",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    warehouse_id = table.Column<int>(type: "int", nullable: false),
                    warehouse_name = table.Column<string>(type: "longtext", nullable: false),
                    warehouse_area_name = table.Column<string>(type: "longtext", nullable: false),
                    warehouse_area_property = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    location_name = table.Column<string>(type: "longtext", nullable: false),
                    location_length = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    location_width = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    location_heigth = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    location_volume = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    location_load = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    roadway_number = table.Column<string>(type: "longtext", nullable: false),
                    shelf_number = table.Column<string>(type: "longtext", nullable: false),
                    layer_number = table.Column<string>(type: "longtext", nullable: false),
                    tag_number = table.Column<string>(type: "longtext", nullable: false),
                    create_time = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    last_update_time = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    is_valid = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    tenant_id = table.Column<long>(type: "bigint", nullable: false),
                    warehouse_area_id = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_goodslocation", x => x.id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "goodsowner",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    goods_owner_name = table.Column<string>(type: "longtext", nullable: false),
                    city = table.Column<string>(type: "longtext", nullable: false),
                    address = table.Column<string>(type: "longtext", nullable: false),
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
                    table.PrimaryKey("PK_goodsowner", x => x.id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "menu",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    menu_name = table.Column<string>(type: "longtext", nullable: false),
                    module = table.Column<string>(type: "longtext", nullable: false),
                    vue_path = table.Column<string>(type: "longtext", nullable: false),
                    vue_path_detail = table.Column<string>(type: "longtext", nullable: false),
                    vue_directory = table.Column<string>(type: "longtext", nullable: false),
                    sort = table.Column<int>(type: "int", nullable: false),
                    tenant_id = table.Column<long>(type: "bigint", nullable: false),
                    menu_actions = table.Column<string>(type: "longtext", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_menu", x => x.id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "rolemenu",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    userrole_id = table.Column<int>(type: "int", nullable: false),
                    menu_id = table.Column<int>(type: "int", nullable: false),
                    authority = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    create_time = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    last_update_time = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    tenant_id = table.Column<long>(type: "bigint", nullable: false),
                    menu_actions_authority = table.Column<string>(type: "longtext", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_rolemenu", x => x.id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "spu",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    spu_code = table.Column<string>(type: "longtext", nullable: false),
                    spu_name = table.Column<string>(type: "longtext", nullable: false),
                    category_id = table.Column<int>(type: "int", nullable: false),
                    spu_description = table.Column<string>(type: "longtext", nullable: false),
                    supplier_id = table.Column<int>(type: "int", nullable: false),
                    supplier_name = table.Column<string>(type: "longtext", nullable: false),
                    brand = table.Column<string>(type: "longtext", nullable: false),
                    origin = table.Column<string>(type: "longtext", nullable: false),
                    length_unit = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    volume_unit = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    weight_unit = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    creator = table.Column<string>(type: "longtext", nullable: false),
                    create_time = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    last_update_time = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    is_valid = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    tenant_id = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_spu", x => x.id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "stock",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    sku_id = table.Column<int>(type: "int", nullable: false),
                    goods_location_id = table.Column<int>(type: "int", nullable: false),
                    qty = table.Column<int>(type: "int", nullable: false),
                    goods_owner_id = table.Column<int>(type: "int", nullable: false),
                    is_freeze = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    last_update_time = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    tenant_id = table.Column<long>(type: "bigint", nullable: false),
                    series_number = table.Column<string>(type: "longtext", nullable: false),
                    expiry_date = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    price = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    putaway_date = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_stock", x => x.id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "stockadjust",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    job_code = table.Column<string>(type: "longtext", nullable: false),
                    sku_id = table.Column<int>(type: "int", nullable: false),
                    goods_owner_id = table.Column<int>(type: "int", nullable: false),
                    goods_location_id = table.Column<int>(type: "int", nullable: false),
                    qty = table.Column<int>(type: "int", nullable: false),
                    creator = table.Column<string>(type: "longtext", nullable: false),
                    create_time = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    last_update_time = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    tenant_id = table.Column<long>(type: "bigint", nullable: false),
                    is_update_stock = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    job_type = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    source_table_id = table.Column<int>(type: "int", nullable: false),
                    series_number = table.Column<string>(type: "longtext", nullable: false),
                    expiry_date = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    price = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    putaway_date = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_stockadjust", x => x.id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "stockfreeze",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    job_code = table.Column<string>(type: "longtext", nullable: false),
                    job_type = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    sku_id = table.Column<int>(type: "int", nullable: false),
                    goods_owner_id = table.Column<int>(type: "int", nullable: false),
                    goods_location_id = table.Column<int>(type: "int", nullable: false),
                    handler = table.Column<string>(type: "longtext", nullable: false),
                    handle_time = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    last_update_time = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    tenant_id = table.Column<long>(type: "bigint", nullable: false),
                    series_number = table.Column<string>(type: "longtext", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_stockfreeze", x => x.id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "stockmove",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    job_code = table.Column<string>(type: "longtext", nullable: false),
                    move_status = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    sku_id = table.Column<int>(type: "int", nullable: false),
                    orig_goods_location_id = table.Column<int>(type: "int", nullable: false),
                    dest_googs_location_id = table.Column<int>(type: "int", nullable: false),
                    qty = table.Column<int>(type: "int", nullable: false),
                    goods_owner_id = table.Column<int>(type: "int", nullable: false),
                    handler = table.Column<string>(type: "longtext", nullable: false),
                    handle_time = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    creator = table.Column<string>(type: "longtext", nullable: false),
                    create_time = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    last_update_time = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    tenant_id = table.Column<long>(type: "bigint", nullable: false),
                    series_number = table.Column<string>(type: "longtext", nullable: false),
                    expiry_date = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    price = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    putaway_date = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_stockmove", x => x.id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "stockprocess",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    job_code = table.Column<string>(type: "longtext", nullable: false),
                    job_type = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    process_status = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    processor = table.Column<string>(type: "longtext", nullable: false),
                    process_time = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    creator = table.Column<string>(type: "longtext", nullable: false),
                    create_time = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    last_update_time = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    tenant_id = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_stockprocess", x => x.id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "stocktaking",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    job_code = table.Column<string>(type: "longtext", nullable: false),
                    job_status = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    sku_id = table.Column<int>(type: "int", nullable: false),
                    goods_owner_id = table.Column<int>(type: "int", nullable: false),
                    goods_location_id = table.Column<int>(type: "int", nullable: false),
                    series_number = table.Column<string>(type: "longtext", nullable: false),
                    expiry_date = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    price = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    putaway_date = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    book_qty = table.Column<int>(type: "int", nullable: false),
                    counted_qty = table.Column<int>(type: "int", nullable: false),
                    difference_qty = table.Column<int>(type: "int", nullable: false),
                    creator = table.Column<string>(type: "longtext", nullable: false),
                    create_time = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    last_update_time = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    tenant_id = table.Column<long>(type: "bigint", nullable: false),
                    handler = table.Column<string>(type: "longtext", nullable: false),
                    handle_time = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_stocktaking", x => x.id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "supplier",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    supplier_name = table.Column<string>(type: "longtext", nullable: false),
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
                    table.PrimaryKey("PK_supplier", x => x.id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "user",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    user_num = table.Column<string>(type: "longtext", nullable: false),
                    user_name = table.Column<string>(type: "longtext", nullable: false),
                    contact_tel = table.Column<string>(type: "longtext", nullable: false),
                    user_role = table.Column<string>(type: "longtext", nullable: false),
                    sex = table.Column<string>(type: "longtext", nullable: false),
                    is_valid = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    auth_string = table.Column<string>(type: "longtext", nullable: false),
                    email = table.Column<string>(type: "longtext", nullable: false),
                    creator = table.Column<string>(type: "longtext", nullable: false),
                    create_time = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    last_update_time = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    tenant_id = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user", x => x.id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "user_defined_print_solution",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    vue_path = table.Column<string>(type: "longtext", nullable: false),
                    tab_page = table.Column<string>(type: "longtext", nullable: false),
                    solution_name = table.Column<string>(type: "longtext", nullable: false),
                    config_json = table.Column<string>(type: "longtext", nullable: false),
                    report_length = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    report_width = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    report_direction = table.Column<string>(type: "longtext", nullable: false),
                    last_update_time = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    tenant_id = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_defined_print_solution", x => x.id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "userrole",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    role_name = table.Column<string>(type: "longtext", nullable: false),
                    is_valid = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    create_time = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    last_update_time = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    tenant_id = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_userrole", x => x.id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "warehouse",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    warehouse_name = table.Column<string>(type: "longtext", nullable: false),
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
                    table.PrimaryKey("PK_warehouse", x => x.id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "warehousearea",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    warehouse_id = table.Column<int>(type: "int", nullable: false),
                    area_name = table.Column<string>(type: "longtext", nullable: false),
                    parent_id = table.Column<int>(type: "int", nullable: false),
                    create_time = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    last_update_time = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    is_valid = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    tenant_id = table.Column<long>(type: "bigint", nullable: false),
                    area_property = table.Column<byte>(type: "tinyint unsigned", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_warehousearea", x => x.id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "asn",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    asnmaster_id = table.Column<int>(type: "int", nullable: false),
                    asn_no = table.Column<string>(type: "longtext", nullable: false),
                    asn_status = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    spu_id = table.Column<int>(type: "int", nullable: false),
                    sku_id = table.Column<int>(type: "int", nullable: false),
                    asn_qty = table.Column<int>(type: "int", nullable: false),
                    actual_qty = table.Column<int>(type: "int", nullable: false),
                    arrival_time = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    unload_time = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    unload_person_id = table.Column<int>(type: "int", nullable: false),
                    unload_person = table.Column<string>(type: "longtext", nullable: false),
                    sorted_qty = table.Column<int>(type: "int", nullable: false),
                    shortage_qty = table.Column<int>(type: "int", nullable: false),
                    more_qty = table.Column<int>(type: "int", nullable: false),
                    damage_qty = table.Column<int>(type: "int", nullable: false),
                    weight = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    volume = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    supplier_id = table.Column<int>(type: "int", nullable: false),
                    supplier_name = table.Column<string>(type: "longtext", nullable: false),
                    goods_owner_id = table.Column<int>(type: "int", nullable: false),
                    goods_owner_name = table.Column<string>(type: "longtext", nullable: false),
                    creator = table.Column<string>(type: "longtext", nullable: false),
                    create_time = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    last_update_time = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    is_valid = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    tenant_id = table.Column<long>(type: "bigint", nullable: false),
                    expiry_date = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    price = table.Column<decimal>(type: "decimal(18,2)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_asn", x => x.id);
                    table.ForeignKey(
                        name: "FK_asn_asnmaster_asnmaster_id",
                        column: x => x.asnmaster_id,
                        principalTable: "asnmaster",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "dispatchpicklist",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    dispatchlist_id = table.Column<int>(type: "int", nullable: false),
                    goods_owner_id = table.Column<int>(type: "int", nullable: false),
                    goods_location_id = table.Column<int>(type: "int", nullable: false),
                    sku_id = table.Column<int>(type: "int", nullable: false),
                    pick_qty = table.Column<int>(type: "int", nullable: false),
                    picked_qty = table.Column<int>(type: "int", nullable: false),
                    is_update_stock = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    last_update_time = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    series_number = table.Column<string>(type: "longtext", nullable: false),
                    picker_id = table.Column<int>(type: "int", nullable: false),
                    picker = table.Column<string>(type: "longtext", nullable: false),
                    expiry_date = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    price = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    putaway_date = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_dispatchpicklist", x => x.id);
                    table.ForeignKey(
                        name: "FK_dispatchpicklist_dispatchlist_dispatchlist_id",
                        column: x => x.dispatchlist_id,
                        principalTable: "dispatchlist",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "flowset",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    flowsetmain_id = table.Column<int>(type: "int", nullable: false),
                    is_origin = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    is_end = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    node_guid = table.Column<string>(type: "longtext", nullable: false),
                    node_name = table.Column<string>(type: "longtext", nullable: false),
                    prev_node_guid = table.Column<string>(type: "longtext", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_flowset", x => x.id);
                    table.ForeignKey(
                        name: "FK_flowset_flowsetmain_flowsetmain_id",
                        column: x => x.flowsetmain_id,
                        principalTable: "flowsetmain",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "sku",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    spu_id = table.Column<int>(type: "int", nullable: false),
                    sku_code = table.Column<string>(type: "longtext", nullable: false),
                    sku_name = table.Column<string>(type: "longtext", nullable: false),
                    bar_code = table.Column<string>(type: "longtext", nullable: false),
                    weight = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    lenght = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    width = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    height = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    volume = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    unit = table.Column<string>(type: "longtext", nullable: false),
                    cost = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    price = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    create_time = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    last_update_time = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sku", x => x.id);
                    table.ForeignKey(
                        name: "FK_sku_spu_spu_id",
                        column: x => x.spu_id,
                        principalTable: "spu",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "stockprocessdetail",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    stock_process_id = table.Column<int>(type: "int", nullable: false),
                    sku_id = table.Column<int>(type: "int", nullable: false),
                    goods_owner_id = table.Column<int>(type: "int", nullable: false),
                    goods_location_id = table.Column<int>(type: "int", nullable: false),
                    qty = table.Column<int>(type: "int", nullable: false),
                    last_update_time = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    tenant_id = table.Column<long>(type: "bigint", nullable: false),
                    is_source = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    is_update_stock = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    series_number = table.Column<string>(type: "longtext", nullable: false),
                    expiry_date = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    price = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    putaway_date = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_stockprocessdetail", x => x.id);
                    table.ForeignKey(
                        name: "FK_stockprocessdetail_stockprocess_stock_process_id",
                        column: x => x.stock_process_id,
                        principalTable: "stockprocess",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "flowsetfilter",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    flowset_id = table.Column<int>(type: "int", nullable: false),
                    flowsetmain_id = table.Column<int>(type: "int", nullable: false),
                    node_guid = table.Column<string>(type: "longtext", nullable: false),
                    logic = table.Column<string>(type: "longtext", nullable: false),
                    c1 = table.Column<string>(type: "longtext", nullable: false),
                    col_label = table.Column<string>(type: "longtext", nullable: false),
                    col_name = table.Column<string>(type: "longtext", nullable: false),
                    compare = table.Column<string>(type: "longtext", nullable: false),
                    content = table.Column<string>(type: "longtext", nullable: false),
                    c2 = table.Column<string>(type: "longtext", nullable: false),
                    sort = table.Column<int>(type: "int", nullable: false),
                    condition_group = table.Column<string>(type: "longtext", nullable: false),
                    formulas = table.Column<string>(type: "longtext", nullable: false),
                    assert_mode = table.Column<string>(type: "longtext", nullable: false),
                    table_name = table.Column<string>(type: "longtext", nullable: false),
                    scheme_name = table.Column<string>(type: "longtext", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_flowsetfilter", x => x.id);
                    table.ForeignKey(
                        name: "FK_flowsetfilter_flowset_flowset_id",
                        column: x => x.flowset_id,
                        principalTable: "flowset",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "flowsetusers",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    flowset_id = table.Column<int>(type: "int", nullable: false),
                    flowsetmain_id = table.Column<int>(type: "int", nullable: false),
                    node_guid = table.Column<string>(type: "longtext", nullable: false),
                    user_id = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_flowsetusers", x => x.id);
                    table.ForeignKey(
                        name: "FK_flowsetusers_flowset_flowset_id",
                        column: x => x.flowset_id,
                        principalTable: "flowset",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "sku_safety_stock",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    sku_id = table.Column<int>(type: "int", nullable: false),
                    warehouse_id = table.Column<int>(type: "int", nullable: false),
                    safety_stock_qty = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sku_safety_stock", x => x.id);
                    table.ForeignKey(
                        name: "FK_sku_safety_stock_sku_sku_id",
                        column: x => x.sku_id,
                        principalTable: "sku",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_asn_asnmaster_id",
                table: "asn",
                column: "asnmaster_id");

            migrationBuilder.CreateIndex(
                name: "IX_dispatchpicklist_dispatchlist_id",
                table: "dispatchpicklist",
                column: "dispatchlist_id");

            migrationBuilder.CreateIndex(
                name: "IX_flowset_flowsetmain_id",
                table: "flowset",
                column: "flowsetmain_id");

            migrationBuilder.CreateIndex(
                name: "IX_flowsetfilter_flowset_id",
                table: "flowsetfilter",
                column: "flowset_id");

            migrationBuilder.CreateIndex(
                name: "IX_flowsetusers_flowset_id",
                table: "flowsetusers",
                column: "flowset_id");

            migrationBuilder.CreateIndex(
                name: "IX_sku_spu_id",
                table: "sku",
                column: "spu_id");

            migrationBuilder.CreateIndex(
                name: "IX_sku_safety_stock_sku_id",
                table: "sku_safety_stock",
                column: "sku_id");

            migrationBuilder.CreateIndex(
                name: "IX_stockprocessdetail_stock_process_id",
                table: "stockprocessdetail",
                column: "stock_process_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "action_log");

            migrationBuilder.DropTable(
                name: "asn");

            migrationBuilder.DropTable(
                name: "asnsort");

            migrationBuilder.DropTable(
                name: "category");

            migrationBuilder.DropTable(
                name: "company");

            migrationBuilder.DropTable(
                name: "customer");

            migrationBuilder.DropTable(
                name: "dispatchpicklist");

            migrationBuilder.DropTable(
                name: "flowsetfilter");

            migrationBuilder.DropTable(
                name: "flowsetusers");

            migrationBuilder.DropTable(
                name: "freightfee");

            migrationBuilder.DropTable(
                name: "global_unique_serial");

            migrationBuilder.DropTable(
                name: "goodslocation");

            migrationBuilder.DropTable(
                name: "goodsowner");

            migrationBuilder.DropTable(
                name: "menu");

            migrationBuilder.DropTable(
                name: "rolemenu");

            migrationBuilder.DropTable(
                name: "sku_safety_stock");

            migrationBuilder.DropTable(
                name: "stock");

            migrationBuilder.DropTable(
                name: "stockadjust");

            migrationBuilder.DropTable(
                name: "stockfreeze");

            migrationBuilder.DropTable(
                name: "stockmove");

            migrationBuilder.DropTable(
                name: "stockprocessdetail");

            migrationBuilder.DropTable(
                name: "stocktaking");

            migrationBuilder.DropTable(
                name: "supplier");

            migrationBuilder.DropTable(
                name: "user");

            migrationBuilder.DropTable(
                name: "user_defined_print_solution");

            migrationBuilder.DropTable(
                name: "userrole");

            migrationBuilder.DropTable(
                name: "warehouse");

            migrationBuilder.DropTable(
                name: "warehousearea");

            migrationBuilder.DropTable(
                name: "asnmaster");

            migrationBuilder.DropTable(
                name: "dispatchlist");

            migrationBuilder.DropTable(
                name: "flowset");

            migrationBuilder.DropTable(
                name: "sku");

            migrationBuilder.DropTable(
                name: "stockprocess");

            migrationBuilder.DropTable(
                name: "flowsetmain");

            migrationBuilder.DropTable(
                name: "spu");
        }
    }
}
