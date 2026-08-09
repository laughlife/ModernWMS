using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ModernWMS.Migrations
{
    /// <inheritdoc />
    public partial class UnifyDatabaseWithWmsPrefix : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_asn_asnmaster_asnmaster_id",
                table: "asn");

            migrationBuilder.DropForeignKey(
                name: "FK_dispatchpicklist_dispatchlist_dispatchlist_id",
                table: "dispatchpicklist");

            migrationBuilder.DropForeignKey(
                name: "FK_flowset_flowsetmain_flowsetmain_id",
                table: "flowset");

            migrationBuilder.DropForeignKey(
                name: "FK_flowsetfilter_flowset_flowset_id",
                table: "flowsetfilter");

            migrationBuilder.DropForeignKey(
                name: "FK_flowsetusers_flowset_flowset_id",
                table: "flowsetusers");

            migrationBuilder.DropForeignKey(
                name: "FK_sku_spu_spu_id",
                table: "sku");

            migrationBuilder.DropForeignKey(
                name: "FK_sku_safety_stock_sku_sku_id",
                table: "sku_safety_stock");

            migrationBuilder.DropForeignKey(
                name: "FK_stockprocessdetail_stockprocess_stock_process_id",
                table: "stockprocessdetail");

            migrationBuilder.DropPrimaryKey(
                name: "PK_warehousearea",
                table: "warehousearea");

            migrationBuilder.DropPrimaryKey(
                name: "PK_warehouse",
                table: "warehouse");

            migrationBuilder.DropPrimaryKey(
                name: "PK_userrole",
                table: "userrole");

            migrationBuilder.DropPrimaryKey(
                name: "PK_user_defined_print_solution",
                table: "user_defined_print_solution");

            migrationBuilder.DropPrimaryKey(
                name: "PK_user",
                table: "user");

            migrationBuilder.DropPrimaryKey(
                name: "PK_supplier",
                table: "supplier");

            migrationBuilder.DropPrimaryKey(
                name: "PK_stocktaking",
                table: "stocktaking");

            migrationBuilder.DropPrimaryKey(
                name: "PK_stockprocessdetail",
                table: "stockprocessdetail");

            migrationBuilder.DropPrimaryKey(
                name: "PK_stockprocess",
                table: "stockprocess");

            migrationBuilder.DropPrimaryKey(
                name: "PK_stockmove",
                table: "stockmove");

            migrationBuilder.DropPrimaryKey(
                name: "PK_stockfreeze",
                table: "stockfreeze");

            migrationBuilder.DropPrimaryKey(
                name: "PK_stockadjust",
                table: "stockadjust");

            migrationBuilder.DropPrimaryKey(
                name: "PK_stock",
                table: "stock");

            migrationBuilder.DropPrimaryKey(
                name: "PK_spu",
                table: "spu");

            migrationBuilder.DropPrimaryKey(
                name: "PK_sku_safety_stock",
                table: "sku_safety_stock");

            migrationBuilder.DropPrimaryKey(
                name: "PK_sku",
                table: "sku");

            migrationBuilder.DropPrimaryKey(
                name: "PK_rolemenu",
                table: "rolemenu");

            migrationBuilder.DropPrimaryKey(
                name: "PK_menu",
                table: "menu");

            migrationBuilder.DropPrimaryKey(
                name: "PK_goodsowner",
                table: "goodsowner");

            migrationBuilder.DropPrimaryKey(
                name: "PK_goodslocation",
                table: "goodslocation");

            migrationBuilder.DropPrimaryKey(
                name: "PK_global_unique_serial",
                table: "global_unique_serial");

            migrationBuilder.DropPrimaryKey(
                name: "PK_freightfee",
                table: "freightfee");

            migrationBuilder.DropPrimaryKey(
                name: "PK_flowsetusers",
                table: "flowsetusers");

            migrationBuilder.DropPrimaryKey(
                name: "PK_flowsetmain",
                table: "flowsetmain");

            migrationBuilder.DropPrimaryKey(
                name: "PK_flowsetfilter",
                table: "flowsetfilter");

            migrationBuilder.DropPrimaryKey(
                name: "PK_flowset",
                table: "flowset");

            migrationBuilder.DropPrimaryKey(
                name: "PK_dispatchpicklist",
                table: "dispatchpicklist");

            migrationBuilder.DropPrimaryKey(
                name: "PK_dispatchlist",
                table: "dispatchlist");

            migrationBuilder.DropPrimaryKey(
                name: "PK_company",
                table: "company");

            migrationBuilder.DropPrimaryKey(
                name: "PK_asnsort",
                table: "asnsort");

            migrationBuilder.DropPrimaryKey(
                name: "PK_asnmaster",
                table: "asnmaster");

            migrationBuilder.DropPrimaryKey(
                name: "PK_asn",
                table: "asn");

            migrationBuilder.DropPrimaryKey(
                name: "PK_action_log",
                table: "action_log");

            migrationBuilder.RenameTable(
                name: "warehousearea",
                newName: "wms_warehousearea");

            migrationBuilder.RenameTable(
                name: "warehouse",
                newName: "wms_warehouse");

            migrationBuilder.RenameTable(
                name: "userrole",
                newName: "wms_userrole");

            migrationBuilder.RenameTable(
                name: "user_defined_print_solution",
                newName: "wms_user_defined_print_solution");

            migrationBuilder.RenameTable(
                name: "user",
                newName: "wms_user");

            migrationBuilder.RenameTable(
                name: "supplier",
                newName: "wms_supplier");

            migrationBuilder.RenameTable(
                name: "stocktaking",
                newName: "wms_stocktaking");

            migrationBuilder.RenameTable(
                name: "stockprocessdetail",
                newName: "wms_stockprocessdetail");

            migrationBuilder.RenameTable(
                name: "stockprocess",
                newName: "wms_stockprocess");

            migrationBuilder.RenameTable(
                name: "stockmove",
                newName: "wms_stockmove");

            migrationBuilder.RenameTable(
                name: "stockfreeze",
                newName: "wms_stockfreeze");

            migrationBuilder.RenameTable(
                name: "stockadjust",
                newName: "wms_stockadjust");

            migrationBuilder.RenameTable(
                name: "stock",
                newName: "wms_stock");

            migrationBuilder.RenameTable(
                name: "spu",
                newName: "wms_spu");

            migrationBuilder.RenameTable(
                name: "sku_safety_stock",
                newName: "wms_sku_safety_stock");

            migrationBuilder.RenameTable(
                name: "sku",
                newName: "wms_sku");

            migrationBuilder.RenameTable(
                name: "rolemenu",
                newName: "wms_rolemenu");

            migrationBuilder.RenameTable(
                name: "menu",
                newName: "wms_menu");

            migrationBuilder.RenameTable(
                name: "goodsowner",
                newName: "wms_goodsowner");

            migrationBuilder.RenameTable(
                name: "goodslocation",
                newName: "wms_goodslocation");

            migrationBuilder.RenameTable(
                name: "global_unique_serial",
                newName: "wms_global_unique_serial");

            migrationBuilder.RenameTable(
                name: "freightfee",
                newName: "wms_freightfee");

            migrationBuilder.RenameTable(
                name: "flowsetusers",
                newName: "wms_flowsetusers");

            migrationBuilder.RenameTable(
                name: "flowsetmain",
                newName: "wms_flowsetmain");

            migrationBuilder.RenameTable(
                name: "flowsetfilter",
                newName: "wms_flowsetfilter");

            migrationBuilder.RenameTable(
                name: "flowset",
                newName: "wms_flowset");

            migrationBuilder.RenameTable(
                name: "dispatchpicklist",
                newName: "wms_dispatchpicklist");

            migrationBuilder.RenameTable(
                name: "dispatchlist",
                newName: "wms_dispatchlist");

            migrationBuilder.RenameTable(
                name: "company",
                newName: "wms_company");

            migrationBuilder.RenameTable(
                name: "asnsort",
                newName: "wms_asnsort");

            migrationBuilder.RenameTable(
                name: "asnmaster",
                newName: "wms_asnmaster");

            migrationBuilder.RenameTable(
                name: "asn",
                newName: "wms_asn");

            migrationBuilder.RenameTable(
                name: "action_log",
                newName: "wms_action_log");

            migrationBuilder.RenameIndex(
                name: "IX_warehouse_erp_warehouse_id",
                table: "wms_warehouse",
                newName: "IX_wms_warehouse_erp_warehouse_id");

            migrationBuilder.RenameIndex(
                name: "IX_stockprocessdetail_stock_process_id",
                table: "wms_stockprocessdetail",
                newName: "IX_wms_stockprocessdetail_stock_process_id");

            migrationBuilder.RenameIndex(
                name: "IX_sku_safety_stock_sku_id",
                table: "wms_sku_safety_stock",
                newName: "IX_wms_sku_safety_stock_sku_id");

            migrationBuilder.RenameIndex(
                name: "IX_sku_spu_id",
                table: "wms_sku",
                newName: "IX_wms_sku_spu_id");

            migrationBuilder.RenameIndex(
                name: "IX_flowsetusers_flowset_id",
                table: "wms_flowsetusers",
                newName: "IX_wms_flowsetusers_flowset_id");

            migrationBuilder.RenameIndex(
                name: "IX_flowsetfilter_flowset_id",
                table: "wms_flowsetfilter",
                newName: "IX_wms_flowsetfilter_flowset_id");

            migrationBuilder.RenameIndex(
                name: "IX_flowset_flowsetmain_id",
                table: "wms_flowset",
                newName: "IX_wms_flowset_flowsetmain_id");

            migrationBuilder.RenameIndex(
                name: "IX_dispatchpicklist_dispatchlist_id",
                table: "wms_dispatchpicklist",
                newName: "IX_wms_dispatchpicklist_dispatchlist_id");

            migrationBuilder.RenameIndex(
                name: "IX_asn_asnmaster_id",
                table: "wms_asn",
                newName: "IX_wms_asn_asnmaster_id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_wms_warehousearea",
                table: "wms_warehousearea",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_wms_warehouse",
                table: "wms_warehouse",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_wms_userrole",
                table: "wms_userrole",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_wms_user_defined_print_solution",
                table: "wms_user_defined_print_solution",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_wms_user",
                table: "wms_user",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_wms_supplier",
                table: "wms_supplier",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_wms_stocktaking",
                table: "wms_stocktaking",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_wms_stockprocessdetail",
                table: "wms_stockprocessdetail",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_wms_stockprocess",
                table: "wms_stockprocess",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_wms_stockmove",
                table: "wms_stockmove",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_wms_stockfreeze",
                table: "wms_stockfreeze",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_wms_stockadjust",
                table: "wms_stockadjust",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_wms_stock",
                table: "wms_stock",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_wms_spu",
                table: "wms_spu",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_wms_sku_safety_stock",
                table: "wms_sku_safety_stock",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_wms_sku",
                table: "wms_sku",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_wms_rolemenu",
                table: "wms_rolemenu",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_wms_menu",
                table: "wms_menu",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_wms_goodsowner",
                table: "wms_goodsowner",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_wms_goodslocation",
                table: "wms_goodslocation",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_wms_global_unique_serial",
                table: "wms_global_unique_serial",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_wms_freightfee",
                table: "wms_freightfee",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_wms_flowsetusers",
                table: "wms_flowsetusers",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_wms_flowsetmain",
                table: "wms_flowsetmain",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_wms_flowsetfilter",
                table: "wms_flowsetfilter",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_wms_flowset",
                table: "wms_flowset",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_wms_dispatchpicklist",
                table: "wms_dispatchpicklist",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_wms_dispatchlist",
                table: "wms_dispatchlist",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_wms_company",
                table: "wms_company",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_wms_asnsort",
                table: "wms_asnsort",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_wms_asnmaster",
                table: "wms_asnmaster",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_wms_asn",
                table: "wms_asn",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_wms_action_log",
                table: "wms_action_log",
                column: "id");

            migrationBuilder.AddForeignKey(
                name: "FK_wms_asn_wms_asnmaster_asnmaster_id",
                table: "wms_asn",
                column: "asnmaster_id",
                principalTable: "wms_asnmaster",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_wms_dispatchpicklist_wms_dispatchlist_dispatchlist_id",
                table: "wms_dispatchpicklist",
                column: "dispatchlist_id",
                principalTable: "wms_dispatchlist",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_wms_flowset_wms_flowsetmain_flowsetmain_id",
                table: "wms_flowset",
                column: "flowsetmain_id",
                principalTable: "wms_flowsetmain",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_wms_flowsetfilter_wms_flowset_flowset_id",
                table: "wms_flowsetfilter",
                column: "flowset_id",
                principalTable: "wms_flowset",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_wms_flowsetusers_wms_flowset_flowset_id",
                table: "wms_flowsetusers",
                column: "flowset_id",
                principalTable: "wms_flowset",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_wms_sku_wms_spu_spu_id",
                table: "wms_sku",
                column: "spu_id",
                principalTable: "wms_spu",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_wms_sku_safety_stock_wms_sku_sku_id",
                table: "wms_sku_safety_stock",
                column: "sku_id",
                principalTable: "wms_sku",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_wms_stockprocessdetail_wms_stockprocess_stock_process_id",
                table: "wms_stockprocessdetail",
                column: "stock_process_id",
                principalTable: "wms_stockprocess",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_wms_asn_wms_asnmaster_asnmaster_id",
                table: "wms_asn");

            migrationBuilder.DropForeignKey(
                name: "FK_wms_dispatchpicklist_wms_dispatchlist_dispatchlist_id",
                table: "wms_dispatchpicklist");

            migrationBuilder.DropForeignKey(
                name: "FK_wms_flowset_wms_flowsetmain_flowsetmain_id",
                table: "wms_flowset");

            migrationBuilder.DropForeignKey(
                name: "FK_wms_flowsetfilter_wms_flowset_flowset_id",
                table: "wms_flowsetfilter");

            migrationBuilder.DropForeignKey(
                name: "FK_wms_flowsetusers_wms_flowset_flowset_id",
                table: "wms_flowsetusers");

            migrationBuilder.DropForeignKey(
                name: "FK_wms_sku_wms_spu_spu_id",
                table: "wms_sku");

            migrationBuilder.DropForeignKey(
                name: "FK_wms_sku_safety_stock_wms_sku_sku_id",
                table: "wms_sku_safety_stock");

            migrationBuilder.DropForeignKey(
                name: "FK_wms_stockprocessdetail_wms_stockprocess_stock_process_id",
                table: "wms_stockprocessdetail");

            migrationBuilder.DropPrimaryKey(
                name: "PK_wms_warehousearea",
                table: "wms_warehousearea");

            migrationBuilder.DropPrimaryKey(
                name: "PK_wms_warehouse",
                table: "wms_warehouse");

            migrationBuilder.DropPrimaryKey(
                name: "PK_wms_userrole",
                table: "wms_userrole");

            migrationBuilder.DropPrimaryKey(
                name: "PK_wms_user_defined_print_solution",
                table: "wms_user_defined_print_solution");

            migrationBuilder.DropPrimaryKey(
                name: "PK_wms_user",
                table: "wms_user");

            migrationBuilder.DropPrimaryKey(
                name: "PK_wms_supplier",
                table: "wms_supplier");

            migrationBuilder.DropPrimaryKey(
                name: "PK_wms_stocktaking",
                table: "wms_stocktaking");

            migrationBuilder.DropPrimaryKey(
                name: "PK_wms_stockprocessdetail",
                table: "wms_stockprocessdetail");

            migrationBuilder.DropPrimaryKey(
                name: "PK_wms_stockprocess",
                table: "wms_stockprocess");

            migrationBuilder.DropPrimaryKey(
                name: "PK_wms_stockmove",
                table: "wms_stockmove");

            migrationBuilder.DropPrimaryKey(
                name: "PK_wms_stockfreeze",
                table: "wms_stockfreeze");

            migrationBuilder.DropPrimaryKey(
                name: "PK_wms_stockadjust",
                table: "wms_stockadjust");

            migrationBuilder.DropPrimaryKey(
                name: "PK_wms_stock",
                table: "wms_stock");

            migrationBuilder.DropPrimaryKey(
                name: "PK_wms_spu",
                table: "wms_spu");

            migrationBuilder.DropPrimaryKey(
                name: "PK_wms_sku_safety_stock",
                table: "wms_sku_safety_stock");

            migrationBuilder.DropPrimaryKey(
                name: "PK_wms_sku",
                table: "wms_sku");

            migrationBuilder.DropPrimaryKey(
                name: "PK_wms_rolemenu",
                table: "wms_rolemenu");

            migrationBuilder.DropPrimaryKey(
                name: "PK_wms_menu",
                table: "wms_menu");

            migrationBuilder.DropPrimaryKey(
                name: "PK_wms_goodsowner",
                table: "wms_goodsowner");

            migrationBuilder.DropPrimaryKey(
                name: "PK_wms_goodslocation",
                table: "wms_goodslocation");

            migrationBuilder.DropPrimaryKey(
                name: "PK_wms_global_unique_serial",
                table: "wms_global_unique_serial");

            migrationBuilder.DropPrimaryKey(
                name: "PK_wms_freightfee",
                table: "wms_freightfee");

            migrationBuilder.DropPrimaryKey(
                name: "PK_wms_flowsetusers",
                table: "wms_flowsetusers");

            migrationBuilder.DropPrimaryKey(
                name: "PK_wms_flowsetmain",
                table: "wms_flowsetmain");

            migrationBuilder.DropPrimaryKey(
                name: "PK_wms_flowsetfilter",
                table: "wms_flowsetfilter");

            migrationBuilder.DropPrimaryKey(
                name: "PK_wms_flowset",
                table: "wms_flowset");

            migrationBuilder.DropPrimaryKey(
                name: "PK_wms_dispatchpicklist",
                table: "wms_dispatchpicklist");

            migrationBuilder.DropPrimaryKey(
                name: "PK_wms_dispatchlist",
                table: "wms_dispatchlist");

            migrationBuilder.DropPrimaryKey(
                name: "PK_wms_company",
                table: "wms_company");

            migrationBuilder.DropPrimaryKey(
                name: "PK_wms_asnsort",
                table: "wms_asnsort");

            migrationBuilder.DropPrimaryKey(
                name: "PK_wms_asnmaster",
                table: "wms_asnmaster");

            migrationBuilder.DropPrimaryKey(
                name: "PK_wms_asn",
                table: "wms_asn");

            migrationBuilder.DropPrimaryKey(
                name: "PK_wms_action_log",
                table: "wms_action_log");

            migrationBuilder.RenameTable(
                name: "wms_warehousearea",
                newName: "warehousearea");

            migrationBuilder.RenameTable(
                name: "wms_warehouse",
                newName: "warehouse");

            migrationBuilder.RenameTable(
                name: "wms_userrole",
                newName: "userrole");

            migrationBuilder.RenameTable(
                name: "wms_user_defined_print_solution",
                newName: "user_defined_print_solution");

            migrationBuilder.RenameTable(
                name: "wms_user",
                newName: "user");

            migrationBuilder.RenameTable(
                name: "wms_supplier",
                newName: "supplier");

            migrationBuilder.RenameTable(
                name: "wms_stocktaking",
                newName: "stocktaking");

            migrationBuilder.RenameTable(
                name: "wms_stockprocessdetail",
                newName: "stockprocessdetail");

            migrationBuilder.RenameTable(
                name: "wms_stockprocess",
                newName: "stockprocess");

            migrationBuilder.RenameTable(
                name: "wms_stockmove",
                newName: "stockmove");

            migrationBuilder.RenameTable(
                name: "wms_stockfreeze",
                newName: "stockfreeze");

            migrationBuilder.RenameTable(
                name: "wms_stockadjust",
                newName: "stockadjust");

            migrationBuilder.RenameTable(
                name: "wms_stock",
                newName: "stock");

            migrationBuilder.RenameTable(
                name: "wms_spu",
                newName: "spu");

            migrationBuilder.RenameTable(
                name: "wms_sku_safety_stock",
                newName: "sku_safety_stock");

            migrationBuilder.RenameTable(
                name: "wms_sku",
                newName: "sku");

            migrationBuilder.RenameTable(
                name: "wms_rolemenu",
                newName: "rolemenu");

            migrationBuilder.RenameTable(
                name: "wms_menu",
                newName: "menu");

            migrationBuilder.RenameTable(
                name: "wms_goodsowner",
                newName: "goodsowner");

            migrationBuilder.RenameTable(
                name: "wms_goodslocation",
                newName: "goodslocation");

            migrationBuilder.RenameTable(
                name: "wms_global_unique_serial",
                newName: "global_unique_serial");

            migrationBuilder.RenameTable(
                name: "wms_freightfee",
                newName: "freightfee");

            migrationBuilder.RenameTable(
                name: "wms_flowsetusers",
                newName: "flowsetusers");

            migrationBuilder.RenameTable(
                name: "wms_flowsetmain",
                newName: "flowsetmain");

            migrationBuilder.RenameTable(
                name: "wms_flowsetfilter",
                newName: "flowsetfilter");

            migrationBuilder.RenameTable(
                name: "wms_flowset",
                newName: "flowset");

            migrationBuilder.RenameTable(
                name: "wms_dispatchpicklist",
                newName: "dispatchpicklist");

            migrationBuilder.RenameTable(
                name: "wms_dispatchlist",
                newName: "dispatchlist");

            migrationBuilder.RenameTable(
                name: "wms_company",
                newName: "company");

            migrationBuilder.RenameTable(
                name: "wms_asnsort",
                newName: "asnsort");

            migrationBuilder.RenameTable(
                name: "wms_asnmaster",
                newName: "asnmaster");

            migrationBuilder.RenameTable(
                name: "wms_asn",
                newName: "asn");

            migrationBuilder.RenameTable(
                name: "wms_action_log",
                newName: "action_log");

            migrationBuilder.RenameIndex(
                name: "IX_wms_warehouse_erp_warehouse_id",
                table: "warehouse",
                newName: "IX_warehouse_erp_warehouse_id");

            migrationBuilder.RenameIndex(
                name: "IX_wms_stockprocessdetail_stock_process_id",
                table: "stockprocessdetail",
                newName: "IX_stockprocessdetail_stock_process_id");

            migrationBuilder.RenameIndex(
                name: "IX_wms_sku_safety_stock_sku_id",
                table: "sku_safety_stock",
                newName: "IX_sku_safety_stock_sku_id");

            migrationBuilder.RenameIndex(
                name: "IX_wms_sku_spu_id",
                table: "sku",
                newName: "IX_sku_spu_id");

            migrationBuilder.RenameIndex(
                name: "IX_wms_flowsetusers_flowset_id",
                table: "flowsetusers",
                newName: "IX_flowsetusers_flowset_id");

            migrationBuilder.RenameIndex(
                name: "IX_wms_flowsetfilter_flowset_id",
                table: "flowsetfilter",
                newName: "IX_flowsetfilter_flowset_id");

            migrationBuilder.RenameIndex(
                name: "IX_wms_flowset_flowsetmain_id",
                table: "flowset",
                newName: "IX_flowset_flowsetmain_id");

            migrationBuilder.RenameIndex(
                name: "IX_wms_dispatchpicklist_dispatchlist_id",
                table: "dispatchpicklist",
                newName: "IX_dispatchpicklist_dispatchlist_id");

            migrationBuilder.RenameIndex(
                name: "IX_wms_asn_asnmaster_id",
                table: "asn",
                newName: "IX_asn_asnmaster_id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_warehousearea",
                table: "warehousearea",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_warehouse",
                table: "warehouse",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_userrole",
                table: "userrole",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_user_defined_print_solution",
                table: "user_defined_print_solution",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_user",
                table: "user",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_supplier",
                table: "supplier",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_stocktaking",
                table: "stocktaking",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_stockprocessdetail",
                table: "stockprocessdetail",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_stockprocess",
                table: "stockprocess",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_stockmove",
                table: "stockmove",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_stockfreeze",
                table: "stockfreeze",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_stockadjust",
                table: "stockadjust",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_stock",
                table: "stock",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_spu",
                table: "spu",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_sku_safety_stock",
                table: "sku_safety_stock",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_sku",
                table: "sku",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_rolemenu",
                table: "rolemenu",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_menu",
                table: "menu",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_goodsowner",
                table: "goodsowner",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_goodslocation",
                table: "goodslocation",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_global_unique_serial",
                table: "global_unique_serial",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_freightfee",
                table: "freightfee",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_flowsetusers",
                table: "flowsetusers",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_flowsetmain",
                table: "flowsetmain",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_flowsetfilter",
                table: "flowsetfilter",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_flowset",
                table: "flowset",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_dispatchpicklist",
                table: "dispatchpicklist",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_dispatchlist",
                table: "dispatchlist",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_company",
                table: "company",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_asnsort",
                table: "asnsort",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_asnmaster",
                table: "asnmaster",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_asn",
                table: "asn",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_action_log",
                table: "action_log",
                column: "id");

            migrationBuilder.AddForeignKey(
                name: "FK_asn_asnmaster_asnmaster_id",
                table: "asn",
                column: "asnmaster_id",
                principalTable: "asnmaster",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_dispatchpicklist_dispatchlist_dispatchlist_id",
                table: "dispatchpicklist",
                column: "dispatchlist_id",
                principalTable: "dispatchlist",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_flowset_flowsetmain_flowsetmain_id",
                table: "flowset",
                column: "flowsetmain_id",
                principalTable: "flowsetmain",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_flowsetfilter_flowset_flowset_id",
                table: "flowsetfilter",
                column: "flowset_id",
                principalTable: "flowset",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_flowsetusers_flowset_flowset_id",
                table: "flowsetusers",
                column: "flowset_id",
                principalTable: "flowset",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_sku_spu_spu_id",
                table: "sku",
                column: "spu_id",
                principalTable: "spu",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_sku_safety_stock_sku_sku_id",
                table: "sku_safety_stock",
                column: "sku_id",
                principalTable: "sku",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_stockprocessdetail_stockprocess_stock_process_id",
                table: "stockprocessdetail",
                column: "stock_process_id",
                principalTable: "stockprocess",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
