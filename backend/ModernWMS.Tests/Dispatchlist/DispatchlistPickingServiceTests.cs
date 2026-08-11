using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using ModernWMS.Core.DBContext;
using ModernWMS.Core.JWT;
using ModernWMS.WMS.Entities.Models;
using ModernWMS.WMS.Services;

namespace ModernWMS.Tests.Dispatchlist;

public class DispatchlistPickingServiceTests
{
    [Fact]
    public async Task ReturnToWeighingAsync_returns_the_whole_dispatch_and_preserves_measurements()
    {
        await using var wmsDatabase = CreateWmsDatabase();
        await using var ruoyiDatabase = CreateRuoyiDatabase();
        var firstRow = CreateDispatchRow(1, "DB20260811001", 5);
        var secondRow = CreateDispatchRow(2, "DB20260811001", 5);
        await wmsDatabase.Set<DispatchlistEntity>().AddRangeAsync(firstRow, secondRow);
        await wmsDatabase.Set<DispatchWeighingBoxEntity>().AddAsync(new DispatchWeighingBoxEntity
        {
            id = 1,
            tenant_id = 1,
            dispatch_no = "DB20260811001",
            erp_box_id = 101,
            weighing_weight = 12.5m,
            weighing_length = 30,
            weighing_width = 20,
            weighing_height = 10,
            weighing_volume = 6000,
            weighing_person = "张三"
        });
        await wmsDatabase.SaveChangesAsync();

        var service = new DispatchlistPickingService(wmsDatabase, ruoyiDatabase, new TestStringLocalizer());
        var (flag, _) = await service.ReturnToWeighingAsync(firstRow.id, AdminUser());

        Assert.True(flag);
        var rows = await wmsDatabase.Set<DispatchlistEntity>().AsNoTracking().OrderBy(t => t.id).ToListAsync();
        Assert.All(rows, row => Assert.Equal((byte)4, row.dispatch_status));
        Assert.All(rows, row => Assert.Equal(12.5m, row.weighing_weight));
        Assert.All(rows, row => Assert.Equal(5000, row.volume_divisor));
        Assert.All(rows, row => Assert.Equal("朝阳仓", row.carrier_unit));
        var box = await wmsDatabase.Set<DispatchWeighingBoxEntity>().AsNoTracking().SingleAsync();
        Assert.Equal(12.5m, box.weighing_weight);
        Assert.Equal(6000m, box.weighing_volume);
    }

    [Fact]
    public async Task ReturnToWeighingAsync_rejects_a_dispatch_with_changed_status()
    {
        await using var wmsDatabase = CreateWmsDatabase();
        await using var ruoyiDatabase = CreateRuoyiDatabase();
        var pendingRow = CreateDispatchRow(1, "DB20260811002", 5);
        var completedRow = CreateDispatchRow(2, "DB20260811002", 6);
        await wmsDatabase.Set<DispatchlistEntity>().AddRangeAsync(pendingRow, completedRow);
        await wmsDatabase.SaveChangesAsync();

        var service = new DispatchlistPickingService(wmsDatabase, ruoyiDatabase, new TestStringLocalizer());
        var (flag, msg) = await service.ReturnToWeighingAsync(pendingRow.id, AdminUser());

        Assert.False(flag);
        Assert.Equal("data_changed", msg);
        Assert.Equal((byte)5, pendingRow.dispatch_status);
        Assert.Equal((byte)6, completedRow.dispatch_status);
    }

    [Fact]
    public async Task ReturnToWeighingAsync_is_idempotent_after_the_dispatch_was_returned()
    {
        await using var wmsDatabase = CreateWmsDatabase();
        await using var ruoyiDatabase = CreateRuoyiDatabase();
        var row = CreateDispatchRow(1, "DB20260811003", 4);
        await wmsDatabase.Set<DispatchlistEntity>().AddAsync(row);
        await wmsDatabase.SaveChangesAsync();

        var service = new DispatchlistPickingService(wmsDatabase, ruoyiDatabase, new TestStringLocalizer());
        var (flag, msg) = await service.ReturnToWeighingAsync(row.id, AdminUser());

        Assert.True(flag);
        Assert.Equal("operation_success", msg);
        Assert.Equal((byte)4, row.dispatch_status);
    }

    [Fact]
    public async Task ReturnToWeighingAsync_rejects_a_user_without_weighing_authority()
    {
        await using var wmsDatabase = CreateWmsDatabase();
        await using var ruoyiDatabase = CreateRuoyiDatabase();
        var row = CreateDispatchRow(1, "DB20260811004", 5);
        await wmsDatabase.Set<DispatchlistEntity>().AddAsync(row);
        await wmsDatabase.SaveChangesAsync();

        var service = new DispatchlistPickingService(wmsDatabase, ruoyiDatabase, new TestStringLocalizer());
        var (flag, msg) = await service.ReturnToWeighingAsync(row.id, new CurrentUser
        {
            tenant_id = 1,
            user_role = "operator"
        });

        Assert.False(flag);
        Assert.Equal("没有称重操作权限", msg);
        Assert.Equal((byte)5, row.dispatch_status);
    }

    private static DispatchlistEntity CreateDispatchRow(int id, string dispatchNo, byte status) => new()
    {
        id = id,
        dispatch_no = dispatchNo,
        dispatch_status = status,
        tenant_id = 1,
        weighing_qty = 10,
        weighing_weight = 12.5m,
        weighing_length = 30,
        weighing_width = 20,
        weighing_height = 10,
        weighing_volume = 6000,
        weighing_person = "张三",
        volume_divisor = 5000,
        carrier_warehouse_id = 18,
        carrier_unit = "朝阳仓"
    };

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

    private static CurrentUser AdminUser() => new()
    {
        tenant_id = 1,
        user_role = "admin",
        user_name = "超管"
    };

    private sealed class TestStringLocalizer : IStringLocalizer<ModernWMS.Core.MultiLanguage>
    {
        public LocalizedString this[string name] => new(name, name);

        public LocalizedString this[string name, params object[] arguments] => new(name, string.Format(name, arguments));

        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => [];
    }
}
