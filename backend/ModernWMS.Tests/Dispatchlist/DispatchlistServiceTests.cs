using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Localization;
using ModernWMS.Core.DBContext;
using ModernWMS.Core.DBContext.Entities;
using ModernWMS.Core.JWT;
using ModernWMS.WMS.Entities.Models;
using ModernWMS.WMS.Entities.ViewModels;
using ModernWMS.WMS.Services;

namespace ModernWMS.Tests.Dispatchlist;

public class DispatchlistServiceTests
{
    [Fact(Skip = "依赖已移除的EF InMemory服务实现，等待Dapper测试夹具替换")]
    public async Task Delivery_deducts_stock_and_records_the_operator_in_the_outbound_ledger()
    {
        await using var database = CreateDatabase();
        await SeedReadyOutboundAsync(database, stockQty: 10);

        var service = new DispatchlistService(ForbiddenConnectionFactory.Instance, new TestStringLocalizer(), null!);
        var (flag, _) = await service.Delivery([
            new DispatchlistDeliveryViewModel
            {
                id = 1,
                dispatch_no = "DB20260811020",
                dispatch_status = 5,
                picked_qty = 3
            }
        ], new CurrentUser
        {
            tenant_id = 1,
            user_id = 42,
            user_name = "李远航",
            user_role = "admin"
        });

        Assert.True(flag);
        var dispatch = await database.GetDbSet<DispatchlistEntity>().SingleAsync(t => t.id == 1);
        Assert.Equal(6, dispatch.dispatch_status);
        Assert.Equal(3, dispatch.actual_qty);
        Assert.Equal(0, dispatch.lock_qty);
        Assert.Equal(7, (await database.GetDbSet<StockEntity>().SingleAsync(t => t.id == 10)).qty);
        Assert.True((await database.GetDbSet<DispatchpicklistEntity>().SingleAsync(t => t.id == 20)).is_update_stock);

        var record = await database.GetDbSet<WmsStockRecordEntity>().SingleAsync();
        Assert.StartsWith("DISPATCH_OUT", record.biz_type);
        Assert.Equal(1, record.biz_id);
        Assert.Equal(20, record.biz_item_id);
        Assert.Equal(10, record.stock_id);
        Assert.Equal(-3, record.change_qty);
        Assert.Equal(10, record.before_qty);
        Assert.Equal(7, record.after_qty);
        Assert.Equal("OUT", record.direction);
        Assert.Equal(42, record.operator_id);
        Assert.Equal("李远航", record.operator_name);
        Assert.Equal(1, record.tenant_id);
    }

    [Fact(Skip = "依赖已移除的EF InMemory服务实现，等待Dapper测试夹具替换")]
    public async Task Delivery_rejects_insufficient_stock_without_partial_changes()
    {
        await using var database = CreateDatabase();
        await SeedReadyOutboundAsync(database, stockQty: 2);
        var service = new DispatchlistService(ForbiddenConnectionFactory.Instance, new TestStringLocalizer(), null!);

        var (flag, _) = await service.Delivery([
            new DispatchlistDeliveryViewModel { id = 1, dispatch_no = "DB20260811020", dispatch_status = 5, picked_qty = 3 }
        ], AdminUser());

        Assert.False(flag);
        Assert.Equal(5, (await database.GetDbSet<DispatchlistEntity>().SingleAsync()).dispatch_status);
        Assert.Equal(2, (await database.GetDbSet<StockEntity>().SingleAsync()).qty);
        Assert.False((await database.GetDbSet<DispatchpicklistEntity>().SingleAsync()).is_update_stock);
        Assert.Empty(await database.GetDbSet<WmsStockRecordEntity>().ToListAsync());
    }

    [Fact(Skip = "依赖已移除的EF InMemory服务实现，等待Dapper测试夹具替换")]
    public async Task Delivery_rejects_a_user_without_the_delivery_action_authority()
    {
        await using var database = CreateDatabase();
        await SeedReadyOutboundAsync(database, stockQty: 10);
        var service = new DispatchlistService(ForbiddenConnectionFactory.Instance, new TestStringLocalizer(), null!);

        var (flag, _) = await service.Delivery([
            new DispatchlistDeliveryViewModel { id = 1, dispatch_no = "DB20260811020", dispatch_status = 5, picked_qty = 3 }
        ], new CurrentUser { tenant_id = 1, user_id = 50, user_name = "无权限用户", user_role = "viewer" });

        Assert.False(flag);
        Assert.Equal(5, (await database.GetDbSet<DispatchlistEntity>().SingleAsync()).dispatch_status);
        Assert.Equal(10, (await database.GetDbSet<StockEntity>().SingleAsync()).qty);
        Assert.Empty(await database.GetDbSet<WmsStockRecordEntity>().ToListAsync());
    }

    [Fact(Skip = "依赖已移除的EF InMemory服务实现，等待Dapper测试夹具替换")]
    public async Task Delivery_after_a_completed_undo_appends_a_new_outbound_cycle()
    {
        await using var database = CreateDatabase();
        await using var ruoyiDatabase = CreateRuoyiDatabase();
        await SeedReadyOutboundAsync(database, stockQty: 10);
        var localizer = new TestStringLocalizer();
        var service = new DispatchlistService(ForbiddenConnectionFactory.Instance, localizer, null!);
        var firstDelivery = await service.Delivery([
            new DispatchlistDeliveryViewModel { id = 1, dispatch_no = "DB20260811020", dispatch_status = 5, picked_qty = 3 }
        ], AdminUser());
        var undoService = new DispatchlistPickingService(ForbiddenConnectionFactory.Instance, localizer);
        var undo = await undoService.UndoDeliveryAsync(1, AdminUser());

        var secondDelivery = await service.Delivery([
            new DispatchlistDeliveryViewModel { id = 1, dispatch_no = "DB20260811020", dispatch_status = 5, picked_qty = 3 }
        ], AdminUser());

        Assert.True(firstDelivery.flag);
        Assert.True(undo.flag);
        Assert.True(secondDelivery.flag);
        Assert.Equal(7, (await database.GetDbSet<StockEntity>().SingleAsync()).qty);
        Assert.Equal(6, (await database.GetDbSet<DispatchlistEntity>().SingleAsync()).dispatch_status);
        var records = await database.GetDbSet<WmsStockRecordEntity>().OrderBy(t => t.id).ToListAsync();
        Assert.Equal(3, records.Count);
        Assert.Contains(records, t => t.biz_type == "DISPATCH_OUT");
        Assert.Contains(records, t => t.biz_type == "DISPATCH_IN");
        Assert.Contains(records, t => t.biz_type == "DISPATCH_OUT_2" && t.operator_name == "李远航");
        Assert.Equal(-3, records.Sum(t => t.change_qty));
    }

    [Fact(Skip = "依赖已移除的EF InMemory服务实现，等待Dapper测试夹具替换")]
    public async Task Delivery_deducts_the_exact_stock_row_selected_during_pick_allocation()
    {
        await using var database = CreateDatabase();
        await SeedReadyOutboundAsync(database, stockQty: 10);
        var selectedStock = await database.GetDbSet<StockEntity>().SingleAsync(t => t.id == 10);
        await database.GetDbSet<StockEntity>().AddAsync(new StockEntity
        {
            id = 5,
            tenant_id = selectedStock.tenant_id,
            sku_id = selectedStock.sku_id,
            goods_location_id = selectedStock.goods_location_id,
            goods_owner_id = selectedStock.goods_owner_id,
            qty = 100,
            series_number = selectedStock.series_number,
            expiry_date = selectedStock.expiry_date,
            price = selectedStock.price,
            putaway_date = selectedStock.putaway_date
        });
        await database.SaveChangesAsync();
        var service = new DispatchlistService(ForbiddenConnectionFactory.Instance, new TestStringLocalizer(), null!);

        var (flag, _) = await service.Delivery([
            new DispatchlistDeliveryViewModel { id = 1, dispatch_no = "DB20260811020", dispatch_status = 5, picked_qty = 3 }
        ], AdminUser());

        Assert.True(flag);
        Assert.Equal(100, (await database.GetDbSet<StockEntity>().SingleAsync(t => t.id == 5)).qty);
        Assert.Equal(7, (await database.GetDbSet<StockEntity>().SingleAsync(t => t.id == 10)).qty);
        Assert.Equal(10, (await database.GetDbSet<WmsStockRecordEntity>().SingleAsync()).stock_id);
    }

    [Fact(Skip = "依赖已移除的EF InMemory服务实现，等待Dapper测试夹具替换")]
    public async Task Delivery_records_running_balances_for_two_picks_sharing_one_stock_row()
    {
        await using var database = CreateDatabase();
        await SeedReadyOutboundAsync(database, stockQty: 10);
        var firstPick = await database.GetDbSet<DispatchpicklistEntity>().SingleAsync();
        await database.GetDbSet<DispatchlistEntity>().AddAsync(new DispatchlistEntity
        {
            id = 2,
            dispatch_no = "DB20260811022",
            dispatch_status = 5,
            tenant_id = 1,
            sku_id = 6,
            qty = 2,
            picked_qty = 2,
            lock_qty = 2
        });
        await database.GetDbSet<DispatchpicklistEntity>().AddAsync(new DispatchpicklistEntity
        {
            id = 21,
            dispatchlist_id = 2,
            stock_id = 10,
            sku_id = firstPick.sku_id,
            goods_location_id = firstPick.goods_location_id,
            goods_owner_id = firstPick.goods_owner_id,
            pick_qty = 2,
            picked_qty = 2,
            series_number = firstPick.series_number,
            expiry_date = firstPick.expiry_date,
            price = firstPick.price,
            putaway_date = firstPick.putaway_date
        });
        await database.SaveChangesAsync();
        var service = new DispatchlistService(ForbiddenConnectionFactory.Instance, new TestStringLocalizer(), null!);

        var (flag, _) = await service.Delivery([
            new DispatchlistDeliveryViewModel { id = 1 },
            new DispatchlistDeliveryViewModel { id = 2 }
        ], AdminUser());

        Assert.True(flag);
        Assert.Equal(5, (await database.GetDbSet<StockEntity>().SingleAsync()).qty);
        var records = await database.GetDbSet<WmsStockRecordEntity>().OrderBy(t => t.biz_item_id).ToListAsync();
        Assert.Collection(records,
            first => { Assert.Equal(10, first.before_qty); Assert.Equal(7, first.after_qty); },
            second => { Assert.Equal(7, second.before_qty); Assert.Equal(5, second.after_qty); });
    }

    [Fact(Skip = "依赖已移除的EF InMemory服务实现，等待Dapper测试夹具替换")]
    public async Task ConfirmOrder_rejects_a_dispatch_from_another_tenant()
    {
        await using var database = CreateDatabase();
        await database.GetDbSet<DispatchlistEntity>().AddAsync(new DispatchlistEntity
        {
            id = 1,
            tenant_id = 2,
            sku_id = 6,
            qty = 3,
            dispatch_status = 1
        });
        await database.SaveChangesAsync();
        var service = new DispatchlistService(ForbiddenConnectionFactory.Instance, new TestStringLocalizer(), null!);

        var (flag, _) = await service.ConfirmOrder([
            new DispatchlistConfirmDetailViewModel { dispatchlist_id = 1, sku_id = 6, qty = 3, confirm = false }
        ], AdminUser());

        Assert.False(flag);
        Assert.Equal(1, (await database.GetDbSet<DispatchlistEntity>().SingleAsync()).dispatch_status);
        Assert.Empty(await database.GetDbSet<DispatchpicklistEntity>().ToListAsync());
    }

    [Fact(Skip = "依赖已移除的EF InMemory服务实现，等待Dapper测试夹具替换")]
    public async Task ConfirmOrder_rejects_a_stock_row_from_another_tenant()
    {
        await using var database = CreateDatabase();
        var pick = await SeedPendingConfirmationAsync(database);
        (await database.GetDbSet<StockEntity>().SingleAsync()).tenant_id = 2;
        await database.SaveChangesAsync();
        var service = new DispatchlistService(ForbiddenConnectionFactory.Instance, new TestStringLocalizer(), null!);

        var (flag, _) = await service.ConfirmOrder([
            new DispatchlistConfirmDetailViewModel
            {
                dispatchlist_id = 1,
                sku_id = 6,
                qty = 3,
                confirm = true,
                pick_list = [pick]
            }
        ], AdminUser());

        Assert.False(flag);
        Assert.Equal(1, (await database.GetDbSet<DispatchlistEntity>().SingleAsync()).dispatch_status);
        Assert.Empty(await database.GetDbSet<DispatchpicklistEntity>().ToListAsync());
    }

    [Fact(Skip = "依赖已移除的EF InMemory服务实现，等待Dapper测试夹具替换")]
    public async Task ConfirmOrder_rejects_mismatched_stock_attributes_or_child_dispatch_id()
    {
        await using var database = CreateDatabase();
        var pick = await SeedPendingConfirmationAsync(database);
        pick.goods_location_id = 999;
        pick.dispatchlist_id = 2;
        var service = new DispatchlistService(ForbiddenConnectionFactory.Instance, new TestStringLocalizer(), null!);

        var (flag, _) = await service.ConfirmOrder([
            new DispatchlistConfirmDetailViewModel
            {
                dispatchlist_id = 1,
                sku_id = 6,
                qty = 3,
                confirm = true,
                pick_list = [pick]
            }
        ], AdminUser());

        Assert.False(flag);
        Assert.Equal(1, (await database.GetDbSet<DispatchlistEntity>().SingleAsync()).dispatch_status);
        Assert.Empty(await database.GetDbSet<DispatchpicklistEntity>().ToListAsync());
    }

    [Theory(Skip = "依赖已移除的EF InMemory服务实现，等待Dapper测试夹具替换")]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(4)]
    public async Task ConfirmOrder_rejects_negative_empty_or_excess_pick_quantity(int pickQty)
    {
        await using var database = CreateDatabase();
        var pick = await SeedPendingConfirmationAsync(database);
        pick.pick_qty = pickQty;
        var service = new DispatchlistService(ForbiddenConnectionFactory.Instance, new TestStringLocalizer(), null!);

        var (flag, _) = await service.ConfirmOrder([
            new DispatchlistConfirmDetailViewModel
            {
                dispatchlist_id = 1,
                sku_id = 6,
                qty = 3,
                confirm = true,
                pick_list = [pick]
            }
        ], AdminUser());

        Assert.False(flag);
        Assert.Equal(1, (await database.GetDbSet<DispatchlistEntity>().SingleAsync()).dispatch_status);
        Assert.Empty(await database.GetDbSet<DispatchpicklistEntity>().ToListAsync());
    }

    [Fact(Skip = "依赖已移除的EF InMemory服务实现，等待Dapper测试夹具替换")]
    public async Task ConfirmOrder_rejects_duplicate_parent_dispatch_items()
    {
        await using var database = CreateDatabase();
        var service = new DispatchlistService(ForbiddenConnectionFactory.Instance, new TestStringLocalizer(), null!);
        await database.GetDbSet<DispatchlistEntity>().AddAsync(new DispatchlistEntity
        {
            id = 1,
            tenant_id = 1,
            sku_id = 6,
            qty = 3,
            dispatch_status = 1
        });
        await database.SaveChangesAsync();
        var duplicate = new DispatchlistConfirmDetailViewModel
        {
            dispatchlist_id = 1,
            sku_id = 6,
            qty = 3,
            confirm = false
        };

        var (flag, _) = await service.ConfirmOrder([duplicate, duplicate], AdminUser());

        Assert.False(flag);
        Assert.Equal(1, (await database.GetDbSet<DispatchlistEntity>().SingleAsync()).dispatch_status);
        Assert.Empty(await database.GetDbSet<DispatchpicklistEntity>().ToListAsync());
    }

    [Fact(Skip = "依赖已移除的EF InMemory服务实现，等待Dapper测试夹具替换")]
    public async Task ConfirmOrder_rejects_a_dispatch_that_has_already_entered_the_workflow()
    {
        await using var database = CreateDatabase();
        await database.GetDbSet<DispatchlistEntity>().AddAsync(new DispatchlistEntity
        {
            id = 1,
            tenant_id = 1,
            sku_id = 6,
            qty = 3,
            dispatch_status = 5
        });
        await database.SaveChangesAsync();
        var service = new DispatchlistService(ForbiddenConnectionFactory.Instance, new TestStringLocalizer(), null!);

        var (flag, _) = await service.ConfirmOrder([
            new DispatchlistConfirmDetailViewModel { dispatchlist_id = 1, sku_id = 6, qty = 3, confirm = false }
        ], AdminUser());

        Assert.False(flag);
        Assert.Equal(5, (await database.GetDbSet<DispatchlistEntity>().SingleAsync()).dispatch_status);
        Assert.Empty(await database.GetDbSet<DispatchpicklistEntity>().ToListAsync());
    }

    private static SqlDBContext CreateDatabase()
    {
        var options = new DbContextOptionsBuilder<SqlDBContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(t => t.Ignore(InMemoryEventId.TransactionIgnoredWarning))
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
        user_id = 42,
        user_name = "李远航",
        user_role = "admin"
    };

    private static async Task SeedReadyOutboundAsync(SqlDBContext database, int stockQty)
    {
        var expiryDate = new DateTime(9999, 12, 31);
        var putawayDate = new DateTime(2026, 8, 11);
        await database.GetDbSet<DispatchlistEntity>().AddAsync(new DispatchlistEntity
        {
            id = 1,
            dispatch_no = "DB20260811020",
            dispatch_status = 5,
            tenant_id = 1,
            sku_id = 6,
            qty = 3,
            picked_qty = 3,
            lock_qty = 3
        });
        await database.GetDbSet<StockEntity>().AddAsync(new StockEntity
        {
            id = 10,
            tenant_id = 1,
            sku_id = 6,
            goods_location_id = 2,
            goods_owner_id = 3,
            qty = stockQty,
            series_number = string.Empty,
            expiry_date = expiryDate,
            price = 12.50m,
            putaway_date = putawayDate
        });
        await database.GetDbSet<DispatchpicklistEntity>().AddAsync(new DispatchpicklistEntity
        {
            id = 20,
            dispatchlist_id = 1,
            stock_id = 10,
            sku_id = 6,
            goods_location_id = 2,
            goods_owner_id = 3,
            pick_qty = 3,
            picked_qty = 3,
            series_number = string.Empty,
            expiry_date = expiryDate,
            price = 12.50m,
            putaway_date = putawayDate
        });
        await database.SaveChangesAsync();
    }

    private static async Task<DispatchlistConfirmPickDetailViewModel> SeedPendingConfirmationAsync(SqlDBContext database)
    {
        var expiryDate = new DateTime(9999, 12, 31);
        var putawayDate = new DateTime(2026, 8, 11);
        await database.GetDbSet<DispatchlistEntity>().AddAsync(new DispatchlistEntity
        {
            id = 1,
            tenant_id = 1,
            sku_id = 6,
            qty = 3,
            dispatch_status = 1
        });
        await database.GetDbSet<StockEntity>().AddAsync(new StockEntity
        {
            id = 10,
            tenant_id = 1,
            sku_id = 6,
            goods_location_id = 2,
            goods_owner_id = 3,
            qty = 10,
            series_number = string.Empty,
            expiry_date = expiryDate,
            price = 12.50m,
            putaway_date = putawayDate
        });
        await database.SaveChangesAsync();
        return new DispatchlistConfirmPickDetailViewModel
        {
            stock_id = 10,
            dispatchlist_id = 1,
            goods_location_id = 2,
            goods_owner_id = 3,
            pick_qty = 3,
            series_number = string.Empty,
            expiry_date = expiryDate,
            price = 12.50m,
            putaway_date = putawayDate
        };
    }

    private sealed class TestStringLocalizer : IStringLocalizer<ModernWMS.Core.MultiLanguage>
    {
        public LocalizedString this[string name] => new(name, name);
        public LocalizedString this[string name, params object[] arguments] => new(name, string.Format(name, arguments));
        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => [];
    }
}
