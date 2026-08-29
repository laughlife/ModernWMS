using Dapper;
using ModernWMS.WMS.Services.StockAllocation;
using MySqlConnector;

namespace ModernWMS.Tests.Database;

public sealed class DirectErpStockMigrationMySqlIntegrationTests
{
    [DevelopmentMySqlFact]
    public async Task Plan_a_selection_insert_accepts_stock_only_identity_on_authorized_development_schema()
    {
        var connectionString = Environment.GetEnvironmentVariable("MODERNWMS_TEST_MYSQL")!;
        var settings = new MySqlConnectionStringBuilder(connectionString);
        Assert.Equal("192.168.100.2", settings.Server);
        Assert.Equal("ruoyi-vue-pro", settings.Database);

        await using var connection = new MySqlConnection(connectionString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        try
        {
            var marker = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var inserted = await connection.ExecuteAsync("""
                INSERT INTO `wms_packing_task_stock_selection`
                  (`sellfox_task_id`,`sellfox_item_id`,`wms_sku_id`,`stock_id`,`erp_stock_id`,
                   `stock_allocation_id`,`reservation_id`,`reservation_item_id`,`qty`,
                   `goods_location_id`,`goods_owner_id`,`sku_code`,`selected_by`,`selected_by_name`,
                   `create_time`,`last_update_time`,`status`,`operation_source`)
                VALUES
                  (@marker,@marker,NULL,NULL,@marker,NULL,NULL,NULL,1,
                   NULL,NULL,'PLAN-A-INTEGRATION',0,'integration-test',NOW(6),NOW(6),
                   'ACTIVE','MODERN_WMS');
                """, new { marker }, transaction);

            Assert.Equal(1, inserted);
        }
        finally
        {
            await transaction.RollbackAsync();
        }
    }

    [DevelopmentMySqlFact]
    public async Task Reservation_totals_query_maps_mysql_aggregates_to_int64()
    {
        var connectionString = Environment.GetEnvironmentVariable("MODERNWMS_TEST_MYSQL")!;
        var settings = new MySqlConnectionStringBuilder(connectionString);
        Assert.Equal("192.168.100.2", settings.Server);
        Assert.Equal("ruoyi-vue-pro", settings.Database);

        await using var connection = new MySqlConnection(connectionString);
        await connection.OpenAsync();
        var totals = await connection.QuerySingleAsync<ReservationTotals>(
            StockReservationMutationCoordinator.ReservationTotalsSql,
            new { reservationId = long.MaxValue });

        Assert.Equal(0, totals.RemainingQty);
        Assert.Equal(0, totals.ReleasedQty);
        Assert.Equal(0, totals.ConsumedQty);
    }

    [DevelopmentMySqlFact]
    public async Task Conservation_query_maps_mysql_aggregates_to_int64()
    {
        var connectionString = Environment.GetEnvironmentVariable("MODERNWMS_TEST_MYSQL")!;
        var settings = new MySqlConnectionStringBuilder(connectionString);
        Assert.Equal("192.168.100.2", settings.Server);
        Assert.Equal("ruoyi-vue-pro", settings.Database);

        await using var connection = new MySqlConnection(connectionString);
        await connection.OpenAsync();
        var identity = await connection.QuerySingleAsync<ConservationIdentity>("""
            SELECT item.`id` ReservationItemId,item.`stock_id` StockId,allocation.`id` AllocationId
              FROM `trk_stock_reservation_item` item
              JOIN `trk_stock` stock ON stock.`id`=item.`stock_id` AND stock.`deleted`=b'0'
              CROSS JOIN `wms_erp_stock_allocation` allocation
             WHERE item.`deleted`=b'0' LIMIT 1;
            """);
        var quantities = await connection.QuerySingleAsync<ConservationTotals>(
            StockReservationMutationCoordinator.ReservationConservationSql,
            identity);

        Assert.True(quantities.ItemRemainingQty >= 0);
    }

    private sealed record ReservationTotals(long RemainingQty, long ReleasedQty, long ConsumedQty);
    private sealed record ConservationIdentity(long ReservationItemId, long StockId, long AllocationId);
    private sealed record ConservationTotals(long ItemRemainingQty, long ItemLocationRemainingQty,
        long StockOccupiedQty, long StockOwnerRemainingQty, long AllocationOccupiedQty,
        long AllocationOwnerRemainingQty);
}
