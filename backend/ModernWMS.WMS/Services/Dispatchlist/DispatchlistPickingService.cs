using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using ModernWMS.Core.DBContext;
using ModernWMS.Core.JWT;
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
        var moveByNo = moves.ToDictionary(t => t.no);
        var itemsByMove = items.GroupBy(t => t.stock_move_id).ToDictionary(t => t.Key, t => t.ToList());

        var snapshotItemIds = items.Select(t => ParseSnapshot(t).fbaShipmentItemId)
            .Where(t => t.HasValue)
            .Select(t => t.GetValueOrDefault())
            .Distinct()
            .ToList();
        var shipmentItemMap = snapshotItemIds.Count > 0
            ? await _ruoyiDbContext.FbaShipmentItems.AsNoTracking()
                .Where(t => !t.deleted && snapshotItemIds.Contains(t.id))
                .ToDictionaryAsync(t => t.id)
            : new Dictionary<long, ModernWMS.Core.DBContext.Entities.ErpFbaShipmentItemEntity>();

        foreach (var row in rows)
        {
            if (!moveByNo.TryGetValue(row.dispatch_no, out var move))
            {
                continue;
            }

            row.dept_name = move.dept_name ?? string.Empty;
            row.order_user_name = move.order_user_name ?? string.Empty;
            row.prepared_time = move.create_time;

            var sourceItems = (itemsByMove.GetValueOrDefault(move.id) ?? [])
                .Where(t => t.commodity_id.HasValue
                    && skuMap.TryGetValue(t.commodity_id.Value, out var wmsSkuId)
                    && wmsSkuId == row.sku_id)
                .ToList();
            var snapshots = sourceItems.Select(ParseSnapshot).ToList();
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

        entity.dispatch_status = 4;
        entity.last_update_time = DateTime.Now;
        return await SaveAsync();
    }

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
        public long? fbaShipmentItemId { get; init; }
        public DateTime? preparedTime { get; init; }
    }
}
