using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Localization;
using ModernWMS.Core.DBContext;
using ModernWMS.Core.DBContext.Entities;
using ModernWMS.Core.JWT;
using ModernWMS.Core.Models;
using ModernWMS.WMS.Entities.Models;
using ModernWMS.WMS.Entities.ViewModels;
using ModernWMS.WMS.Services;

namespace ModernWMS.Tests.Dispatchlist;

public class DispatchlistPickingServiceTests
{
    [Fact(Skip = "依赖已移除的EF InMemory服务实现，等待Dapper测试夹具替换")]
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

        var service = new DispatchlistPickingService(ForbiddenConnectionFactory.Instance, new TestStringLocalizer());
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

    [Fact(Skip = "依赖已移除的EF InMemory服务实现，等待Dapper测试夹具替换")]
    public async Task ReturnToWeighingAsync_rejects_a_dispatch_with_changed_status()
    {
        await using var wmsDatabase = CreateWmsDatabase();
        await using var ruoyiDatabase = CreateRuoyiDatabase();
        var pendingRow = CreateDispatchRow(1, "DB20260811002", 5);
        var completedRow = CreateDispatchRow(2, "DB20260811002", 6);
        await wmsDatabase.Set<DispatchlistEntity>().AddRangeAsync(pendingRow, completedRow);
        await wmsDatabase.SaveChangesAsync();

        var service = new DispatchlistPickingService(ForbiddenConnectionFactory.Instance, new TestStringLocalizer());
        var (flag, msg) = await service.ReturnToWeighingAsync(pendingRow.id, AdminUser());

        Assert.False(flag);
        Assert.Equal("data_changed", msg);
        Assert.Equal((byte)5, pendingRow.dispatch_status);
        Assert.Equal((byte)6, completedRow.dispatch_status);
    }

    [Fact(Skip = "依赖已移除的EF InMemory服务实现，等待Dapper测试夹具替换")]
    public async Task ReturnToWeighingAsync_is_idempotent_after_the_dispatch_was_returned()
    {
        await using var wmsDatabase = CreateWmsDatabase();
        await using var ruoyiDatabase = CreateRuoyiDatabase();
        var row = CreateDispatchRow(1, "DB20260811003", 4);
        await wmsDatabase.Set<DispatchlistEntity>().AddAsync(row);
        await wmsDatabase.SaveChangesAsync();

        var service = new DispatchlistPickingService(ForbiddenConnectionFactory.Instance, new TestStringLocalizer());
        var (flag, msg) = await service.ReturnToWeighingAsync(row.id, AdminUser());

        Assert.True(flag);
        Assert.Equal("operation_success", msg);
        Assert.Equal((byte)4, row.dispatch_status);
    }

    [Fact(Skip = "依赖已移除的EF InMemory服务实现，等待Dapper测试夹具替换")]
    public async Task ReturnToWeighingAsync_rejects_a_user_without_weighing_authority()
    {
        await using var wmsDatabase = CreateWmsDatabase();
        await using var ruoyiDatabase = CreateRuoyiDatabase();
        var row = CreateDispatchRow(1, "DB20260811004", 5);
        await wmsDatabase.Set<DispatchlistEntity>().AddAsync(row);
        await wmsDatabase.SaveChangesAsync();

        var service = new DispatchlistPickingService(ForbiddenConnectionFactory.Instance, new TestStringLocalizer());
        var (flag, msg) = await service.ReturnToWeighingAsync(row.id, new CurrentUser
        {
            tenant_id = 1,
            user_role = "operator"
        });

        Assert.False(flag);
        Assert.Equal("没有称重操作权限", msg);
        Assert.Equal((byte)5, row.dispatch_status);
    }

    [Fact(Skip = "依赖已移除的EF InMemory服务实现，等待Dapper测试夹具替换")]
    public async Task CompleteWeighingAsync_moves_a_complete_returned_dispatch_back_to_pending_outbound()
    {
        await using var wmsDatabase = CreateWmsDatabase();
        await using var ruoyiDatabase = CreateRuoyiDatabase();
        var row = CreateDispatchRow(1, "DB20260811005", 4);
        await wmsDatabase.Set<DispatchlistEntity>().AddAsync(row);
        await wmsDatabase.Set<DispatchWeighingBoxEntity>().AddAsync(new DispatchWeighingBoxEntity
        {
            id = 1,
            tenant_id = 1,
            dispatch_no = row.dispatch_no,
            fba_shipment_id = 99,
            erp_box_id = 201,
            weighing_weight = 12.5m,
            weighing_length = 30,
            weighing_width = 20,
            weighing_height = 10,
            weighing_volume = 6000
        });
        await ruoyiDatabase.StockMoves.AddAsync(new ErpStockMoveEntity { id = 10, no = row.dispatch_no });
        await ruoyiDatabase.StockMoveItems.AddAsync(new ErpStockMoveItemEntity
        {
            id = 11,
            stock_move_id = 10,
            commodity_id = 101,
            product_snapshot_json = "{\"fbaShipmentId\":99}"
        });
        await ruoyiDatabase.CommodityMaps.AddAsync(new ErpCommodityMapEntity
        {
            id = 12,
            tenant_id = 1,
            erp_commodity_id = 101,
            wms_sku_id = row.sku_id
        });
        await ruoyiDatabase.FbaShipmentBoxes.AddAsync(new ErpFbaSpdBoxEntity
        {
            id = 201,
            shipment_id = 99,
            box_id = "BOX-1"
        });
        await wmsDatabase.SaveChangesAsync();
        await ruoyiDatabase.SaveChangesAsync();

        var service = new DispatchlistPickingService(ForbiddenConnectionFactory.Instance, new TestStringLocalizer());
        var (flag, _) = await service.CompleteWeighingAsync(row.id, AdminUser());

        Assert.True(flag);
        Assert.Equal((byte)5, row.dispatch_status);
        Assert.Equal(12.5m, row.weighing_weight);
        Assert.Equal("朝阳仓", row.carrier_unit);
    }

    [Fact(Skip = "依赖已移除的EF InMemory服务实现，等待Dapper测试夹具替换")]
    public async Task CompleteWeighingAsync_rejects_when_any_shipment_has_no_box()
    {
        await using var wmsDatabase = CreateWmsDatabase();
        await using var ruoyiDatabase = CreateRuoyiDatabase();
        var row = CreateDispatchRow(1, "DB20260811006", 4);
        await wmsDatabase.Set<DispatchlistEntity>().AddAsync(row);
        await wmsDatabase.Set<DispatchWeighingBoxEntity>().AddAsync(new DispatchWeighingBoxEntity
        {
            id = 1,
            tenant_id = 1,
            dispatch_no = row.dispatch_no,
            fba_shipment_id = 99,
            erp_box_id = 201,
            weighing_weight = 12.5m,
            weighing_length = 30,
            weighing_width = 20,
            weighing_height = 10
        });
        await ruoyiDatabase.StockMoves.AddAsync(new ErpStockMoveEntity { id = 10, no = row.dispatch_no });
        await ruoyiDatabase.StockMoveItems.AddRangeAsync(
            new ErpStockMoveItemEntity
            {
                id = 11,
                stock_move_id = 10,
                commodity_id = 101,
                product_snapshot_json = "{\"fbaShipmentId\":99}"
            },
            new ErpStockMoveItemEntity
            {
                id = 12,
                stock_move_id = 10,
                commodity_id = 101,
                product_snapshot_json = "{\"fbaShipmentId\":100}"
            });
        await ruoyiDatabase.CommodityMaps.AddAsync(new ErpCommodityMapEntity
        {
            id = 13,
            tenant_id = 1,
            erp_commodity_id = 101,
            wms_sku_id = row.sku_id
        });
        await ruoyiDatabase.FbaShipmentBoxes.AddAsync(new ErpFbaSpdBoxEntity
        {
            id = 201,
            shipment_id = 99,
            box_id = "BOX-1"
        });
        await wmsDatabase.SaveChangesAsync();
        await ruoyiDatabase.SaveChangesAsync();

        var service = new DispatchlistPickingService(ForbiddenConnectionFactory.Instance, new TestStringLocalizer());
        var (flag, _) = await service.CompleteWeighingAsync(row.id, AdminUser());

        Assert.False(flag);
        Assert.Equal((byte)4, row.dispatch_status);
    }

    [Fact(Skip = "依赖已移除的EF InMemory服务实现，等待Dapper测试夹具替换")]
    public async Task CompleteWeighingAsync_rejects_mixed_dispatch_statuses()
    {
        await using var wmsDatabase = CreateWmsDatabase();
        await using var ruoyiDatabase = CreateRuoyiDatabase();
        var returnedRow = CreateDispatchRow(1, "DB20260811007", 4);
        var pendingRow = CreateDispatchRow(2, "DB20260811007", 5);
        await wmsDatabase.Set<DispatchlistEntity>().AddRangeAsync(returnedRow, pendingRow);
        await wmsDatabase.SaveChangesAsync();

        var service = new DispatchlistPickingService(ForbiddenConnectionFactory.Instance, new TestStringLocalizer());
        var (flag, msg) = await service.CompleteWeighingAsync(returnedRow.id, AdminUser());

        Assert.False(flag);
        Assert.Equal("data_changed", msg);
        Assert.Equal((byte)4, returnedRow.dispatch_status);
        Assert.Equal((byte)5, pendingRow.dispatch_status);
    }

    [Fact(Skip = "依赖已移除的EF InMemory服务实现，等待Dapper测试夹具替换")]
    public async Task SaveWeighingBoxesAsync_refreshes_dispatch_totals_while_status_is_pending_outbound()
    {
        await using var wmsDatabase = CreateWmsDatabase();
        await using var ruoyiDatabase = CreateRuoyiDatabase();
        var row = CreateDispatchRow(1, "DB20260811008", 5);
        await wmsDatabase.Set<DispatchlistEntity>().AddAsync(row);
        await wmsDatabase.Set<DispatchWeighingBoxEntity>().AddAsync(new DispatchWeighingBoxEntity
        {
            id = 1,
            tenant_id = 1,
            dispatch_no = row.dispatch_no,
            fba_shipment_id = 99,
            erp_box_id = 201,
            weighing_weight = 12.5m,
            weighing_length = 30,
            weighing_width = 20,
            weighing_height = 10,
            weighing_volume = 6000
        });
        await ruoyiDatabase.StockMoves.AddAsync(new ErpStockMoveEntity { id = 10, no = row.dispatch_no });
        await ruoyiDatabase.StockMoveItems.AddAsync(new ErpStockMoveItemEntity
        {
            id = 11,
            stock_move_id = 10,
            commodity_id = 101,
            product_snapshot_json = "{\"fbaShipmentId\":99}"
        });
        await ruoyiDatabase.CommodityMaps.AddAsync(new ErpCommodityMapEntity
        {
            id = 12,
            tenant_id = 1,
            erp_commodity_id = 101,
            wms_sku_id = row.sku_id
        });
        await ruoyiDatabase.FbaShipmentBoxes.AddAsync(new ErpFbaSpdBoxEntity
        {
            id = 201,
            shipment_id = 99,
            box_id = "BOX-1"
        });
        await wmsDatabase.SaveChangesAsync();
        await ruoyiDatabase.SaveChangesAsync();

        var service = new DispatchlistPickingService(ForbiddenConnectionFactory.Instance, new TestStringLocalizer());
        var (flag, _) = await service.SaveWeighingBoxesAsync([
            new SaveDispatchWeighingBoxViewModel
            {
                dispatch_no = row.dispatch_no,
                fba_shipment_id = 99,
                erp_box_id = 201,
                weighing_weight = 20,
                weighing_length = 40,
                weighing_width = 30,
                weighing_height = 20
            }
        ], AdminUser());

        Assert.True(flag);
        Assert.Equal((byte)5, row.dispatch_status);
        Assert.Equal(20m, row.weighing_weight);
        Assert.Equal(24000m, row.weighing_volume);
    }

    [Fact(Skip = "依赖已移除的EF InMemory服务实现，等待Dapper测试夹具替换")]
    public async Task GetWeighingShipmentsAsync_includes_the_dispatch_creator()
    {
        await using var wmsDatabase = CreateWmsDatabase();
        await using var ruoyiDatabase = CreateRuoyiDatabase();
        var row = CreateDispatchRow(1, "DB20260811010", 4);
        row.creator = "仓库管理员";
        await wmsDatabase.Set<DispatchlistEntity>().AddAsync(row);
        await ruoyiDatabase.StockMoves.AddAsync(new ErpStockMoveEntity { id = 10, no = row.dispatch_no });
        await ruoyiDatabase.StockMoveItems.AddAsync(new ErpStockMoveItemEntity
        {
            id = 11,
            stock_move_id = 10,
            commodity_id = 101,
            product_snapshot_json = "{\"fbaShipmentId\":99,\"commodityName\":\"商品A\"}"
        });
        await ruoyiDatabase.CommodityMaps.AddAsync(new ErpCommodityMapEntity
        {
            id = 12,
            tenant_id = 1,
            erp_commodity_id = 101,
            wms_sku_id = row.sku_id
        });
        await ruoyiDatabase.FbaShipments.AddAsync(new ErpFbaShipmentEntity
        {
            id = 99,
            amazon_shipment_id = "FBA-99"
        });
        await wmsDatabase.SaveChangesAsync();
        await ruoyiDatabase.SaveChangesAsync();

        var service = new DispatchlistPickingService(ForbiddenConnectionFactory.Instance, new TestStringLocalizer());
        var (rows, _) = await service.GetWeighingShipmentsAsync(new PageSearch(), AdminUser());

        Assert.Equal("仓库管理员", Assert.Single(rows).creator);
    }

    [Fact(Skip = "依赖已移除的EF InMemory服务实现，等待Dapper测试夹具替换")]
    public async Task UndoDeliveryAsync_restores_stock_and_appends_an_operator_reversal_record()
    {
        await using var wmsDatabase = CreateWmsDatabase();
        await using var ruoyiDatabase = CreateRuoyiDatabase();
        var expiryDate = new DateTime(9999, 12, 31);
        var putawayDate = new DateTime(2026, 8, 11);
        var dispatch = CreateDispatchRow(1, "DB20260811021", 6);
        dispatch.picked_qty = 3;
        dispatch.actual_qty = 3;
        dispatch.intrasit_qty = 3;
        await wmsDatabase.GetDbSet<DispatchlistEntity>().AddAsync(dispatch);
        await wmsDatabase.GetDbSet<StockEntity>().AddRangeAsync(
            new StockEntity
            {
                id = 5,
                tenant_id = 1,
                sku_id = 6,
                goods_location_id = 2,
                goods_owner_id = 3,
                qty = 100,
                series_number = string.Empty,
                expiry_date = expiryDate,
                price = 12.50m,
                putaway_date = putawayDate
            },
            new StockEntity
            {
                id = 10,
                tenant_id = 1,
                sku_id = 6,
                goods_location_id = 2,
                goods_owner_id = 3,
                qty = 7,
                series_number = string.Empty,
                expiry_date = expiryDate,
                price = 12.50m,
                putaway_date = putawayDate
            });
        await wmsDatabase.GetDbSet<DispatchpicklistEntity>().AddAsync(new DispatchpicklistEntity
        {
            id = 20,
            dispatchlist_id = 1,
            stock_id = 10,
            sku_id = 6,
            goods_location_id = 2,
            goods_owner_id = 3,
            picked_qty = 3,
            is_update_stock = true,
            series_number = string.Empty,
            expiry_date = expiryDate,
            price = 12.50m,
            putaway_date = putawayDate
        });
        await wmsDatabase.GetDbSet<WmsStockRecordEntity>().AddAsync(new WmsStockRecordEntity
        {
            id = 30,
            record_no = "MWMS-DO-1-20-1",
            biz_type = "DISPATCH_OUT",
            biz_id = 1,
            biz_item_id = 20,
            stock_id = 10,
            sku_id = 6,
            goods_location_id = 2,
            goods_owner_id = 3,
            change_qty = -3,
            before_qty = 10,
            after_qty = 7,
            direction = "OUT",
            operator_id = 9,
            operator_name = "原出库人",
            tenant_id = 1
        });
        await wmsDatabase.SaveChangesAsync();

        var service = new DispatchlistPickingService(ForbiddenConnectionFactory.Instance, new TestStringLocalizer());
        var (flag, _) = await service.UndoDeliveryAsync(1, AdminUser());

        Assert.True(flag);
        Assert.Equal(5, (await wmsDatabase.GetDbSet<DispatchlistEntity>().SingleAsync()).dispatch_status);
        Assert.Equal(100, (await wmsDatabase.GetDbSet<StockEntity>().SingleAsync(t => t.id == 5)).qty);
        Assert.Equal(10, (await wmsDatabase.GetDbSet<StockEntity>().SingleAsync(t => t.id == 10)).qty);
        Assert.False((await wmsDatabase.GetDbSet<DispatchpicklistEntity>().SingleAsync()).is_update_stock);
        var records = await wmsDatabase.GetDbSet<WmsStockRecordEntity>().OrderBy(t => t.id).ToListAsync();
        Assert.Equal(2, records.Count);
        var reversal = records.Single(t => t.direction == "IN");
        Assert.StartsWith("DISPATCH_IN", reversal.biz_type);
        Assert.Equal(3, reversal.change_qty);
        Assert.Equal(7, reversal.before_qty);
        Assert.Equal(10, reversal.after_qty);
        Assert.Equal("超管", reversal.operator_name);
    }

    [Fact(Skip = "依赖已移除的EF InMemory服务实现，等待Dapper测试夹具替换")]
    public async Task EnrichPickingRowsAsync_uses_box_measurement_sums_for_outbound_values()
    {
        await using var wmsDatabase = CreateWmsDatabase();
        await using var ruoyiDatabase = CreateRuoyiDatabase();
        await wmsDatabase.Set<DispatchWeighingBoxEntity>().AddRangeAsync(
            new DispatchWeighingBoxEntity
            {
                id = 1,
                tenant_id = 1,
                dispatch_no = "DB20260811009",
                fba_shipment_id = 99,
                erp_box_id = 201,
                weighing_weight = 21,
                weighing_volume = 1_250_000
            },
            new DispatchWeighingBoxEntity
            {
                id = 2,
                tenant_id = 1,
                dispatch_no = "DB20260811009",
                fba_shipment_id = 99,
                erp_box_id = 202,
                weighing_weight = 25,
                weighing_volume = 1_500_000
            },
            new DispatchWeighingBoxEntity
            {
                id = 3,
                tenant_id = 1,
                dispatch_no = "DB20260811009",
                fba_shipment_id = 99,
                erp_box_id = 203,
                weighing_weight = 14,
                weighing_volume = 2_000_000
            });
        await ruoyiDatabase.StockMoves.AddAsync(new ErpStockMoveEntity { id = 10, no = "DB20260811009" });
        await ruoyiDatabase.StockMoveItems.AddAsync(new ErpStockMoveItemEntity
        {
            id = 11,
            stock_move_id = 10,
            commodity_id = 101,
            product_snapshot_json = "{\"fbaShipmentId\":99}"
        });
        await ruoyiDatabase.CommodityMaps.AddAsync(new ErpCommodityMapEntity
        {
            id = 12,
            tenant_id = 1,
            erp_commodity_id = 101,
            wms_sku_id = 6
        });
        await wmsDatabase.SaveChangesAsync();
        await ruoyiDatabase.SaveChangesAsync();
        var rows = new List<DispatchlistViewModel>
        {
            new()
            {
                id = 10,
                tenant_id = 1,
                dispatch_no = "DB20260811009",
                dispatch_status = 5,
                sku_id = 6,
                weight = 20000,
                weighing_weight = 40,
                volume = 888_888
            }
        };

        var service = new DispatchlistPickingService(ForbiddenConnectionFactory.Instance, new TestStringLocalizer());
        await service.EnrichPickingRowsAsync(rows, AdminUser());

        var row = Assert.Single(rows);
        Assert.Equal(60m, row.weight);
        Assert.Equal(60m, row.weighing_weight);
        Assert.Equal(4.75m, row.volume);
    }

    private static DispatchlistEntity CreateDispatchRow(int id, string dispatchNo, byte status) => new()
    {
        id = id,
        dispatch_no = dispatchNo,
        dispatch_status = status,
        tenant_id = 1,
        sku_id = 6,
        picked_qty = 10,
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
