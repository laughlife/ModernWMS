using Microsoft.EntityFrameworkCore;
using ModernWMS.Core.DBContext;
using ModernWMS.Core.DBContext.Entities;
using ModernWMS.Core.JWT;
using ModernWMS.Core.Models;
using ModernWMS.WMS.Services;

namespace ModernWMS.Tests.FbaShipment;

public class FbaShipmentServiceTests
{
    [Fact]
    public async Task PageAsync_includes_the_erp_shipment_creator()
    {
        await using var wmsDatabase = CreateWmsDatabase();
        await using var ruoyiDatabase = CreateRuoyiDatabase();
        await ruoyiDatabase.StockMoves.AddAsync(new ErpStockMoveEntity
        {
            id = 1,
            no = "MOVE-001",
            from_warehouse_id = 320118,
            transfer_type = "OVERSEA_FBA_SHIPMENT",
            status = "WAIT_SHIPMENT",
            shipment_status = "WAIT_SHIPMENT",
            creator = "ERP创建人",
            create_time = new DateTime(2026, 8, 11, 10, 0, 0),
            update_time = new DateTime(2026, 8, 11, 10, 0, 0)
        });
        await ruoyiDatabase.SaveChangesAsync();

        var service = new FbaShipmentService(ruoyiDatabase, wmsDatabase, null!);
        var (rows, totals) = await service.PageAsync(new PageSearch(), new CurrentUser { tenant_id = 1 });

        Assert.Equal(1, totals);
        Assert.Equal("ERP创建人", Assert.Single(rows).creator);
    }

    private static SqlDBContext CreateWmsDatabase()
    {
        var options = new DbContextOptionsBuilder<SqlDBContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new SqlDBContext(options);
    }

    private static RuoyiDbContext CreateRuoyiDatabase()
    {
        var options = new DbContextOptionsBuilder<RuoyiDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new RuoyiDbContext(options);
    }
}
