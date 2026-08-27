namespace ModernWMS.Tests.Database;

public sealed class RemoveTenantMigrationContractTests
{
    [Fact]
    public void Migration_rebuilds_affected_indexes_and_drops_every_scoped_physical_column()
    {
        var repository = FindRepositoryRoot();
        var migration = File.ReadAllText(Path.Combine(repository, "flyway", "sql",
            "V20260827090000__remove_tenant_dependencies.sql"));

        Assert.Contains("IF(non_unique_value = 0, 'UNIQUE ', '')", migration, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("DROP INDEX", migration, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("DROP COLUMN `tenant_id`", migration, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("wms_packing_task_stock_selection", migration, StringComparison.Ordinal);
        Assert.Contains("trk_stock_reservation_command_item", migration, StringComparison.Ordinal);
        Assert.DoesNotMatch("(?is)tenant_id.{0,80}DEFAULT\\s+[01]", migration);
        Assert.DoesNotContain("COALESCE", migration, StringComparison.OrdinalIgnoreCase);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "flyway")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("ModernWMS repository root not found");
    }
}
