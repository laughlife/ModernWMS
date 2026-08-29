namespace ModernWMS.Tests.Database;

public sealed class DirectErpStockMigrationContractTests
{
    [Fact]
    public void Forward_migration_retires_runtime_gate_and_makes_position_fields_compatibility_only()
    {
        var root=FindRepositoryRoot();
        var sql=File.ReadAllText(Path.Combine(root,"flyway","sql",
            "V20260829120000__direct_erp_stock_packing.sql"));

        Assert.Contains("RENAME TABLE `wms_inventory_runtime_config`",sql,StringComparison.OrdinalIgnoreCase);
        Assert.Contains("`wms_inventory_runtime_config_retired_20260829`",sql,StringComparison.Ordinal);
        Assert.Contains("MODIFY COLUMN `wms_sku_id` int NULL",sql,StringComparison.OrdinalIgnoreCase);
        Assert.Contains("MODIFY COLUMN `stock_id` int NULL",sql,StringComparison.OrdinalIgnoreCase);
        Assert.Contains("MODIFY COLUMN `goods_location_id` int NULL",sql,StringComparison.OrdinalIgnoreCase);
        Assert.Contains("idx_packing_selection_erp_stock_status",sql,StringComparison.Ordinal);
        Assert.DoesNotContain("DROP TABLE `wms_erp_stock_allocation`",sql,StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DROP TABLE `wms_stock`",sql,StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Manual_rollback_restores_runtime_table_name_and_compatibility_defaults()
    {
        var root=FindRepositoryRoot();
        var sql=File.ReadAllText(Path.Combine(root,"flyway","manual",
            "rollback_direct_erp_stock_packing_20260829.sql"));

        Assert.Contains("RENAME TABLE `wms_inventory_runtime_config_retired_20260829`",sql,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("UPDATE `wms_packing_task_stock_selection`",sql,StringComparison.OrdinalIgnoreCase);
        Assert.Contains("UPDATE `wms_dispatchpicklist`",sql,StringComparison.OrdinalIgnoreCase);
        Assert.Contains("UPDATE `wms_weighing_box_item`",sql,StringComparison.OrdinalIgnoreCase);
        Assert.Contains("MODIFY COLUMN `wms_sku_id` int NOT NULL",sql,StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void New_packing_rows_write_null_instead_of_fake_position_identities()
    {
        var root=FindRepositoryRoot();
        var packing=File.ReadAllText(Path.Combine(root,"backend","ModernWMS.WMS","Services",
            "PackingTask","DapperPackingTaskQueryDataSource.cs"));
        var picking=File.ReadAllText(Path.Combine(root,"backend","ModernWMS.WMS","Services",
            "DispatchWorkflow","DispatchWorkflowService.Picking.cs"));
        var actual=File.ReadAllText(Path.Combine(root,"backend","ModernWMS.WMS","Services",
            "DispatchWorkflow","DispatchWorkflowService.ActualPacking.cs"));

        Assert.Contains("(@TaskId,@ItemId,NULL,NULL,@ErpStockId,NULL",packing,StringComparison.Ordinal);
        Assert.Contains("NULL,NULL,@SkuCode",packing,StringComparison.Ordinal);
        Assert.Contains("stockId=(int?)null",picking,StringComparison.Ordinal);
        Assert.Contains("ownerId=(int?)null",picking,StringComparison.Ordinal);
        Assert.Contains("locationId=(int?)null",picking,StringComparison.Ordinal);
        Assert.Contains("skuId=(int?)null",picking,StringComparison.Ordinal);
        Assert.Contains("VALUES (@detailId,@taskItemId,NULL,@erpStockId,NULL",actual,StringComparison.Ordinal);
        Assert.Contains("NULL,NULL,NULL,@quantity",actual,StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var directory=new DirectoryInfo(AppContext.BaseDirectory);
        while(directory is not null&&!Directory.Exists(Path.Combine(directory.FullName,"flyway")))
            directory=directory.Parent;
        return directory?.FullName??throw new DirectoryNotFoundException("ModernWMS repository root not found");
    }
}
