using Dapper;
using MySqlConnector;

namespace ModernWMS.Tests.Database;

public sealed class ActualPackingMigrationMySqlIntegrationTests
{
    [DevelopmentMySqlFact]
    public async Task Development_schema_upgrades_existing_box_items_without_creating_an_actual_item_table()
    {
        var connectionString = Environment.GetEnvironmentVariable("MODERNWMS_TEST_MYSQL")!;
        var settings = new MySqlConnectionStringBuilder(connectionString);
        Assert.Equal("ruoyi-vue-pro", settings.Database);

        await using var connection = new MySqlConnection(connectionString);
        await connection.OpenAsync();

        Assert.False(await TableExistsAsync(connection, "wms_weighing_box_inventory_item"));
        Assert.True(await ColumnExistsAsync(connection, "wms_weighing_box_item", "actual_qty"));
        Assert.True(await ColumnExistsAsync(connection, "wms_weighing_box_item", "stock_allocation_id"));
        Assert.True(await ColumnExistsAsync(connection, "wms_weighing_box_item", "client_line_key"));
        Assert.False(await ColumnExistsAsync(connection, "wms_weighing_box_item", "task_qty"));
        Assert.False(await CheckExistsAsync(connection, "ck_erp_stock_allocation_allocated_nonnegative"));
        Assert.False(await CheckExistsAsync(connection, "ck_erp_stock_allocation_occupied_within_allocated"));
        Assert.True(await CheckExistsAsync(connection, "ck_erp_stock_allocation_occupied_nonnegative"));
        Assert.True(await CheckExistsAsync(connection, "ck_weighing_box_item_actual_qty_positive"));
    }

    private static Task<bool> TableExistsAsync(MySqlConnection connection, string tableName) =>
        connection.ExecuteScalarAsync<bool>("""
            SELECT EXISTS(
                SELECT 1 FROM information_schema.tables
                 WHERE table_schema=DATABASE() AND table_name=@tableName);
            """, new { tableName });

    private static Task<bool> ColumnExistsAsync(
        MySqlConnection connection,
        string tableName,
        string columnName) =>
        connection.ExecuteScalarAsync<bool>("""
            SELECT EXISTS(
                SELECT 1 FROM information_schema.columns
                 WHERE table_schema=DATABASE() AND table_name=@tableName AND column_name=@columnName);
            """, new { tableName, columnName });

    private static Task<bool> CheckExistsAsync(MySqlConnection connection, string constraintName) =>
        connection.ExecuteScalarAsync<bool>("""
            SELECT EXISTS(
                SELECT 1 FROM information_schema.table_constraints
                 WHERE constraint_schema=DATABASE()
                   AND constraint_type='CHECK'
                   AND constraint_name=@constraintName);
            """, new { constraintName });
}
