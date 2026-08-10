using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using ModernWMS.Core.DBContext;
using ModernWMS.Core.JWT;
using ModernWMS.Core.Models;
using ModernWMS.WMS.Entities.Models;
using ModernWMS.WMS.Entities.ViewModels;
using ModernWMS.WMS.IServices;

namespace ModernWMS.WMS.Services;

/// <summary>
/// Picking workflow that operates on individual dispatch rows.
/// </summary>
public class DispatchlistPickingService : IDispatchlistPickingService
{
    private readonly SqlDBContext _wmsDbContext;
    private readonly RuoyiDbContext _ruoyiDbContext;
    private readonly IStringLocalizer<ModernWMS.Core.MultiLanguage> _stringLocalizer;

    public DispatchlistPickingService(
        SqlDBContext wmsDbContext,
        RuoyiDbContext ruoyiDbContext,
        IStringLocalizer<ModernWMS.Core.MultiLanguage> stringLocalizer)
    {
        _wmsDbContext = wmsDbContext;
        _ruoyiDbContext = ruoyiDbContext;
        _stringLocalizer = stringLocalizer;
    }

    public async Task EnrichPickingRowsAsync(List<DispatchlistViewModel> rows, CurrentUser currentUser)
    {
        if (rows.Count == 0)
        {
            return;
        }

        var dispatchNos = rows.Select(t => t.dispatch_no).Distinct().ToList();
        var moves = await _ruoyiDbContext.StockMoves.AsNoTracking()
            .Where(t => !t.deleted && dispatchNos.Contains(t.no))
            .Select(t => new { t.id, t.no, t.dept_name, t.order_user_name, t.create_time })
            .ToListAsync();
        if (moves.Count == 0)
        {
            return;
        }

        var moveIds = moves.Select(t => t.id).ToList();
        var items = await _ruoyiDbContext.StockMoveItems.AsNoTracking()
            .Where(t => !t.deleted && moveIds.Contains(t.stock_move_id))
            .ToListAsync();
        var commodityIds = items.Where(t => t.commodity_id.HasValue)
            .Select(t => t.commodity_id!.Value)
            .Distinct()
            .ToList();
        var skuMap = await _ruoyiDbContext.CommodityMaps.AsNoTracking()
            .Where(t => t.tenant_id == currentUser.tenant_id && commodityIds.Contains(t.erp_commodity_id))
            .ToDictionaryAsync(t => t.erp_commodity_id, t => t.wms_sku_id);
        var movesByNo = moves.GroupBy(t => t.no).ToDictionary(t => t.Key, t => t.ToList());
        var itemsByMove = items.GroupBy(t => t.stock_move_id).ToDictionary(t => t.Key, t => t.ToList());
        var snapshotByItem = items.ToDictionary(t => t.id, ParseSnapshot);

        var snapshotItemIds = snapshotByItem.Values.Select(t => t.fbaShipmentItemId)
            .Where(t => t.HasValue)
            .Select(t => t.GetValueOrDefault())
            .Distinct()
            .ToList();
        var shipmentItemMap = snapshotItemIds.Count > 0
            ? await _ruoyiDbContext.FbaShipmentItems.AsNoTracking()
                .Where(t => !t.deleted && snapshotItemIds.Contains(t.id))
                .ToDictionaryAsync(t => t.id)
            : new Dictionary<long, ModernWMS.Core.DBContext.Entities.ErpFbaShipmentItemEntity>();
        var shipmentIds = snapshotByItem.Values.Select(t => t.fbaShipmentId)
            .Where(t => t.HasValue)
            .Select(t => t.GetValueOrDefault())
            .Concat(shipmentItemMap.Values.Select(t => t.shipment_id))
            .Distinct()
            .ToList();
        var shipmentMap = shipmentIds.Count > 0
            ? await _ruoyiDbContext.FbaShipments.AsNoTracking()
                .Where(t => !t.deleted && shipmentIds.Contains(t.id))
                .ToDictionaryAsync(t => t.id)
            : new Dictionary<long, ModernWMS.Core.DBContext.Entities.ErpFbaShipmentEntity>();

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
            row.shop_name = shipmentId.HasValue && shipmentMap.TryGetValue(shipmentId.Value, out var shipment)
                ? shipment.shop_name ?? string.Empty
                : string.Empty;
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

        var entities = await _wmsDbContext.GetDbSet<DispatchlistEntity>()
            .Where(t => distinctIds.Contains(t.id) && t.tenant_id == currentUser.tenant_id && t.dispatch_status == 2)
            .ToListAsync();
        if (entities.Count != distinctIds.Count)
        {
            return (false, _stringLocalizer["data_changed"]);
        }

        var pickRows = await _wmsDbContext.GetDbSet<DispatchpicklistEntity>()
            .Where(t => distinctIds.Contains(t.dispatchlist_id))
            .ToListAsync();
        var now = DateTime.Now;
        foreach (var entity in entities)
        {
            entity.picked_qty = entity.lock_qty;
            entity.dispatch_status = 3;
            entity.pick_checker = currentUser.user_name;
            entity.pick_checker_id = currentUser.user_id;
            entity.last_update_time = now;
        }
        foreach (var pickRow in pickRows)
        {
            pickRow.picked_qty = pickRow.pick_qty;
            pickRow.last_update_time = now;
        }

        return await SaveAsync();
    }

    public async Task<(bool flag, string msg)> RepickAsync(int id, CurrentUser currentUser)
    {
        var entity = await _wmsDbContext.GetDbSet<DispatchlistEntity>()
            .FirstOrDefaultAsync(t => t.id == id && t.tenant_id == currentUser.tenant_id && t.dispatch_status == 3);
        if (entity == null)
        {
            return (false, _stringLocalizer["data_changed"]);
        }

        var pickRows = await _wmsDbContext.GetDbSet<DispatchpicklistEntity>()
            .Where(t => t.dispatchlist_id == id)
            .ToListAsync();
        var now = DateTime.Now;
        entity.dispatch_status = 2;
        entity.picked_qty = 0;
        entity.pick_checker = string.Empty;
        entity.pick_checker_id = 0;
        entity.last_update_time = now;
        foreach (var pickRow in pickRows)
        {
            pickRow.picked_qty = 0;
            pickRow.last_update_time = now;
        }

        return await SaveAsync();
    }

    public async Task<(bool flag, string msg)> StartWeighingAsync(int id, CurrentUser currentUser)
    {
        var entity = await _wmsDbContext.GetDbSet<DispatchlistEntity>()
            .FirstOrDefaultAsync(t => t.id == id && t.tenant_id == currentUser.tenant_id && t.dispatch_status == 3);
        if (entity == null || entity.picked_qty != entity.qty)
        {
            return (false, _stringLocalizer["data_changed"]);
        }

        var dispatchRows = await _wmsDbContext.GetDbSet<DispatchlistEntity>()
            .Where(t => t.dispatch_no == entity.dispatch_no && t.tenant_id == currentUser.tenant_id)
            .ToListAsync();
        if (dispatchRows.Any(t => t.dispatch_status != 3 || t.picked_qty != t.qty))
        {
            return (false, "该FBA货件还有商品未完成拣货");
        }

        var now = DateTime.Now;
        foreach (var row in dispatchRows)
        {
            row.dispatch_status = 4;
            row.last_update_time = now;
        }
        return await SaveAsync();
    }

    public async Task<(bool flag, string msg)> UndoWeighingAsync(int id, CurrentUser currentUser)
    {
        var entity = await _wmsDbContext.GetDbSet<DispatchlistEntity>()
            .FirstOrDefaultAsync(t => t.id == id
                && t.tenant_id == currentUser.tenant_id
                && (t.dispatch_status == 4 || t.dispatch_status == 5));
        if (entity == null)
        {
            return (false, _stringLocalizer["data_changed"]);
        }

        var rows = await _wmsDbContext.GetDbSet<DispatchlistEntity>()
            .Where(t => t.dispatch_no == entity.dispatch_no
                && t.tenant_id == currentUser.tenant_id
                && (t.dispatch_status == 4 || t.dispatch_status == 5))
            .ToListAsync();
        var measurements = await _wmsDbContext.GetDbSet<DispatchWeighingBoxEntity>()
            .Where(t => t.dispatch_no == entity.dispatch_no && t.tenant_id == currentUser.tenant_id)
            .ToListAsync();
        const byte targetStatus = 3;
        var now = DateTime.Now;
        foreach (var row in rows)
        {
            row.dispatch_status = targetStatus;
            row.weighing_no = string.Empty;
            row.weighing_qty = 0;
            row.weighing_weight = 0;
            row.weighing_length = 0;
            row.weighing_width = 0;
            row.weighing_height = 0;
            row.weighing_volume = 0;
            row.weighing_person = string.Empty;
            row.last_update_time = now;
        }
        _wmsDbContext.GetDbSet<DispatchWeighingBoxEntity>().RemoveRange(measurements);
        return await SaveAsync();
    }

    public async Task<(List<DispatchWeighingShipmentViewModel> data, int totals)> GetWeighingShipmentsAsync(
        PageSearch pageSearch,
        CurrentUser currentUser)
    {
        var dispatchRows = await _wmsDbContext.GetDbSet<DispatchlistEntity>().AsNoTracking()
            .Where(t => t.tenant_id == currentUser.tenant_id && (t.dispatch_status == 4 || t.dispatch_status == 5))
            .ToListAsync();
        if (dispatchRows.Count == 0)
        {
            return ([], 0);
        }

        var dispatchNos = dispatchRows.Select(t => t.dispatch_no).Distinct().ToList();
        var moves = await _ruoyiDbContext.StockMoves.AsNoTracking()
            .Where(t => !t.deleted && dispatchNos.Contains(t.no))
            .ToListAsync();
        var moveIds = moves.Select(t => t.id).ToList();
        var moveItems = await _ruoyiDbContext.StockMoveItems.AsNoTracking()
            .Where(t => !t.deleted && moveIds.Contains(t.stock_move_id))
            .ToListAsync();
        var commodityIds = moveItems.Where(t => t.commodity_id.HasValue)
            .Select(t => t.commodity_id!.Value).Distinct().ToList();
        var skuMap = await _ruoyiDbContext.CommodityMaps.AsNoTracking()
            .Where(t => t.tenant_id == currentUser.tenant_id && commodityIds.Contains(t.erp_commodity_id))
            .ToDictionaryAsync(t => t.erp_commodity_id, t => t.wms_sku_id);
        var snapshots = moveItems.ToDictionary(t => t.id, ParseSnapshot);
        var shipmentItemIds = snapshots.Values.Where(t => t.fbaShipmentItemId.HasValue)
            .Select(t => t.fbaShipmentItemId!.Value).Distinct().ToList();
        var shipmentItems = await _ruoyiDbContext.FbaShipmentItems.AsNoTracking()
            .Where(t => !t.deleted && shipmentItemIds.Contains(t.id))
            .ToDictionaryAsync(t => t.id);
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
        var shipments = await _ruoyiDbContext.FbaShipments.AsNoTracking()
            .Where(t => !t.deleted && shipmentIds.Contains(t.id))
            .ToDictionaryAsync(t => t.id);
        var boxes = await _ruoyiDbContext.FbaShipmentBoxes.AsNoTracking()
            .Where(t => !t.deleted && shipmentIds.Contains(t.shipment_id))
            .ToListAsync();
        var measured = await _wmsDbContext.GetDbSet<DispatchWeighingBoxEntity>().AsNoTracking()
            .Where(t => t.tenant_id == currentUser.tenant_id && shipmentIds.Contains(t.fba_shipment_id))
            .ToListAsync();

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
        var boxes = await _ruoyiDbContext.FbaShipmentBoxes.AsNoTracking()
            .Where(t => !t.deleted && t.shipment_id == shipmentId)
            .OrderBy(t => t.idx).ThenBy(t => t.box_id)
            .ToListAsync();
        var measured = await _wmsDbContext.GetDbSet<DispatchWeighingBoxEntity>().AsNoTracking()
            .Where(t => t.tenant_id == currentUser.tenant_id && t.dispatch_no == dispatchNo && t.fba_shipment_id == shipmentId)
            .ToDictionaryAsync(t => t.erp_box_id);
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

        var boxes = await _ruoyiDbContext.FbaShipmentBoxes.AsNoTracking()
            .Where(t => !t.deleted && t.shipment_id == shipmentId)
            .OrderBy(t => t.idx).ThenBy(t => t.box_id)
            .ToListAsync();
        var submittedIds = viewModels.Select(t => t.erp_box_id).ToHashSet();
        if (boxes.Count == 0 || boxes.Count != viewModels.Count || boxes.Any(t => !submittedIds.Contains(t.id)))
            return (false, "箱号数据已发生变化，请刷新后重新填写");

        var fbaNo = await _ruoyiDbContext.FbaShipments.AsNoTracking()
            .Where(t => !t.deleted && t.id == shipmentId)
            .Select(t => t.amazon_shipment_id)
            .FirstOrDefaultAsync() ?? string.Empty;
        var valuesByBoxId = viewModels.ToDictionary(t => t.erp_box_id);

        await using var transaction = await _wmsDbContext.Database.BeginTransactionAsync();
        var set = _wmsDbContext.GetDbSet<DispatchWeighingBoxEntity>();
        var existing = await set.Where(t => t.tenant_id == currentUser.tenant_id && submittedIds.Contains(t.erp_box_id))
            .ToDictionaryAsync(t => t.erp_box_id);
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
                set.Add(entity);
            }

            var value = valuesByBoxId[box.id];
            ApplyMeasurement(entity, value.weighing_weight, value.weighing_length, value.weighing_width,
                value.weighing_height, currentUser, now, null);
        }

        await _wmsDbContext.SaveChangesAsync();
        var latestBoxIds = await _ruoyiDbContext.FbaShipmentBoxes.AsNoTracking()
            .Where(t => !t.deleted && t.shipment_id == shipmentId)
            .Select(t => t.id)
            .ToListAsync();
        if (latestBoxIds.Count != submittedIds.Count || latestBoxIds.Any(t => !submittedIds.Contains(t)))
            return (false, "箱号数据已发生变化，请刷新后重新填写");
        await CompleteDispatchIfReadyAsync(dispatchNo, currentUser);
        await transaction.CommitAsync();
        return (true, _stringLocalizer["operation_success"]);
    }

    private async Task<bool> CanAccessShipmentAsync(string dispatchNo, long shipmentId, CurrentUser currentUser)
    {
        var hasDispatch = await _wmsDbContext.GetDbSet<DispatchlistEntity>().AsNoTracking()
            .AnyAsync(t => t.tenant_id == currentUser.tenant_id && t.dispatch_no == dispatchNo
                && (t.dispatch_status == 4 || t.dispatch_status == 5));
        if (!hasDispatch) return false;
        var shipmentIds = await GetShipmentIdsForDispatchAsync(dispatchNo, currentUser);
        return shipmentIds.Contains(shipmentId);
    }

    private async Task CompleteDispatchIfReadyAsync(string dispatchNo, CurrentUser currentUser)
    {
        var shipmentIds = await GetShipmentIdsForDispatchAsync(dispatchNo, currentUser);
        var boxIds = await _ruoyiDbContext.FbaShipmentBoxes.AsNoTracking()
            .Where(t => !t.deleted && shipmentIds.Contains(t.shipment_id)).Select(t => t.id).ToListAsync();
        if (boxIds.Count == 0) return;
        var measurements = await _wmsDbContext.GetDbSet<DispatchWeighingBoxEntity>()
            .Where(t => t.tenant_id == currentUser.tenant_id && t.dispatch_no == dispatchNo && boxIds.Contains(t.erp_box_id))
            .ToListAsync();
        if (measurements.Select(t => t.erp_box_id).Distinct().Count() != boxIds.Distinct().Count()
            || measurements.Any(t => t.weighing_weight <= 0
                || t.weighing_length <= 0
                || t.weighing_width <= 0
                || t.weighing_height <= 0)) return;

        var rows = await _wmsDbContext.GetDbSet<DispatchlistEntity>()
            .Where(t => t.tenant_id == currentUser.tenant_id && t.dispatch_no == dispatchNo
                && (t.dispatch_status == 4 || t.dispatch_status == 5))
            .ToListAsync();
        var now = DateTime.Now;
        var totalWeight = measurements.Sum(t => t.weighing_weight);
        var totalVolume = measurements.Sum(t => t.weighing_volume);
        var firstRowId = rows.Select(t => t.id).DefaultIfEmpty().Min();
        foreach (var row in rows)
        {
            row.dispatch_status = 5;
            row.weighing_no = $"{dispatchNo}-BOX";
            row.weighing_qty = row.picked_qty;
            row.weighing_weight = row.id == firstRowId ? totalWeight : 0;
            row.weighing_length = measurements.Count == 1 ? measurements[0].weighing_length : 0;
            row.weighing_width = measurements.Count == 1 ? measurements[0].weighing_width : 0;
            row.weighing_height = measurements.Count == 1 ? measurements[0].weighing_height : 0;
            row.weighing_volume = row.id == firstRowId ? totalVolume : 0;
            row.weighing_person = currentUser.user_name;
            row.last_update_time = now;
        }
        await _wmsDbContext.SaveChangesAsync();
    }

    private async Task<List<long>> GetShipmentIdsForDispatchAsync(string dispatchNo, CurrentUser currentUser)
    {
        var wmsSkuIds = await _wmsDbContext.GetDbSet<DispatchlistEntity>().AsNoTracking()
            .Where(t => t.tenant_id == currentUser.tenant_id && t.dispatch_no == dispatchNo)
            .Select(t => t.sku_id).Distinct().ToListAsync();
        var moveIds = await _ruoyiDbContext.StockMoves.AsNoTracking()
            .Where(t => !t.deleted && t.no == dispatchNo).Select(t => t.id).ToListAsync();
        var items = await _ruoyiDbContext.StockMoveItems.AsNoTracking()
            .Where(t => !t.deleted && moveIds.Contains(t.stock_move_id)).ToListAsync();
        var commodityIds = items.Where(t => t.commodity_id.HasValue)
            .Select(t => t.commodity_id!.Value).Distinct().ToList();
        var allowedCommodityIds = await _ruoyiDbContext.CommodityMaps.AsNoTracking()
            .Where(t => t.tenant_id == currentUser.tenant_id
                && commodityIds.Contains(t.erp_commodity_id)
                && wmsSkuIds.Contains(t.wms_sku_id))
            .Select(t => t.erp_commodity_id).ToListAsync();
        items = items.Where(t => t.commodity_id.HasValue && allowedCommodityIds.Contains(t.commodity_id.Value)).ToList();
        var snapshots = items.Select(ParseSnapshot).ToList();
        var result = snapshots.Where(t => t.fbaShipmentId.HasValue)
            .Select(t => t.fbaShipmentId!.Value).ToList();
        var shipmentItemIds = snapshots.Where(t => t.fbaShipmentItemId.HasValue)
            .Select(t => t.fbaShipmentItemId!.Value).Distinct().ToList();
        if (shipmentItemIds.Count > 0)
        {
            result.AddRange(await _ruoyiDbContext.FbaShipmentItems.AsNoTracking()
                .Where(t => !t.deleted && shipmentItemIds.Contains(t.id))
                .Select(t => t.shipment_id).ToListAsync());
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

    private async Task<(bool flag, string msg)> SaveAsync()
    {
        var changed = await _wmsDbContext.SaveChangesAsync();
        return changed > 0
            ? (true, _stringLocalizer["operation_success"])
            : (false, _stringLocalizer["operation_failed"]);
    }

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
}
