using ModernWMS.Initialization;

namespace ModernWMS.Tests.Initialization;

public class DatabaseMigrationPreflightTests
{
    private static readonly string[] RenamedTables =
    [
        "warehousearea", "warehouse", "userrole", "user_defined_print_solution", "user", "supplier",
        "stocktaking", "stockprocessdetail", "stockprocess", "stockmove", "stockfreeze", "stockadjust",
        "stock", "spu", "sku_safety_stock", "sku", "rolemenu", "menu", "goodsowner", "goodslocation",
        "global_unique_serial", "freightfee", "flowsetusers", "flowsetmain", "flowsetfilter", "flowset",
        "dispatchpicklist", "dispatchlist", "company", "asnsort", "asnmaster", "asn", "action_log"
    ];

    [Fact]
    public async Task Empty_database_is_allowed()
    {
        var preflight = CreatePreflight([], []);

        await preflight.EnsureSafeAsync();
    }

    [Fact]
    public async Task Clean_schema_before_prefix_migration_is_allowed()
    {
        var preflight = CreatePreflight(
            ["20260808020000_AddWarehouseErpBinding"],
            RenamedTables);

        await preflight.EnsureSafeAsync();
    }

    [Fact]
    public async Task Schema_with_recorded_prefix_migration_is_allowed()
    {
        var preflight = CreatePreflight(
            [DatabaseMigrationPreflight.WmsPrefixMigrationId],
            PrefixedTables());

        await preflight.EnsureSafeAsync();
    }

    [Fact]
    public async Task Recorded_prefix_migration_with_missing_target_table_is_blocked()
    {
        var preflight = CreatePreflight(
            [DatabaseMigrationPreflight.WmsPrefixMigrationId],
            ["wms_asn", "wms_warehouse"]);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => preflight.EnsureSafeAsync());

        Assert.Contains("wms_asnmaster", exception.Message);
        Assert.Contains("迁移历史已记录", exception.Message);
    }

    [Fact]
    public async Task Recorded_prefix_migration_missing_non_anchor_target_table_is_blocked()
    {
        var tables = PrefixedTables()
            .Where(table => table != "wms_action_log")
            .ToArray();
        var preflight = CreatePreflight(
            [DatabaseMigrationPreflight.WmsPrefixMigrationId],
            tables);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => preflight.EnsureSafeAsync());

        Assert.Contains("wms_action_log", exception.Message);
    }

    [Fact]
    public async Task Recorded_prefix_migration_with_recreated_old_table_is_blocked()
    {
        var preflight = CreatePreflight(
            [DatabaseMigrationPreflight.WmsPrefixMigrationId],
            ["asn", .. PrefixedTables()]);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => preflight.EnsureSafeAsync());

        Assert.Contains("旧裸表", exception.Message);
        Assert.Contains("asn", exception.Message);
        Assert.Contains("旧版本程序", exception.Message);
    }

    [Fact]
    public async Task Old_and_prefixed_tables_without_prefix_history_are_blocked()
    {
        var preflight = CreatePreflight(
            ["20260808020000_AddWarehouseErpBinding"],
            ["asn", "asnmaster", "wms_asn", "wms_asnmaster", "wms_erp_receipt_record"]);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => preflight.EnsureSafeAsync());

        Assert.Contains(DatabaseMigrationPreflight.WmsPrefixMigrationId, exception.Message);
        Assert.Contains("asn", exception.Message);
        Assert.Contains("wms_asn", exception.Message);
        Assert.Contains("禁止自动继续迁移", exception.Message);
        Assert.Contains("备份", exception.Message);
    }

    [Fact]
    public async Task Prefixed_tables_without_prefix_history_are_blocked_even_when_old_tables_are_absent()
    {
        var preflight = CreatePreflight(
            ["20260808020000_AddWarehouseErpBinding"],
            ["wms_asn", "wms_asnmaster"]);

        await Assert.ThrowsAsync<InvalidOperationException>(() => preflight.EnsureSafeAsync());
    }

    [Fact]
    public async Task Existing_migration_history_with_incomplete_old_schema_is_blocked()
    {
        var tables = RenamedTables
            .Where(table => table != "action_log")
            .ToArray();
        var preflight = CreatePreflight(
            ["20260808020000_AddWarehouseErpBinding"],
            tables);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => preflight.EnsureSafeAsync());

        Assert.Contains("action_log", exception.Message);
        Assert.Contains("旧源表不完整", exception.Message);
    }

    [Fact]
    public async Task Empty_migration_history_with_partial_old_schema_is_blocked()
    {
        var preflight = CreatePreflight([], ["asn"]);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => preflight.EnsureSafeAsync());

        Assert.Contains("迁移历史为空", exception.Message);
        Assert.Contains("asn", exception.Message);
    }

    private static DatabaseMigrationPreflight CreatePreflight(
        IReadOnlyCollection<string> appliedMigrations,
        IReadOnlyCollection<string> tables)
    {
        return new DatabaseMigrationPreflight(
            new StubDatabaseSchemaInspector(
                new DatabaseSchemaSnapshot(appliedMigrations, tables)));
    }

    private static string[] PrefixedTables()
    {
        return RenamedTables.Select(table => $"wms_{table}").ToArray();
    }

    private sealed class StubDatabaseSchemaInspector(DatabaseSchemaSnapshot snapshot)
        : IDatabaseSchemaInspector
    {
        public Task<DatabaseSchemaSnapshot> InspectAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(snapshot);
        }
    }
}
