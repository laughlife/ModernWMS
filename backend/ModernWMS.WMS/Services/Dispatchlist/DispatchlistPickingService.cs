using System.Text.Json;
using System.Data;
using Dapper;
using Microsoft.Extensions.Localization;
using ModernWMS.Core.Database;
using ModernWMS.Core.DBContext.Entities;
using ModernWMS.Core.JWT;
using ModernWMS.Core.Models;
using ModernWMS.WMS.Entities.Models;
using ModernWMS.WMS.Entities.ViewModels;
using ModernWMS.WMS.IServices;
using MySqlConnector;

namespace ModernWMS.WMS.Services;

/// <summary>
/// Picking workflow that operates on individual dispatch rows.
/// </summary>
public class DispatchlistPickingService : IDispatchlistPickingService
{
    private static readonly int[] AllowedVolumeDivisors = [5000, 6000, 7000, 8000];
    private const string OutboundSettingAuthority = "delivered-setCarrier";
    private const string OutboundDeliveryAuthority = "delivered-delivery";
    private const string WeighingAuthority = "weighed-weigh";
    private static readonly string[] ExcludedCarrierWarehouseNames =
    [
        "有座山深圳仓",
        "南阳有座山公司-样品专用",
        "海外-领星OMS校准仓",
        "包材",
        "南阳"
    ];

    private readonly IMySqlConnectionFactory _connectionFactory;
    private readonly IStringLocalizer<ModernWMS.Core.MultiLanguage> _stringLocalizer;

    public DispatchlistPickingService(
        IMySqlConnectionFactory connectionFactory,
        IStringLocalizer<ModernWMS.Core.MultiLanguage> stringLocalizer)
    {
        _connectionFactory = connectionFactory;
        _stringLocalizer = stringLocalizer;
    }

    public async Task EnrichPickingRowsAsync(List<DispatchlistViewModel> rows, CurrentUser currentUser)
    {
        if (rows.Count == 0)
        {
            return;
        }

        var dispatchNos = rows.Select(t => t.dispatch_no).Distinct().ToList();
        await using var connection = await _connectionFactory.OpenConnectionAsync();
        var moves = (await connection.QueryAsync<ErpStockMoveEntity>(
            "SELECT * FROM `trk_stock_move` WHERE `deleted`=0 AND `no` IN @dispatchNos;", new { dispatchNos })).AsList();
        if (moves.Count == 0)
        {
            return;
        }

        var moveIds = moves.Select(t => t.id).ToList();
        var items = (await connection.QueryAsync<ErpStockMoveItemEntity>(
            "SELECT * FROM `trk_stock_move_item` WHERE `deleted`=0 AND `stock_move_id` IN @moveIds;", new { moveIds })).AsList();
        var commodityIds = items.Where(t => t.commodity_id.HasValue)
            .Select(t => t.commodity_id!.Value)
            .Distinct()
            .ToList();
        var skuMap = (await connection.QueryAsync<ErpCommodityMapEntity>(
            "SELECT * FROM `wms_erp_commodity_map` WHERE `tenant_id`=@tenant_id AND `erp_commodity_id` IN @commodityIds;",
            new { currentUser.tenant_id, commodityIds })).ToDictionary(t => t.erp_commodity_id, t => t.wms_sku_id);
        var movesByNo = moves.GroupBy(t => t.no).ToDictionary(t => t.Key, t => t.ToList());
        var itemsByMove = items.GroupBy(t => t.stock_move_id).ToDictionary(t => t.Key, t => t.ToList());
        var snapshotByItem = items.ToDictionary(t => t.id, ParseSnapshot);

        var snapshotItemIds = snapshotByItem.Values.Select(t => t.fbaShipmentItemId)
            .Where(t => t.HasValue)
            .Select(t => t.GetValueOrDefault())
            .Distinct()
            .ToList();
        var shipmentItemMap = snapshotItemIds.Count > 0
            ? (await connection.QueryAsync<ErpFbaShipmentItemEntity>(
                "SELECT * FROM `erp_fba_shipment_item` WHERE `deleted`=0 AND `id` IN @snapshotItemIds;", new { snapshotItemIds })).ToDictionary(t => t.id)
            : new Dictionary<long, ModernWMS.Core.DBContext.Entities.ErpFbaShipmentItemEntity>();
        var shipmentIds = snapshotByItem.Values.Select(t => t.fbaShipmentId)
            .Where(t => t.HasValue)
            .Select(t => t.GetValueOrDefault())
            .Concat(shipmentItemMap.Values.Select(t => t.shipment_id))
            .Distinct()
            .ToList();
        var shipmentMap = shipmentIds.Count > 0
            ? (await connection.QueryAsync<ErpFbaShipmentEntity>(
                "SELECT * FROM `erp_fba_shipment` WHERE `deleted`=0 AND `id` IN @shipmentIds;", new { shipmentIds })).ToDictionary(t => t.id)
            : new Dictionary<long, ModernWMS.Core.DBContext.Entities.ErpFbaShipmentEntity>();
        var boxCounts = shipmentIds.Count > 0
            ? (await connection.QueryAsync<ErpFbaSpdBoxEntity>(
                "SELECT * FROM `erp_fba_spd_box` WHERE `deleted`=0 AND `shipment_id` IN @shipmentIds;", new { shipmentIds }))
                .GroupBy(t => t.shipment_id).ToDictionary(t => t.Key, t => t.Count())
            : new Dictionary<long, int>();
        var measuredBoxRows = (await connection.QueryAsync<MeasuredBoxTotal>("""
            SELECT `dispatch_no`,`fba_shipment_id`,SUM(`weighing_weight`) `weight`,SUM(`weighing_volume`) `volume`
            FROM `wms_dispatch_weighing_box` WHERE `tenant_id`=@tenant_id AND `dispatch_no` IN @dispatchNos
              AND `fba_shipment_id` IN @shipmentIds GROUP BY `dispatch_no`,`fba_shipment_id`;
            """, new { currentUser.tenant_id, dispatchNos, shipmentIds })).AsList();
        var measuredBoxes = measuredBoxRows.ToDictionary(t => (t.dispatch_no, t.fba_shipment_id));

        foreach (var row in rows)
        {
            if (!movesByNo.TryGetValue(row.dispatch_no, out var candidateMoves))
            {
                continue;
            }

            var matchingMoves = candidateMoves
                .Where(move => (itemsByMove.GetValueOrDefault(move.id) ?? [])
                    .Any(item => item.commodity_id.HasValue
                        && skuMap.TryGetValue(item.commodity_id.Value, out var wmsSkuId)
                        && wmsSkuId == row.sku_id))
                .ToList();
            if (matchingMoves.Count != 1)
            {
                continue;
            }
            var move = matchingMoves[0];

            row.dept_name = move.dept_name ?? string.Empty;
            row.order_user_name = move.order_user_name ?? string.Empty;
            row.prepared_time = move.create_time;

            var sourceItems = (itemsByMove.GetValueOrDefault(move.id) ?? [])
                .Where(t => t.commodity_id.HasValue
                    && skuMap.TryGetValue(t.commodity_id.Value, out var wmsSkuId)
                    && wmsSkuId == row.sku_id)
                .ToList();
            var snapshots = sourceItems.Select(t => snapshotByItem[t.id]).ToList();
            var shipmentItem = snapshots.Select(t => t.fbaShipmentItemId)
                .Where(t => t.HasValue && shipmentItemMap.ContainsKey(t.GetValueOrDefault()))
                .Select(t => shipmentItemMap[t.GetValueOrDefault()])
                .FirstOrDefault();
            row.main_image = snapshots.Select(t => t.mainImage).FirstOrDefault(t => !string.IsNullOrWhiteSpace(t))
                ?? shipmentItem?.main_image
                ?? string.Empty;
            row.commodity_name = snapshots.Select(t => t.commodityName).FirstOrDefault(t => !string.IsNullOrWhiteSpace(t))
                ?? shipmentItem?.commodity_name
                ?? sourceItems.Select(t => t.commodity_name).FirstOrDefault(t => !string.IsNullOrWhiteSpace(t))
                ?? row.spu_name;
            row.fba_sku = !string.IsNullOrWhiteSpace(shipmentItem?.fn_sku)
                ? shipmentItem!.fn_sku!
                : string.Join(", ", snapshots.Select(t => t.fbaSku)
                    .Where(t => !string.IsNullOrWhiteSpace(t))
                    .Distinct(StringComparer.OrdinalIgnoreCase));
            var shipmentId = shipmentItem?.shipment_id
                ?? snapshots.Select(t => t.fbaShipmentId).FirstOrDefault(t => t.HasValue);
            row.fba_shipment_id = shipmentId ?? 0;
            row.shop_name = shipmentId.HasValue && shipmentMap.TryGetValue(shipmentId.Value, out var shipment)
                ? shipment.shop_name ?? string.Empty
                : string.Empty;
            row.box_count = shipmentId.HasValue && boxCounts.TryGetValue(shipmentId.Value, out var boxCount)
                ? boxCount
                : 0;
            if (shipmentId.HasValue
                && measuredBoxes.TryGetValue((row.dispatch_no, shipmentId.Value), out var measuredBox))
            {
                if (measuredBox.weight > 0)
                {
                    row.weight = measuredBox.weight;
                    row.weighing_weight = measuredBox.weight;
                }
                if (measuredBox.volume > 0)
                {
                    row.volume = measuredBox.volume / 1_000_000m;
                }
            }
            row.variant_qty = snapshots.Count > 0
                ? snapshots.Sum(t => t.variantQty ?? 1)
                : 1;
            var preparedTime = snapshots.Where(t => t.preparedTime.HasValue)
                .Select(t => t.preparedTime!.Value)
                .DefaultIfEmpty()
                .Min();
            row.prepared_time = shipmentItem?.create_time
                ?? (preparedTime != default ? preparedTime : row.prepared_time);
        }
    }

    public async Task<(bool flag, string msg)> CompletePickingAsync(List<int> ids, CurrentUser currentUser)
    {
        var distinctIds = ids.Where(t => t > 0).Distinct().ToList();
        if (distinctIds.Count == 0)
        {
            return (false, "请选择需要完成拣货的数据");
        }

        await using var connection = await _connectionFactory.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        var lockedIds = (await connection.QueryAsync<int>("SELECT `id` FROM `wms_dispatchlist` WHERE `id` IN @distinctIds AND `tenant_id`=@tenant_id AND `dispatch_status`=2 FOR UPDATE;", new { distinctIds, currentUser.tenant_id }, transaction)).AsList();
        if (lockedIds.Count != distinctIds.Count)
        {
            await transaction.RollbackAsync();
            return (false, _stringLocalizer["data_changed"]);
        }
        var now = DateTime.Now;
        var changed = await connection.ExecuteAsync("""
            UPDATE `wms_dispatchlist` SET `picked_qty`=`lock_qty`,`dispatch_status`=3,`pick_checker`=@user_name,
                `pick_checker_id`=@user_id,`last_update_time`=@now
            WHERE `id` IN @distinctIds AND `tenant_id`=@tenant_id AND `dispatch_status`=2;
            UPDATE `wms_dispatchpicklist` SET `picked_qty`=`pick_qty`,`last_update_time`=@now WHERE `dispatchlist_id` IN @distinctIds;
            """, new { distinctIds, currentUser.tenant_id, currentUser.user_name, currentUser.user_id, now }, transaction);
        await transaction.CommitAsync();
        return changed > 0 ? (true, _stringLocalizer["operation_success"]) : (false, _stringLocalizer["operation_failed"]);
    }

    public async Task<(bool flag, string msg)> RepickAsync(int id, CurrentUser currentUser)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        var now = DateTime.Now;
        var changed = await connection.ExecuteAsync("""
            UPDATE `wms_dispatchlist` SET `dispatch_status`=2,`picked_qty`=0,`pick_checker`='',`pick_checker_id`=0,`last_update_time`=@now
            WHERE `id`=@id AND `tenant_id`=@tenant_id AND `dispatch_status`=3;
            """, new { id, currentUser.tenant_id, now }, transaction);
        if (changed == 0)
        {
            await transaction.RollbackAsync();
            return (false, _stringLocalizer["data_changed"]);
        }
        await connection.ExecuteAsync("UPDATE `wms_dispatchpicklist` SET `picked_qty`=0,`last_update_time`=@now WHERE `dispatchlist_id`=@id;", new { id, now }, transaction);
        await transaction.CommitAsync();
        return (true, _stringLocalizer["operation_success"]);
    }

    public async Task<(bool flag, string msg)> StartWeighingAsync(int id, CurrentUser currentUser)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync();
        var entity = await connection.QuerySingleOrDefaultAsync<DispatchlistEntity>("SELECT * FROM `wms_dispatchlist` WHERE `id`=@id AND `tenant_id`=@tenant_id AND `dispatch_status`=3;", new { id, currentUser.tenant_id });
        if (entity == null || entity.picked_qty != entity.qty)
        {
            return (false, _stringLocalizer["data_changed"]);
        }

        var invalid = await connection.ExecuteScalarAsync<bool>("SELECT EXISTS(SELECT 1 FROM `wms_dispatchlist` WHERE `dispatch_no`=@dispatch_no AND `tenant_id`=@tenant_id AND (`dispatch_status`<>3 OR `picked_qty`<>`qty`));", new { entity.dispatch_no, currentUser.tenant_id });
        if (invalid)
        {
            return (false, "该FBA货件还有商品未完成拣货");
        }

        var now = DateTime.Now;
        var changed = await connection.ExecuteAsync("UPDATE `wms_dispatchlist` SET `dispatch_status`=4,`last_update_time`=@now WHERE `dispatch_no`=@dispatch_no AND `tenant_id`=@tenant_id AND `dispatch_status`=3;", new { entity.dispatch_no, currentUser.tenant_id, now });
        return changed > 0 ? (true, _stringLocalizer["operation_success"]) : (false, _stringLocalizer["operation_failed"]);
    }

    public async Task<(bool flag, string msg)> UndoWeighingAsync(int id, CurrentUser currentUser)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync();
        var entity = await connection.QuerySingleOrDefaultAsync<DispatchlistEntity>("SELECT * FROM `wms_dispatchlist` WHERE `id`=@id AND `tenant_id`=@tenant_id AND `dispatch_status` IN (4,5);", new { id, currentUser.tenant_id });
        if (entity == null)
        {
            return (false, _stringLocalizer["data_changed"]);
        }

        await using var transaction = await connection.BeginTransactionAsync();
        var now = DateTime.Now;
        var changed = await connection.ExecuteAsync("""
            UPDATE `wms_dispatchlist` SET `dispatch_status`=3,`weighing_no`='',`weighing_qty`=0,`weighing_weight`=0,
              `weighing_length`=0,`weighing_width`=0,`weighing_height`=0,`weighing_volume`=0,`weighing_person`='',`last_update_time`=@now
            WHERE `dispatch_no`=@dispatch_no AND `tenant_id`=@tenant_id AND `dispatch_status` IN (4,5);
            DELETE FROM `wms_dispatch_weighing_box` WHERE `dispatch_no`=@dispatch_no AND `tenant_id`=@tenant_id;
            """, new { entity.dispatch_no, currentUser.tenant_id, now }, transaction);
        await transaction.CommitAsync();
        return changed > 0 ? (true, _stringLocalizer["operation_success"]) : (false, _stringLocalizer["operation_failed"]);
    }

    public async Task<(bool flag, string msg)> ReturnToWeighingAsync(int id, CurrentUser currentUser)
    {
        if (id <= 0)
        {
            return (false, _stringLocalizer["data_changed"]);
        }
        if (!await HasActionAuthorityAsync(currentUser, WeighingAuthority))
        {
            return (false, "没有称重操作权限");
        }

        await using var connection = await _connectionFactory.OpenConnectionAsync();
        var entity = await connection.QuerySingleOrDefaultAsync<DispatchlistEntity>(
            "SELECT * FROM `wms_dispatchlist` WHERE `id`=@id AND `tenant_id`=@tenant_id;", new { id, currentUser.tenant_id });
        if (entity == null)
        {
            return (false, _stringLocalizer["data_changed"]);
        }

        var dispatchRows = (await connection.QueryAsync<DispatchlistEntity>(
            "SELECT * FROM `wms_dispatchlist` WHERE `dispatch_no`=@dispatch_no AND `tenant_id`=@tenant_id;", new { entity.dispatch_no, currentUser.tenant_id })).AsList();
        if (dispatchRows.Count == 0)
        {
            return (false, _stringLocalizer["data_changed"]);
        }
        if (dispatchRows.All(t => t.dispatch_status == 4))
        {
            return (true, _stringLocalizer["operation_success"]);
        }
        if (dispatchRows.Any(t => t.dispatch_status != 5))
        {
            return (false, _stringLocalizer["data_changed"]);
        }

        var saved = await connection.ExecuteAsync("UPDATE `wms_dispatchlist` SET `dispatch_status`=4,`last_update_time`=@now WHERE `dispatch_no`=@dispatch_no AND `tenant_id`=@tenant_id AND `dispatch_status`=5;",
            new { entity.dispatch_no, currentUser.tenant_id, now=DateTime.Now });
        return saved > 0 ? (true, _stringLocalizer["operation_success"]) : (false, _stringLocalizer["operation_failed"]);
    }

    public async Task<(bool flag, string msg)> CompleteWeighingAsync(int id, CurrentUser currentUser)
    {
        if (id <= 0)
        {
            return (false, _stringLocalizer["data_changed"]);
        }
        if (!await HasActionAuthorityAsync(currentUser, WeighingAuthority))
        {
            return (false, "没有称重操作权限");
        }

        await using var connection = await _connectionFactory.OpenConnectionAsync();
        var entity = await connection.QuerySingleOrDefaultAsync<DispatchlistEntity>(
            "SELECT * FROM `wms_dispatchlist` WHERE `id`=@id AND `tenant_id`=@tenant_id;", new { id, currentUser.tenant_id });
        if (entity == null)
        {
            return (false, _stringLocalizer["data_changed"]);
        }

        var dispatchRows = (await connection.QueryAsync<DispatchlistEntity>(
            "SELECT * FROM `wms_dispatchlist` WHERE `dispatch_no`=@dispatch_no AND `tenant_id`=@tenant_id;", new { entity.dispatch_no, currentUser.tenant_id })).AsList();
        if (dispatchRows.Count == 0)
        {
            return (false, _stringLocalizer["data_changed"]);
        }
        if (dispatchRows.All(t => t.dispatch_status == 5))
        {
            return (true, _stringLocalizer["operation_success"]);
        }
        if (dispatchRows.Any(t => t.dispatch_status != 4))
        {
            return (false, _stringLocalizer["data_changed"]);
        }

        try
        {
            var completed = await CompleteDispatchIfReadyAsync(entity.dispatch_no, currentUser);
            return completed
                ? (true, _stringLocalizer["operation_success"])
                : (false, "称重数据不完整，请检查所有箱号的重量和长宽高");
        }
        catch (MySqlException)
        {
            return (false, _stringLocalizer["data_changed"]);
        }
    }

    public async Task<(bool flag, string msg)> UndoDeliveryAsync(int id, CurrentUser currentUser)
    {
        if (id <= 0)
        {
            return (false, _stringLocalizer["data_changed"]);
        }
        if (!await HasActionAuthorityAsync(currentUser, OutboundDeliveryAuthority))
        {
            return (false, "没有出库操作权限");
        }

        await using var connection = await _connectionFactory.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        try
        {
            var dispatchRow = await connection.QuerySingleOrDefaultAsync<DispatchlistEntity>(
                "SELECT * FROM `wms_dispatchlist` WHERE `id`=@id AND `tenant_id`=@tenant_id AND `dispatch_status`=6 FOR UPDATE;",
                new { id, currentUser.tenant_id }, transaction);
            if (dispatchRow == null)
            {
                return (false, _stringLocalizer["data_changed"]);
            }

            var pickRows = (await connection.QueryAsync<DispatchpicklistEntity>(
                "SELECT * FROM `wms_dispatchpicklist` WHERE `dispatchlist_id`=@id FOR UPDATE;", new { id }, transaction)).AsList();
            if (pickRows.Count == 0 || pickRows.Any(t => !t.is_update_stock))
            {
                return (false, _stringLocalizer["data_changed"]);
            }

            var stockKeys = pickRows
                .GroupBy(t => new
                {
                    t.stock_id,
                    t.goods_location_id,
                    t.sku_id,
                    t.goods_owner_id,
                    t.series_number,
                    t.expiry_date,
                    t.price,
                    t.putaway_date
                })
                .Select(t => new
                {
                    t.Key.stock_id,
                    t.Key.goods_location_id,
                    t.Key.sku_id,
                    t.Key.goods_owner_id,
                    t.Key.series_number,
                    t.Key.expiry_date,
                    t.Key.price,
                    t.Key.putaway_date,
                    picked_qty = t.Sum(p => p.picked_qty)
                })
                .ToList();
            var skuIds = stockKeys.Select(t => t.sku_id).Distinct().ToList();
            var locationIds = stockKeys.Select(t => t.goods_location_id).Distinct().ToList();
            var stockRows = (await connection.QueryAsync<StockEntity>(
                "SELECT * FROM `wms_stock` WHERE `tenant_id`=@tenant_id AND `sku_id` IN @skuIds AND `goods_location_id` IN @locationIds FOR UPDATE;",
                new { currentUser.tenant_id, skuIds, locationIds }, transaction)).AsList();
            var existingInboundRecords = (await connection.QueryAsync<StockRecordKey>(
                "SELECT `biz_item_id`,`stock_id` FROM `wms_stock_record` WHERE `tenant_id`=@tenant_id AND `biz_id`=@id AND `biz_type` LIKE 'DISPATCH_IN%';",
                new { currentUser.tenant_id, id=dispatchRow.id }, transaction)).AsList();
            var now = DateTime.Now;
            var operatorName = currentUser.user_name?.Trim() ?? string.Empty;
            if (operatorName.Length > 128)
            {
                operatorName = operatorName[..128];
            }
            foreach (var key in stockKeys)
            {
                var stock = key.stock_id > 0
                    ? stockRows.FirstOrDefault(t => t.id == key.stock_id
                        && t.goods_location_id == key.goods_location_id
                        && t.sku_id == key.sku_id
                        && t.goods_owner_id == key.goods_owner_id
                        && t.series_number == key.series_number
                        && t.expiry_date == key.expiry_date
                        && t.price == key.price
                        && t.putaway_date == key.putaway_date)
                    : stockRows.Where(t => t.goods_location_id == key.goods_location_id
                            && t.sku_id == key.sku_id
                            && t.goods_owner_id == key.goods_owner_id
                            && t.series_number == key.series_number
                            && t.expiry_date == key.expiry_date
                            && t.price == key.price
                            && t.putaway_date == key.putaway_date)
                        .OrderBy(t => t.id)
                        .FirstOrDefault();
                if (stock == null)
                {
                    return (false, _stringLocalizer["data_changed"]);
                }
                var runningQty = stock.qty;
                var groupedPickRows = pickRows
                    .Where(t => t.stock_id == key.stock_id
                        && t.goods_location_id == key.goods_location_id
                        && t.sku_id == key.sku_id
                        && t.goods_owner_id == key.goods_owner_id
                        && t.series_number == key.series_number
                        && t.expiry_date == key.expiry_date
                        && t.price == key.price
                        && t.putaway_date == key.putaway_date)
                    .OrderBy(t => t.id)
                    .ToList();
                foreach (var pickRow in groupedPickRows)
                {
                    var afterQty = runningQty + pickRow.picked_qty;
                    var cycle = existingInboundRecords.Count(t => t.biz_item_id == pickRow.id && t.stock_id == stock.id) + 1;
                    var bizType = cycle == 1 ? "DISPATCH_IN" : $"DISPATCH_IN_{cycle}";
                    await connection.ExecuteAsync("""
                        INSERT INTO `wms_stock_record` (`record_no`,`biz_type`,`biz_id`,`biz_item_id`,`stock_id`,`sku_id`,`goods_location_id`,`goods_owner_id`,`change_qty`,`before_qty`,`after_qty`,`direction`,`operator_id`,`operator_name`,`remark`,`operate_time`,`tenant_id`)
                        VALUES (@record_no,@bizType,@dispatchlist_id,@pick_id,@stock_id,@sku_id,@location_id,@owner_id,@picked_qty,@beforeQty,@afterQty,'IN',@user_id,@operatorName,'已出库发货单撤回',@now,@tenant_id);
                        """, new { record_no=$"MWMS-DI-{pickRow.dispatchlist_id}-{pickRow.id}-{cycle}", bizType, pickRow.dispatchlist_id, pick_id=pickRow.id, stock_id=stock.id, pickRow.sku_id, location_id=pickRow.goods_location_id, owner_id=pickRow.goods_owner_id, pickRow.picked_qty, beforeQty=runningQty, afterQty, currentUser.user_id, operatorName, now, currentUser.tenant_id }, transaction);
                    runningQty = afterQty;
                }
                await connection.ExecuteAsync("UPDATE `wms_stock` SET `qty`=@runningQty,`last_update_time`=@now WHERE `id`=@id AND `tenant_id`=@tenant_id;",
                    new { runningQty, now, stock.id, currentUser.tenant_id }, transaction);
            }

            dispatchRow.dispatch_status = 5;
            dispatchRow.lock_qty = dispatchRow.picked_qty;
            dispatchRow.actual_qty = 0;
            dispatchRow.intrasit_qty = 0;
            dispatchRow.last_update_time = now;
            foreach (var pickRow in pickRows)
            {
                pickRow.is_update_stock = false;
                pickRow.last_update_time = now;
            }

            await connection.ExecuteAsync("""
                UPDATE `wms_dispatchlist` SET `dispatch_status`=5,`lock_qty`=`picked_qty`,`actual_qty`=0,`intrasit_qty`=0,`last_update_time`=@now
                WHERE `id`=@id AND `tenant_id`=@tenant_id AND `dispatch_status`=6;
                UPDATE `wms_dispatchpicklist` SET `is_update_stock`=0,`last_update_time`=@now WHERE `dispatchlist_id`=@id;
                """, new { id, currentUser.tenant_id, now }, transaction);
            await transaction.CommitAsync();
            return (true, _stringLocalizer["operation_success"]);
        }
        catch (MySqlException)
        {
            await transaction.RollbackAsync();
            return (false, _stringLocalizer["data_changed"]);
        }
    }

    public async Task<List<OutboundCarrierOptionViewModel>> GetOutboundCarrierOptionsAsync()
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync();
        return (await connection.QueryAsync<OutboundCarrierOptionViewModel>("""
            SELECT `id`,COALESCE(`name`,'') `name` FROM `erp_warehouse`
            WHERE `deleted`=0 AND `attr`='国内仓库' AND `name` IS NOT NULL AND `name` NOT IN @excluded
            ORDER BY `name`,`id`;
            """, new { excluded=ExcludedCarrierWarehouseNames })).AsList();
    }

    public async Task<(bool flag, string msg)> SetOutboundVolumeDivisorAsync(
        SetOutboundVolumeDivisorViewModel viewModel,
        CurrentUser currentUser)
    {
        if (!await HasOutboundSettingAuthorityAsync(currentUser))
        {
            return (false, "没有待出库设置权限");
        }

        if (!AllowedVolumeDivisors.Contains(viewModel.volume_divisor))
        {
            return (false, _stringLocalizer["data_changed"]);
        }

        var row = await GetPendingOutboundRowAsync(viewModel.id, currentUser);
        if (row == null)
        {
            return (false, _stringLocalizer["data_changed"]);
        }
        if (row.volume_divisor == viewModel.volume_divisor)
        {
            return (true, _stringLocalizer["operation_success"]);
        }

        await using var connection = await _connectionFactory.OpenConnectionAsync();
        var saved = await connection.ExecuteAsync("UPDATE `wms_dispatchlist` SET `volume_divisor`=@volume_divisor,`last_update_time`=@now WHERE `id`=@id AND `tenant_id`=@tenant_id AND `dispatch_status`=5;",
            new { viewModel.volume_divisor, now=DateTime.Now, viewModel.id, currentUser.tenant_id });
        return saved > 0 ? (true, _stringLocalizer["operation_success"]) : (false, _stringLocalizer["data_changed"]);
    }

    public async Task<(bool flag, string msg)> SetOutboundCarrierAsync(
        SetOutboundCarrierViewModel viewModel,
        CurrentUser currentUser)
    {
        if (!await HasOutboundSettingAuthorityAsync(currentUser))
        {
            return (false, "没有待出库设置权限");
        }

        await using var connection = await _connectionFactory.OpenConnectionAsync();
        var carrier = await connection.QuerySingleOrDefaultAsync<CarrierRow>("""
            SELECT `id`,`name` FROM `erp_warehouse` WHERE `id`=@carrier_warehouse_id AND `deleted`=0
              AND `attr`='国内仓库' AND `name` IS NOT NULL AND `name` NOT IN @excluded LIMIT 1;
            """, new { viewModel.carrier_warehouse_id, excluded=ExcludedCarrierWarehouseNames });
        if (carrier == null)
        {
            return (false, _stringLocalizer["data_changed"]);
        }

        var row = await GetPendingOutboundRowAsync(viewModel.id, currentUser);
        if (row == null)
        {
            return (false, _stringLocalizer["data_changed"]);
        }
        if (row.carrier_warehouse_id == carrier.id && row.carrier_unit == carrier.name)
        {
            return (true, _stringLocalizer["operation_success"]);
        }

        var saved = await connection.ExecuteAsync("UPDATE `wms_dispatchlist` SET `carrier_warehouse_id`=@id,`carrier_unit`=@name,`last_update_time`=@now WHERE `id`=@row_id AND `tenant_id`=@tenant_id AND `dispatch_status`=5;",
            new { carrier.id, carrier.name, now=DateTime.Now, row_id=viewModel.id, currentUser.tenant_id });
        return saved > 0 ? (true, _stringLocalizer["operation_success"]) : (false, _stringLocalizer["data_changed"]);
    }

    private async Task<DispatchlistEntity?> GetPendingOutboundRowAsync(int id, CurrentUser currentUser)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync();
        return await connection.QuerySingleOrDefaultAsync<DispatchlistEntity>(
            "SELECT * FROM `wms_dispatchlist` WHERE `id`=@id AND `tenant_id`=@tenant_id AND `dispatch_status`=5 LIMIT 1;",
            new { id, currentUser.tenant_id });
    }

    private async Task<bool> HasOutboundSettingAuthorityAsync(CurrentUser currentUser)
    {
        return await HasActionAuthorityAsync(currentUser, OutboundSettingAuthority);
    }

    private async Task<bool> HasActionAuthorityAsync(CurrentUser currentUser, string requiredAuthority)
    {
        var roleName = currentUser.user_role?.Trim() ?? string.Empty;
        if (string.Equals(roleName, "admin", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        await using var connection = await _connectionFactory.OpenConnectionAsync();
        var roleId = await connection.QuerySingleOrDefaultAsync<int?>("SELECT `id` FROM `wms_userrole` WHERE `tenant_id`=@tenant_id AND `is_valid`=1 AND `role_name`=@roleName LIMIT 1;", new { currentUser.tenant_id, roleName });
        if (!roleId.HasValue)
        {
            return false;
        }

        var actionAuthorities = (await connection.QueryAsync<string>("SELECT `menu_actions_authority` FROM `wms_rolemenu` WHERE `tenant_id`=@tenant_id AND `userrole_id`=@roleId AND `authority`=1;", new { currentUser.tenant_id, roleId=roleId.Value })).AsList();

        return actionAuthorities.Any(actions =>
        {
            try
            {
                return (JsonSerializer.Deserialize<List<string>>(actions) ?? [])
                    .Any(action => string.Equals(action?.Trim(), requiredAuthority, StringComparison.Ordinal));
            }
            catch (JsonException)
            {
                return false;
            }
        });
    }

    public async Task<(List<DispatchWeighingShipmentViewModel> data, int totals)> GetWeighingShipmentsAsync(
        PageSearch pageSearch,
        CurrentUser currentUser)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync();
        var dispatchRows = (await connection.QueryAsync<DispatchlistEntity>(
            "SELECT * FROM `wms_dispatchlist` WHERE `tenant_id`=@tenant_id AND `dispatch_status` IN (4,5);", new { currentUser.tenant_id })).AsList();
        if (dispatchRows.Count == 0)
        {
            return ([], 0);
        }

        var dispatchNos = dispatchRows.Select(t => t.dispatch_no).Distinct().ToList();
        var moves = (await connection.QueryAsync<ErpStockMoveEntity>("SELECT * FROM `trk_stock_move` WHERE `deleted`=0 AND `no` IN @dispatchNos;", new { dispatchNos })).AsList();
        var moveIds = moves.Select(t => t.id).ToList();
        var moveItems = (await connection.QueryAsync<ErpStockMoveItemEntity>("SELECT * FROM `trk_stock_move_item` WHERE `deleted`=0 AND `stock_move_id` IN @moveIds;", new { moveIds })).AsList();
        var commodityIds = moveItems.Where(t => t.commodity_id.HasValue)
            .Select(t => t.commodity_id!.Value).Distinct().ToList();
        var skuMap = (await connection.QueryAsync<ErpCommodityMapEntity>("SELECT * FROM `wms_erp_commodity_map` WHERE `tenant_id`=@tenant_id AND `erp_commodity_id` IN @commodityIds;", new { currentUser.tenant_id, commodityIds })).ToDictionary(t => t.erp_commodity_id, t => t.wms_sku_id);
        var snapshots = moveItems.ToDictionary(t => t.id, ParseSnapshot);
        var shipmentItemIds = snapshots.Values.Where(t => t.fbaShipmentItemId.HasValue)
            .Select(t => t.fbaShipmentItemId!.Value).Distinct().ToList();
        var shipmentItems = (await connection.QueryAsync<ErpFbaShipmentItemEntity>("SELECT * FROM `erp_fba_shipment_item` WHERE `deleted`=0 AND `id` IN @shipmentItemIds;", new { shipmentItemIds })).ToDictionary(t => t.id);
        var resolvedShipmentIds = moveItems.ToDictionary(t => t.id, t =>
        {
            var snapshot = snapshots[t.id];
            if (snapshot.fbaShipmentId.HasValue) return snapshot.fbaShipmentId;
            return snapshot.fbaShipmentItemId.HasValue
                && shipmentItems.TryGetValue(snapshot.fbaShipmentItemId.Value, out var shipmentItem)
                    ? shipmentItem.shipment_id
                    : null;
        });
        var shipmentIds = resolvedShipmentIds.Values.Where(t => t.HasValue)
            .Select(t => t!.Value).Distinct().ToList();
        var shipments = (await connection.QueryAsync<ErpFbaShipmentEntity>("SELECT * FROM `erp_fba_shipment` WHERE `deleted`=0 AND `id` IN @shipmentIds;", new { shipmentIds })).ToDictionary(t => t.id);
        var boxes = (await connection.QueryAsync<ErpFbaSpdBoxEntity>("SELECT * FROM `erp_fba_spd_box` WHERE `deleted`=0 AND `shipment_id` IN @shipmentIds;", new { shipmentIds })).AsList();
        var measured = (await connection.QueryAsync<DispatchWeighingBoxEntity>("SELECT * FROM `wms_dispatch_weighing_box` WHERE `tenant_id`=@tenant_id AND `fba_shipment_id` IN @shipmentIds;", new { currentUser.tenant_id, shipmentIds })).AsList();

        var rowsByNo = dispatchRows.GroupBy(t => t.dispatch_no).ToDictionary(t => t.Key, t => t.ToList());
        var result = new List<DispatchWeighingShipmentViewModel>();
        foreach (var move in moves)
        {
            if (!rowsByNo.TryGetValue(move.no, out var wmsRows)) continue;
            var wmsSkuIds = wmsRows.Select(t => t.sku_id).ToHashSet();
            var sourceItems = moveItems.Where(t => t.stock_move_id == move.id
                && t.commodity_id.HasValue
                && skuMap.TryGetValue(t.commodity_id.Value, out var wmsSkuId)
                && wmsSkuIds.Contains(wmsSkuId)).ToList();
            foreach (var shipmentGroup in sourceItems
                .Where(t => resolvedShipmentIds[t.id].HasValue)
                .GroupBy(t => resolvedShipmentIds[t.id]!.Value))
            {
                if (!shipments.TryGetValue(shipmentGroup.Key, out var shipment)) continue;
                var shipmentBoxes = boxes.Where(t => t.shipment_id == shipment.id).ToList();
                var shipmentBoxIds = shipmentBoxes.Select(t => t.id).ToHashSet();
                var shipmentMeasurements = measured.Where(t => t.dispatch_no == move.no
                    && t.fba_shipment_id == shipment.id
                    && shipmentBoxIds.Contains(t.erp_box_id)).ToList();
                var groupShipmentItems = shipmentGroup.Select(t => snapshots[t.id].fbaShipmentItemId)
                    .Where(t => t.HasValue && shipmentItems.ContainsKey(t.Value))
                    .Select(t => shipmentItems[t!.Value]).ToList();
                var names = shipmentGroup.Select(t => snapshots[t.id].commodityName)
                    .Concat(groupShipmentItems.Select(t => t.commodity_name ?? t.title ?? string.Empty))
                    .Concat(shipmentGroup.Select(t => t.commodity_name ?? string.Empty))
                    .Where(t => !string.IsNullOrWhiteSpace(t)).Distinct().ToList();
                var fbaSkus = shipmentGroup.Select(t => snapshots[t.id].fbaSku)
                    .Concat(groupShipmentItems.Select(t => t.fn_sku ?? string.Empty))
                    .Where(t => !string.IsNullOrWhiteSpace(t)).Distinct().ToList();
                result.Add(new DispatchWeighingShipmentViewModel
                {
                    id = wmsRows.Min(t => t.id),
                    dispatch_no = move.no,
                    dispatch_status = wmsRows.Min(t => t.dispatch_status),
                    fba_shipment_id = shipment.id,
                    fba_no = shipment.amazon_shipment_id ?? string.Empty,
                    main_image = shipmentGroup.Select(t => snapshots[t.id].mainImage)
                        .Concat(groupShipmentItems.Select(t => t.main_image ?? string.Empty))
                        .FirstOrDefault(t => !string.IsNullOrWhiteSpace(t)) ?? string.Empty,
                    commodity_name = string.Join("、", names),
                    fba_sku = string.Join("、", fbaSkus),
                    shop_name = shipment.shop_name ?? string.Empty,
                    dept_name = move.dept_name ?? string.Empty,
                    order_user_name = move.order_user_name ?? string.Empty,
                    creator = wmsRows.Select(t => t.creator).FirstOrDefault(t => !string.IsNullOrWhiteSpace(t)) ?? string.Empty,
                    shipment_total_qty = shipment.quantity ?? wmsRows.Sum(t => t.qty),
                    variant_qty = Math.Max(1, shipmentGroup.Select(t => snapshots[t.id].fbaShipmentItemId ?? t.id).Distinct().Count()),
                    box_count = shipmentBoxes.Count,
                    weighed_box_count = shipmentMeasurements.Count(t => t.weighing_weight > 0),
                    dimension_started_box_count = shipmentMeasurements.Count(t =>
                        t.weighing_length > 0 || t.weighing_width > 0 || t.weighing_height > 0),
                    dimension_measured_box_count = shipmentMeasurements.Count(t =>
                        t.weighing_length > 0 && t.weighing_width > 0 && t.weighing_height > 0),
                    weighing_weight = shipmentMeasurements.Sum(t => t.weighing_weight)
                });
            }
        }

        foreach (var dispatchGroup in result.GroupBy(t => t.dispatch_no))
        {
            var canCompleteDispatch = dispatchGroup.All(t => !t.is_todo);
            foreach (var row in dispatchGroup)
            {
                row.can_complete_dispatch = canCompleteDispatch;
            }
        }

        var dispatchSearch = FindSearchText(pageSearch, "dispatch_no");
        var productSearch = FindSearchText(pageSearch, "spu_name");
        if (!string.IsNullOrWhiteSpace(dispatchSearch))
            result = result.Where(t => t.dispatch_no.Contains(dispatchSearch, StringComparison.OrdinalIgnoreCase)
                || t.fba_no.Contains(dispatchSearch, StringComparison.OrdinalIgnoreCase)).ToList();
        if (!string.IsNullOrWhiteSpace(productSearch))
            result = result.Where(t => t.commodity_name.Contains(productSearch, StringComparison.OrdinalIgnoreCase)
                || t.fba_sku.Contains(productSearch, StringComparison.OrdinalIgnoreCase)).ToList();

        result = result.OrderByDescending(t => t.id).ToList();
        var totals = result.Count;
        var pageIndex = Math.Max(pageSearch.pageIndex, 1);
        var pageSize = Math.Max(pageSearch.pageSize, 1);
        return (result.Skip((pageIndex - 1) * pageSize).Take(pageSize).ToList(), totals);
    }

    public async Task<List<DispatchWeighingBoxViewModel>> GetWeighingBoxesAsync(
        string dispatchNo,
        long shipmentId,
        CurrentUser currentUser)
    {
        if (!await CanAccessShipmentAsync(dispatchNo, shipmentId, currentUser)) return [];
        await using var connection = await _connectionFactory.OpenConnectionAsync();
        var boxes = (await connection.QueryAsync<ErpFbaSpdBoxEntity>("SELECT * FROM `erp_fba_spd_box` WHERE `deleted`=0 AND `shipment_id`=@shipmentId ORDER BY `idx`,`box_id`;", new { shipmentId })).AsList();
        var measured = (await connection.QueryAsync<DispatchWeighingBoxEntity>("SELECT * FROM `wms_dispatch_weighing_box` WHERE `tenant_id`=@tenant_id AND `dispatch_no`=@dispatchNo AND `fba_shipment_id`=@shipmentId;", new { currentUser.tenant_id, dispatchNo, shipmentId })).ToDictionary(t => t.erp_box_id);
        return boxes.Select(box =>
        {
            measured.TryGetValue(box.id, out var value);
            return new DispatchWeighingBoxViewModel
            {
                erp_box_id = box.id,
                box_no = box.box_id,
                tracking_id = box.tracking_id ?? string.Empty,
                box_index = box.idx ?? 0,
                weighing_weight = value?.weighing_weight ?? 0,
                weighing_length = value?.weighing_length ?? 0,
                weighing_width = value?.weighing_width ?? 0,
                weighing_height = value?.weighing_height ?? 0,
                weighing_volume = value?.weighing_volume ?? 0,
                is_weighed = value != null
                    && value.weighing_weight > 0
                    && value.weighing_length > 0
                    && value.weighing_width > 0
                    && value.weighing_height > 0
            };
        }).ToList();
    }

    public async Task<(bool flag, string msg)> SaveWeighingBoxesAsync(
        List<SaveDispatchWeighingBoxViewModel> viewModels,
        CurrentUser currentUser)
    {
        if (viewModels == null || viewModels.Count == 0)
            return (false, "没有可保存的箱号数据");
        if (viewModels.Any(t => !IsValidMeasurement(t)))
            return (false, "每个箱子的重量和长宽高都必须大于0");

        var dispatchNo = viewModels[0].dispatch_no;
        var shipmentId = viewModels[0].fba_shipment_id;
        if (string.IsNullOrWhiteSpace(dispatchNo)
            || shipmentId <= 0
            || viewModels.Any(t => t.dispatch_no != dispatchNo || t.fba_shipment_id != shipmentId)
            || viewModels.Select(t => t.erp_box_id).Distinct().Count() != viewModels.Count)
            return (false, "本次提交的箱号数据不属于同一个FBA货件");
        if (!await CanAccessShipmentAsync(dispatchNo, shipmentId, currentUser))
            return (false, _stringLocalizer["data_changed"]);

        await using var connection = await _connectionFactory.OpenConnectionAsync();
        var boxes = (await connection.QueryAsync<ErpFbaSpdBoxEntity>("SELECT * FROM `erp_fba_spd_box` WHERE `deleted`=0 AND `shipment_id`=@shipmentId ORDER BY `idx`,`box_id`;", new { shipmentId })).AsList();
        var submittedIds = viewModels.Select(t => t.erp_box_id).ToHashSet();
        if (boxes.Count == 0 || boxes.Count != viewModels.Count || boxes.Any(t => !submittedIds.Contains(t.id)))
            return (false, "箱号数据已发生变化，请刷新后重新填写");

        var fbaNo = await connection.QuerySingleOrDefaultAsync<string>("SELECT `amazon_shipment_id` FROM `erp_fba_shipment` WHERE `deleted`=0 AND `id`=@shipmentId LIMIT 1;", new { shipmentId }) ?? string.Empty;
        var valuesByBoxId = viewModels.ToDictionary(t => t.erp_box_id);

        await using var transaction = await connection.BeginTransactionAsync();
        var existing = (await connection.QueryAsync<DispatchWeighingBoxEntity>("SELECT * FROM `wms_dispatch_weighing_box` WHERE `tenant_id`=@tenant_id AND `erp_box_id` IN @submittedIds FOR UPDATE;", new { currentUser.tenant_id, submittedIds }, transaction)).ToDictionary(t => t.erp_box_id);
        if (existing.Values.Any(t => t.dispatch_no != dispatchNo || t.fba_shipment_id != shipmentId))
            return (false, "存在已关联其它WMS发货单的箱号");

        var now = DateTime.Now;
        foreach (var box in boxes)
        {
            if (!existing.TryGetValue(box.id, out var entity))
            {
                entity = new DispatchWeighingBoxEntity
                {
                    tenant_id = currentUser.tenant_id,
                    dispatch_no = dispatchNo,
                    fba_shipment_id = shipmentId,
                    fba_no = fbaNo,
                    erp_box_id = box.id,
                    box_no = box.box_id,
                    box_index = box.idx ?? 0,
                    tracking_id = box.tracking_id ?? string.Empty,
                    create_time = now
                };
            }

            var value = valuesByBoxId[box.id];
            ApplyMeasurement(entity, value.weighing_weight, value.weighing_length, value.weighing_width,
                value.weighing_height, currentUser, now, null);
            if (entity.id == 0)
            {
                await connection.ExecuteAsync("""
                    INSERT INTO `wms_dispatch_weighing_box` (`tenant_id`,`dispatch_no`,`fba_shipment_id`,`fba_no`,`erp_box_id`,`box_no`,`box_index`,`tracking_id`,`weighing_weight`,`weighing_length`,`weighing_width`,`weighing_height`,`weighing_volume`,`weighing_person_id`,`weighing_person`,`weighing_time`,`copied_from_erp_box_id`,`create_time`,`last_update_time`)
                    VALUES (@tenant_id,@dispatch_no,@fba_shipment_id,@fba_no,@erp_box_id,@box_no,@box_index,@tracking_id,@weighing_weight,@weighing_length,@weighing_width,@weighing_height,@weighing_volume,@weighing_person_id,@weighing_person,@weighing_time,@copied_from_erp_box_id,@create_time,@last_update_time);
                    """, entity, transaction);
            }
            else
            {
                await connection.ExecuteAsync("""
                    UPDATE `wms_dispatch_weighing_box` SET `weighing_weight`=@weighing_weight,`weighing_length`=@weighing_length,
                      `weighing_width`=@weighing_width,`weighing_height`=@weighing_height,`weighing_volume`=@weighing_volume,
                      `weighing_person_id`=@weighing_person_id,`weighing_person`=@weighing_person,`weighing_time`=@weighing_time,
                      `copied_from_erp_box_id`=@copied_from_erp_box_id,`last_update_time`=@last_update_time
                    WHERE `id`=@id AND `tenant_id`=@tenant_id AND `dispatch_no`=@dispatch_no AND `fba_shipment_id`=@fba_shipment_id;
                    """, entity, transaction);
            }
        }
        var latestBoxIds = (await connection.QueryAsync<long>("SELECT `id` FROM `erp_fba_spd_box` WHERE `deleted`=0 AND `shipment_id`=@shipmentId;", new { shipmentId }, transaction)).AsList();
        if (latestBoxIds.Count != submittedIds.Count || latestBoxIds.Any(t => !submittedIds.Contains(t)))
            return (false, "箱号数据已发生变化，请刷新后重新填写");
        await CompleteDispatchIfReadyCoreAsync(connection, transaction, dispatchNo, currentUser);
        await transaction.CommitAsync();
        return (true, _stringLocalizer["operation_success"]);
    }

    private async Task<bool> CanAccessShipmentAsync(string dispatchNo, long shipmentId, CurrentUser currentUser)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync();
        var hasDispatch = await connection.ExecuteScalarAsync<bool>("SELECT EXISTS(SELECT 1 FROM `wms_dispatchlist` WHERE `tenant_id`=@tenant_id AND `dispatch_no`=@dispatchNo AND `dispatch_status` IN (4,5));", new { currentUser.tenant_id, dispatchNo });
        if (!hasDispatch) return false;
        var shipmentIds = await GetShipmentIdsForDispatchAsync(dispatchNo, currentUser);
        return shipmentIds.Contains(shipmentId);
    }

    private async Task<bool> CompleteDispatchIfReadyAsync(string dispatchNo, CurrentUser currentUser)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync();
        return await CompleteDispatchIfReadyCoreAsync(connection, null, dispatchNo, currentUser);
    }

    private async Task<bool> CompleteDispatchIfReadyCoreAsync(MySqlConnection connection, IDbTransaction? transaction, string dispatchNo, CurrentUser currentUser)
    {
        var rows = (await connection.QueryAsync<DispatchlistEntity>("SELECT * FROM `wms_dispatchlist` WHERE `tenant_id`=@tenant_id AND `dispatch_no`=@dispatchNo;", new { currentUser.tenant_id, dispatchNo }, transaction)).AsList();
        if (rows.Count == 0) return false;
        var isPendingOutbound = rows.All(t => t.dispatch_status == 5);
        if (!isPendingOutbound && rows.Any(t => t.dispatch_status != 4)) return false;

        var shipmentIds = await GetShipmentIdsForDispatchAsync(dispatchNo, currentUser);
        if (shipmentIds.Count == 0) return false;
        var boxes = (await connection.QueryAsync<BoxKey>("SELECT `id`,`shipment_id` FROM `erp_fba_spd_box` WHERE `deleted`=0 AND `shipment_id` IN @shipmentIds;", new { shipmentIds }, transaction)).AsList();
        if (shipmentIds.Any(shipmentId => boxes.All(t => t.shipment_id != shipmentId))) return false;
        var boxIds = boxes.Select(t => t.id).ToList();
        var measurements = (await connection.QueryAsync<DispatchWeighingBoxEntity>("SELECT * FROM `wms_dispatch_weighing_box` WHERE `tenant_id`=@tenant_id AND `dispatch_no`=@dispatchNo AND `erp_box_id` IN @boxIds;", new { currentUser.tenant_id, dispatchNo, boxIds }, transaction)).AsList();
        if (measurements.Select(t => t.erp_box_id).Distinct().Count() != boxIds.Distinct().Count()
            || measurements.Any(t => t.weighing_weight <= 0
                || t.weighing_length <= 0
                || t.weighing_width <= 0
                || t.weighing_height <= 0)) return false;

        var now = DateTime.Now;
        var totalWeight = measurements.Sum(t => t.weighing_weight);
        var totalVolume = measurements.Sum(t => t.weighing_volume);
        var firstRowId = rows.Select(t => t.id).DefaultIfEmpty().Min();
        await connection.ExecuteAsync("""
            UPDATE `wms_dispatchlist` SET `dispatch_status`=5,`weighing_no`=@weighingNo,`weighing_qty`=`picked_qty`,
              `weighing_weight`=CASE WHEN `id`=@firstRowId THEN @totalWeight ELSE 0 END,
              `weighing_length`=@length,`weighing_width`=@width,`weighing_height`=@height,
              `weighing_volume`=CASE WHEN `id`=@firstRowId THEN @totalVolume ELSE 0 END,
              `weighing_person`=@user_name,`last_update_time`=@now
            WHERE `tenant_id`=@tenant_id AND `dispatch_no`=@dispatchNo AND `dispatch_status` IN (4,5);
            """, new { weighingNo=$"{dispatchNo}-BOX", firstRowId, totalWeight, totalVolume,
                length=measurements.Count==1?measurements[0].weighing_length:0,
                width=measurements.Count==1?measurements[0].weighing_width:0,
                height=measurements.Count==1?measurements[0].weighing_height:0,
                currentUser.user_name, now, currentUser.tenant_id, dispatchNo }, transaction);
        return true;
    }

    private async Task<List<long>> GetShipmentIdsForDispatchAsync(string dispatchNo, CurrentUser currentUser)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync();
        var wmsSkuIds = (await connection.QueryAsync<int>("SELECT DISTINCT `sku_id` FROM `wms_dispatchlist` WHERE `tenant_id`=@tenant_id AND `dispatch_no`=@dispatchNo;", new { currentUser.tenant_id, dispatchNo })).AsList();
        var moveIds = (await connection.QueryAsync<long>("SELECT `id` FROM `trk_stock_move` WHERE `deleted`=0 AND `no`=@dispatchNo;", new { dispatchNo })).AsList();
        if (moveIds.Count == 0) return [];
        var items = (await connection.QueryAsync<ErpStockMoveItemEntity>("SELECT * FROM `trk_stock_move_item` WHERE `deleted`=0 AND `stock_move_id` IN @moveIds;", new { moveIds })).AsList();
        var commodityIds = items.Where(t => t.commodity_id.HasValue)
            .Select(t => t.commodity_id!.Value).Distinct().ToList();
        var allowedCommodityIds = (await connection.QueryAsync<long>("SELECT `erp_commodity_id` FROM `wms_erp_commodity_map` WHERE `tenant_id`=@tenant_id AND `erp_commodity_id` IN @commodityIds AND `wms_sku_id` IN @wmsSkuIds;", new { currentUser.tenant_id, commodityIds, wmsSkuIds })).AsList();
        items = items.Where(t => t.commodity_id.HasValue && allowedCommodityIds.Contains(t.commodity_id.Value)).ToList();
        var snapshots = items.Select(ParseSnapshot).ToList();
        var result = snapshots.Where(t => t.fbaShipmentId.HasValue)
            .Select(t => t.fbaShipmentId!.Value).ToList();
        var shipmentItemIds = snapshots.Where(t => t.fbaShipmentItemId.HasValue)
            .Select(t => t.fbaShipmentItemId!.Value).Distinct().ToList();
        if (shipmentItemIds.Count > 0)
        {
            result.AddRange(await connection.QueryAsync<long>("SELECT `shipment_id` FROM `erp_fba_shipment_item` WHERE `deleted`=0 AND `id` IN @shipmentItemIds;", new { shipmentItemIds }));
        }
        return result.Distinct().ToList();
    }

    private static bool IsValidMeasurement(SaveDispatchWeighingBoxViewModel vm) =>
        vm.weighing_weight > 0 && vm.weighing_length > 0 && vm.weighing_width > 0 && vm.weighing_height > 0;

    private static void ApplyMeasurement(DispatchWeighingBoxEntity entity, decimal weight, decimal length, decimal width,
        decimal height, CurrentUser currentUser, DateTime now, long? copiedFrom)
    {
        entity.weighing_weight = Math.Round(weight, 2);
        entity.weighing_length = Math.Round(length, 2);
        entity.weighing_width = Math.Round(width, 2);
        entity.weighing_height = Math.Round(height, 2);
        entity.weighing_volume = Math.Round(length * width * height, 2);
        entity.weighing_person_id = currentUser.user_id;
        entity.weighing_person = currentUser.user_name;
        entity.weighing_time = now;
        entity.copied_from_erp_box_id = copiedFrom;
        entity.last_update_time = now;
    }

    private static string FindSearchText(PageSearch pageSearch, string name) => pageSearch.searchObjects
        .FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase))?.Text?.Trim() ?? string.Empty;

    private static PackingSnapshot ParseSnapshot(ModernWMS.Core.DBContext.Entities.ErpStockMoveItemEntity item)
    {
        var json = !string.IsNullOrWhiteSpace(item.product_snapshot_json) ? item.product_snapshot_json : item.remark;
        if (string.IsNullOrWhiteSpace(json))
        {
            return new PackingSnapshot();
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            return new PackingSnapshot
            {
                mainImage = GetString(root, "mainImage"),
                commodityName = GetString(root, "commodityName"),
                fbaSku = GetString(root, "fbaSku"),
                variantQty = GetInt64(root, "variantQty"),
                fbaShipmentId = GetInt64(root, "fbaShipmentId"),
                fbaShipmentItemId = GetInt64(root, "fbaShipmentItemId"),
                preparedTime = GetDateTime(root, "preparedTime")
            };
        }
        catch (JsonException)
        {
            return new PackingSnapshot();
        }
    }

    private static string GetString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var value)
            ? value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : value.ToString()
            : string.Empty;
    }

    private static long? GetInt64(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number))
        {
            return number;
        }

        return value.ValueKind == JsonValueKind.String && long.TryParse(value.GetString(), out var parsed)
            ? parsed
            : null;
    }

    private static DateTime? GetDateTime(JsonElement element, string propertyName)
    {
        var value = GetString(element, propertyName);
        return DateTime.TryParse(value, out var result) ? result : null;
    }

    private sealed class PackingSnapshot
    {
        public string mainImage { get; init; } = string.Empty;
        public string commodityName { get; init; } = string.Empty;
        public string fbaSku { get; init; } = string.Empty;
        public long? variantQty { get; init; }
        public long? fbaShipmentId { get; init; }
        public long? fbaShipmentItemId { get; init; }
        public DateTime? preparedTime { get; init; }
    }
    private sealed class MeasuredBoxTotal { public string dispatch_no { get; set; } = string.Empty; public long fba_shipment_id { get; set; } public decimal weight { get; set; } public decimal volume { get; set; } }
    private sealed class StockRecordKey { public int biz_item_id { get; set; } public int stock_id { get; set; } }
    private sealed class CarrierRow { public long id { get; set; } public string name { get; set; } = string.Empty; }
    private sealed class BoxKey { public long id { get; set; } public long shipment_id { get; set; } }
}
