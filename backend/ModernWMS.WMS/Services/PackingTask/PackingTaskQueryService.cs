using System.Data;
using Dapper;
using Microsoft.Extensions.Configuration;
using ModernWMS.Core.Database;
using ModernWMS.Core.DBContext.Entities;
using ModernWMS.Core.JWT;
using ModernWMS.Core.Models;
using ModernWMS.WMS.Entities.ViewModels;
using ModernWMS.WMS.Entities.ViewModels.PackingTask;
using ModernWMS.WMS.IServices;

namespace ModernWMS.WMS.Services;

internal sealed record PackingTaskPageRequest(
    string Keyword,
    long? WarehouseId,
    int Offset,
    int PageSize,
    long TenantId);

internal sealed record PackingTaskStockAvailability(string SkuCode, int Qty);

internal sealed record PackingTaskPageData(
    List<ErpPackingTaskEntity> Tasks,
    List<ErpPackingTaskItemEntity> Items,
    IReadOnlyDictionary<long, PackingTaskStockAvailability> AvailabilityByItemId,
    int Totals);

internal sealed record PackingTaskSelectableData(
    List<SelectableStockViewModel> Rows,
    int WarehouseId,
    string WarehouseName);

internal sealed record PackingTaskStockSaveResult(bool IsSuccess, string Message);

/// <summary>Testable query boundary implemented with Dapper in production.</summary>
internal interface IPackingTaskQueryDataSource
{
    Task<PackingTaskPageData> LoadPageAsync(PackingTaskPageRequest request);

    Task<PackingTaskSelectableData?> LoadSelectableStockAsync(
        PackingTaskStockPageRequest request,
        CurrentUser currentUser);

    Task<PackingTaskStockSaveResult> SaveSelectionAsync(
        PackingTaskStockSelectRequest request,
        CurrentUser currentUser);
}

/// <summary>Reads formal packing-task snapshots without creating dispatch business facts.</summary>
public class PackingTaskQueryService : IPackingTaskQueryService
{
    private readonly IPackingTaskQueryDataSource _dataSource;
    private readonly IConfiguration _configuration;
    private readonly IWarehouseAccessService? _warehouseAccessService;

    public PackingTaskQueryService(
        IMySqlConnectionFactory connectionFactory,
        IConfiguration configuration,
        IWarehouseAccessService warehouseAccessService)
        : this(new DapperPackingTaskQueryDataSource(connectionFactory), configuration, warehouseAccessService)
    {
    }

    internal PackingTaskQueryService(
        IPackingTaskQueryDataSource dataSource,
        IConfiguration configuration,
        IWarehouseAccessService? warehouseAccessService = null)
    {
        _dataSource = dataSource ?? throw new ArgumentNullException(nameof(dataSource));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _warehouseAccessService = warehouseAccessService;
    }

    public async Task<PackingTaskQueryResult> PageAsync(PageSearch pageSearch, CurrentUser currentUser)
    {
        if (!_configuration.GetValue("Features:PackingTaskFirstStep", false))
        {
            return Failure("装箱任务功能未启用");
        }

        var warehouseText = FindSearchText(pageSearch, "warehouse_id");
        long? warehouseId = long.TryParse(warehouseText, out var parsedWarehouseId) && parsedWarehouseId > 0
            ? parsedWarehouseId
            : null;
        if (_warehouseAccessService != null)
        {
            if (warehouseId == null)
            {
                warehouseId = (await _warehouseAccessService.GetAllowedAsync(currentUser)).default_warehouse_id;
                if (warehouseId == null)
                {
                    return new PackingTaskQueryResult(true, string.Empty, [], 0);
                }
            }
            else
            {
                await _warehouseAccessService.EnsureAllowedAsync(warehouseId.Value, currentUser);
            }
        }

        var pageIndex = Math.Max(pageSearch.pageIndex, 1);
        var pageSize = Math.Clamp(pageSearch.pageSize, 1, 200);
        var page = await _dataSource.LoadPageAsync(new PackingTaskPageRequest(
            FindSearchText(pageSearch, "keyword"),
            warehouseId,
            (pageIndex - 1) * pageSize,
            pageSize,
            currentUser.tenant_id));
        var itemsByTask = page.Items.GroupBy(t => t.sellfox_task_id)
            .ToDictionary(t => t.Key, t => t.ToList());
        var data = page.Tasks.Select(task => new PackingTaskQueryViewModel
        {
            id = task.id,
            sellfox_task_id = task.sellfox_task_id,
            packing_task_sn = task.packing_task_sn,
            warehouse_id = task.warehouse_id,
            warehouse_name = task.warehouse_name,
            complete_num = task.complete_num,
            task_num = task.task_num,
            create_name = task.create_name,
            source_create_time = task.source_create_time,
            item_count = task.item_count,
            shop_name = task.shop_name,
            marketplace_name = task.marketplace_name,
            item_list = (itemsByTask.GetValueOrDefault(task.sellfox_task_id) ?? [])
                .Select(item => BuildItemViewModel(item, page.AvailabilityByItemId))
                .ToList()
        }).ToList();

        return new PackingTaskQueryResult(true, string.Empty, data, page.Totals);
    }

    public async Task<(List<SelectableStockViewModel> data, int totals)> SelectableStockPageAsync(
        PackingTaskStockPageRequest request,
        CurrentUser currentUser)
    {
        var loaded = await _dataSource.LoadSelectableStockAsync(request, currentUser);
        if (loaded == null)
        {
            return ([], 0);
        }

        foreach (var row in loaded.Rows)
        {
            row.warehouse_id = loaded.WarehouseId;
            row.warehouse_name = loaded.WarehouseName;
        }

        var ordered = loaded.Rows.OrderByDescending(t => t.matched)
            .ThenByDescending(t => t.available_qty)
            .ThenBy(t => t.sku_code)
            .ToList();
        var totals = ordered.Count;
        var pageIndex = Math.Max(request.page_index, 1);
        var pageSize = Math.Clamp(request.page_size, 1, 200);
        return (ordered.Skip((pageIndex - 1) * pageSize).Take(pageSize).ToList(), totals);
    }

    public async Task<(bool flag, string message)> SelectStockAsync(
        PackingTaskStockSelectRequest request,
        CurrentUser currentUser)
    {
        if (request.qty <= 0)
        {
            return (false, "选择数量必须大于0");
        }

        var result = await _dataSource.SaveSelectionAsync(request, currentUser);
        return (result.IsSuccess, result.Message);
    }

    private static PackingTaskQueryItemViewModel BuildItemViewModel(
        ErpPackingTaskItemEntity item,
        IReadOnlyDictionary<long, PackingTaskStockAvailability> availabilityByItemId)
    {
        var availability = availabilityByItemId.GetValueOrDefault(item.id);
        return new PackingTaskQueryItemViewModel
        {
            id = item.id,
            sellfox_item_id = item.sellfox_item_id,
            commodity_id = item.commodity_id,
            commodity_sku = item.commodity_sku,
            commodity_name = item.commodity_name,
            main_image = item.main_image,
            fn_sku = item.fn_sku,
            sku = item.sku,
            msku = item.msku,
            task_num = item.task_num,
            quantity_shipped = item.quantity_shipped,
            stock_available = item.stock_available,
            stock_sku_code = availability?.SkuCode,
            stock_available_qty = availability?.Qty
        };
    }

    private static PackingTaskQueryResult Failure(string message) => new(false, message, [], 0);

    private static string FindSearchText(PageSearch pageSearch, string name) =>
        pageSearch.searchObjects.FirstOrDefault(t =>
            string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase))?.Text?.Trim() ?? string.Empty;

    private sealed class DapperPackingTaskQueryDataSource(IMySqlConnectionFactory connectionFactory)
        : IPackingTaskQueryDataSource
    {
        private readonly IMySqlConnectionFactory _connectionFactory = connectionFactory
            ?? throw new ArgumentNullException(nameof(connectionFactory));

        public async Task<PackingTaskPageData> LoadPageAsync(PackingTaskPageRequest request)
        {
            const string whereSql = """
                FROM ruiyi_sellfox_packing_task AS task
                WHERE task.source_deleted = 0
                  AND task.source_canceled = 0
                  AND (@WarehouseId IS NULL OR task.warehouse_id = @WarehouseId)
                  AND NOT EXISTS (
                    SELECT 1 FROM wms_dispatch_packing_task AS active_task
                    WHERE active_task.active_source_task_id = task.sellfox_task_id)
                  AND (@HasKeyword = 0
                    OR LOCATE(@Keyword, task.packing_task_sn) > 0
                    OR EXISTS (
                      SELECT 1 FROM ruiyi_sellfox_packing_task_item AS search_item
                      WHERE search_item.sellfox_task_id = task.sellfox_task_id
                        AND search_item.source_deleted = 0
                        AND (LOCATE(@Keyword, search_item.commodity_name) > 0
                          OR LOCATE(@Keyword, search_item.commodity_sku) > 0
                          OR LOCATE(@Keyword, search_item.sku) > 0
                          OR LOCATE(@Keyword, search_item.fn_sku) > 0
                          OR LOCATE(@Keyword, search_item.msku) > 0)))
                """;
            var parameters = new
            {
                HasKeyword = string.IsNullOrEmpty(request.Keyword) ? 0 : 1,
                request.Keyword,
                request.WarehouseId,
                request.Offset,
                request.PageSize,
                request.TenantId
            };
            await using var connection = await _connectionFactory.OpenConnectionAsync();
            var totals = await connection.QuerySingleAsync<int>(
                $"SELECT COUNT(*) {whereSql}", parameters);
            var tasks = (await connection.QueryAsync<ErpPackingTaskEntity>($"""
                SELECT task.id, task.sellfox_task_id, task.packing_task_sn, task.warehouse_id,
                       task.warehouse_name, task.complete_num, task.task_num, task.create_name,
                       task.source_create_time, task.item_count, task.shop_name, task.marketplace_name
                {whereSql}
                ORDER BY task.source_create_time DESC, task.id DESC
                LIMIT @PageSize OFFSET @Offset
                """, parameters)).AsList();
            if (tasks.Count == 0)
            {
                return new PackingTaskPageData([], [],
                    new Dictionary<long, PackingTaskStockAvailability>(), totals);
            }

            var taskIds = tasks.Select(t => t.sellfox_task_id).ToArray();
            var items = (await connection.QueryAsync<ErpPackingTaskItemEntity>("""
                SELECT id, sellfox_item_id, sellfox_task_id, commodity_id, commodity_sku,
                       commodity_name, main_image, fn_sku, sku, msku, task_num,
                       quantity_shipped, stock_available
                FROM ruiyi_sellfox_packing_task_item
                WHERE source_deleted = 0 AND sellfox_task_id IN @TaskIds
                ORDER BY id
                """, new { TaskIds = taskIds })).AsList();
            var availabilityRows = (await connection.QueryAsync<AvailabilityRow>("""
                SELECT item.id AS ItemId,
                       mapped_sku.sku_code AS SkuCode,
                       COALESCE(SUM(CASE WHEN stock.is_freeze = 0 THEN stock.qty ELSE 0 END), 0) AS Qty
                FROM ruiyi_sellfox_packing_task_item AS item
                INNER JOIN wms_erp_commodity_map AS commodity_map
                  ON commodity_map.erp_commodity_id = item.commodity_id
                 AND commodity_map.tenant_id = @TenantId
                 AND commodity_map.wms_sku_id > 0
                INNER JOIN wms_sku AS mapped_sku ON mapped_sku.id = commodity_map.wms_sku_id
                INNER JOIN wms_sku AS variant_sku ON variant_sku.spu_id = mapped_sku.spu_id
                LEFT JOIN wms_stock AS stock
                  ON stock.sku_id = variant_sku.id AND stock.tenant_id = @TenantId
                WHERE item.source_deleted = 0 AND item.sellfox_task_id IN @TaskIds
                GROUP BY item.id, mapped_sku.sku_code
                """, new { TaskIds = taskIds, request.TenantId })).AsList();
            var availability = availabilityRows.ToDictionary(
                t => t.ItemId,
                t => new PackingTaskStockAvailability(StripVariantSuffix(t.SkuCode), t.Qty));
            return new PackingTaskPageData(tasks, items, availability, totals);
        }

        public async Task<PackingTaskSelectableData?> LoadSelectableStockAsync(
            PackingTaskStockPageRequest request,
            CurrentUser currentUser)
        {
            await using var connection = await _connectionFactory.OpenConnectionAsync();
            var context = await connection.QuerySingleOrDefaultAsync<SelectableContext>("""
                SELECT warehouse.id AS WarehouseId, warehouse.warehouse_name AS WarehouseName,
                       commodity_map.wms_sku_id AS MappedSkuId, mapped_sku.sku_code AS MappedSkuCode
                FROM ruiyi_sellfox_packing_task_item AS item
                INNER JOIN ruiyi_sellfox_packing_task AS task
                  ON task.sellfox_task_id = item.sellfox_task_id
                 AND task.source_deleted = 0 AND task.source_canceled = 0
                INNER JOIN wms_warehouse AS warehouse
                  ON warehouse.erp_warehouse_id = task.warehouse_id AND warehouse.is_valid = 1
                LEFT JOIN wms_erp_commodity_map AS commodity_map
                  ON commodity_map.erp_commodity_id = item.commodity_id
                 AND commodity_map.tenant_id = @TenantId
                LEFT JOIN wms_sku AS mapped_sku ON mapped_sku.id = commodity_map.wms_sku_id
                WHERE item.sellfox_task_id = @TaskId AND item.sellfox_item_id = @ItemId
                  AND item.source_deleted = 0
                LIMIT 1
                """, new
            {
                TenantId = currentUser.tenant_id,
                TaskId = request.sellfox_task_id,
                ItemId = request.sellfox_item_id
            });
            if (context == null)
            {
                return null;
            }

            var rows = (await connection.QueryAsync<SelectableRow>("""
                SELECT stock.id AS stock_id, stock.sku_id, sku.sku_code, spu.spu_code,
                       spu.spu_name AS commodity_name,
                       COALESCE((
                         SELECT commodity.img_url
                         FROM wms_erp_commodity_map AS image_map
                         INNER JOIN erp_commodity AS commodity
                           ON commodity.id = CAST(image_map.erp_commodity_id AS CHAR)
                         WHERE image_map.wms_sku_id = stock.sku_id
                           AND image_map.tenant_id = @TenantId
                           AND commodity.img_url <> ''
                         ORDER BY image_map.id
                         LIMIT 1), '') AS main_image,
                       stock.goods_location_id, location.location_name,
                       stock.goods_owner_id, COALESCE(owner.goods_owner_name, '') AS goods_owner_name,
                       stock.qty, stock.is_freeze, stock.series_number, stock.expiry_date,
                       COALESCE(selection.selected_qty, 0) AS selected_qty,
                       COALESCE(dispatch_lock.lock_qty, 0) + COALESCE(process_lock.lock_qty, 0)
                         + COALESCE(move_lock.lock_qty, 0) AS locked_qty
                FROM wms_stock AS stock
                INNER JOIN wms_sku AS sku ON sku.id = stock.sku_id
                INNER JOIN wms_spu AS spu ON spu.id = sku.spu_id
                INNER JOIN wms_goodslocation AS location
                  ON location.id = stock.goods_location_id
                 AND location.warehouse_id = @WarehouseId
                 AND location.is_valid = 1 AND location.warehouse_area_property <> 5
                LEFT JOIN wms_goodsowner AS owner ON owner.id = stock.goods_owner_id
                LEFT JOIN (
                  SELECT stock_id, SUM(qty) AS selected_qty
                  FROM wms_packing_task_stock_selection
                  WHERE tenant_id = @TenantId AND sellfox_task_id = @TaskId AND sellfox_item_id = @ItemId
                  GROUP BY stock_id) AS selection ON selection.stock_id = stock.id
                LEFT JOIN (
                  SELECT pick.sku_id, pick.goods_location_id, SUM(pick.pick_qty) AS lock_qty
                  FROM wms_dispatchpicklist AS pick
                  INNER JOIN wms_dispatchlist AS detail ON detail.id = pick.dispatchlist_id
                  WHERE detail.dispatch_status > 1 AND detail.dispatch_status < 6
                  GROUP BY pick.sku_id, pick.goods_location_id) AS dispatch_lock
                  ON dispatch_lock.sku_id = stock.sku_id
                 AND dispatch_lock.goods_location_id = stock.goods_location_id
                LEFT JOIN (
                  SELECT sku_id, goods_location_id, SUM(qty) AS lock_qty
                  FROM wms_stockprocessdetail WHERE is_update_stock = 0
                  GROUP BY sku_id, goods_location_id) AS process_lock
                  ON process_lock.sku_id = stock.sku_id
                 AND process_lock.goods_location_id = stock.goods_location_id
                LEFT JOIN (
                  SELECT sku_id, orig_goods_location_id, SUM(qty) AS lock_qty
                  FROM wms_stockmove WHERE move_status = 0
                  GROUP BY sku_id, orig_goods_location_id) AS move_lock
                  ON move_lock.sku_id = stock.sku_id
                 AND move_lock.orig_goods_location_id = stock.goods_location_id
                WHERE stock.tenant_id = @TenantId
                """, new
            {
                TenantId = currentUser.tenant_id,
                TaskId = request.sellfox_task_id,
                ItemId = request.sellfox_item_id,
                context.WarehouseId
            })).AsList();
            var baseSkuCode = StripVariantSuffix(context.MappedSkuCode ?? string.Empty);
            var resultRows = new List<SelectableStockViewModel>();
            foreach (var row in rows)
            {
                var available = row.is_freeze ? 0 : Math.Max(0, row.qty - row.locked_qty - row.selected_qty);
                var selected = row.selected_qty > 0;
                if (!selected && available <= 0)
                {
                    continue;
                }

                resultRows.Add(new SelectableStockViewModel
                {
                    stock_id = row.stock_id,
                    sku_id = row.sku_id,
                    sku_code = row.sku_code,
                    spu_code = row.spu_code,
                    commodity_name = row.commodity_name,
                    main_image = row.main_image,
                    goods_location_id = row.goods_location_id,
                    location_name = row.location_name,
                    goods_owner_id = row.goods_owner_id,
                    goods_owner_name = row.goods_owner_name,
                    qty = row.qty,
                    available_qty = available,
                    series_number = row.series_number,
                    expiry_date = row.expiry_date,
                    matched = context.MappedSkuId is > 0
                        && (row.sku_id == context.MappedSkuId
                            || (!string.IsNullOrEmpty(baseSkuCode)
                                && string.Equals(StripVariantSuffix(row.sku_code), baseSkuCode,
                                    StringComparison.OrdinalIgnoreCase))),
                    selected = selected
                });
            }

            return new PackingTaskSelectableData(resultRows, context.WarehouseId, context.WarehouseName);
        }

        public async Task<PackingTaskStockSaveResult> SaveSelectionAsync(
            PackingTaskStockSelectRequest request,
            CurrentUser currentUser)
        {
            await using var connection = await _connectionFactory.OpenConnectionAsync();
            await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted);
            try
            {
                var itemExists = await connection.ExecuteScalarAsync<bool>("""
                    SELECT EXISTS(
                      SELECT 1 FROM ruiyi_sellfox_packing_task_item
                      WHERE sellfox_item_id = @ItemId AND sellfox_task_id = @TaskId AND source_deleted = 0)
                    """, new { ItemId = request.sellfox_item_id, TaskId = request.sellfox_task_id }, transaction);
                if (!itemExists)
                {
                    return await RollbackResultAsync(transaction, "装箱任务明细不存在");
                }

                var stock = await connection.QuerySingleOrDefaultAsync<SelectionStockRow>("""
                    SELECT stock.id, stock.sku_id, stock.goods_location_id, stock.goods_owner_id,
                           stock.is_freeze, sku.sku_code
                    FROM wms_stock AS stock
                    LEFT JOIN wms_sku AS sku ON sku.id = stock.sku_id
                    WHERE stock.id = @StockId AND stock.tenant_id = @TenantId
                    FOR UPDATE
                    """, new { StockId = request.stock_id, TenantId = currentUser.tenant_id }, transaction);
                if (stock == null)
                {
                    return await RollbackResultAsync(transaction, "库存不存在");
                }
                if (stock.is_freeze)
                {
                    return await RollbackResultAsync(transaction, "该库存已冻结，不能选择");
                }

                var existingId = await connection.QuerySingleOrDefaultAsync<int?>("""
                    SELECT id FROM wms_packing_task_stock_selection
                    WHERE tenant_id = @TenantId AND sellfox_task_id = @TaskId
                      AND sellfox_item_id = @ItemId AND stock_id = @StockId
                    ORDER BY id LIMIT 1 FOR UPDATE
                    """, new
                {
                    TenantId = currentUser.tenant_id,
                    TaskId = request.sellfox_task_id,
                    ItemId = request.sellfox_item_id,
                    StockId = request.stock_id
                }, transaction);
                var values = new
                {
                    Id = existingId,
                    TenantId = currentUser.tenant_id,
                    TaskId = request.sellfox_task_id,
                    ItemId = request.sellfox_item_id,
                    WmsSkuId = stock.sku_id,
                    StockId = request.stock_id,
                    request.qty,
                    stock.goods_location_id,
                    stock.goods_owner_id,
                    SkuCode = stock.sku_code ?? string.Empty,
                    SelectedBy = currentUser.user_id,
                    SelectedByName = currentUser.user_name ?? string.Empty,
                    Now = DateTime.Now
                };
                if (existingId == null)
                {
                    await connection.ExecuteAsync("""
                        INSERT INTO wms_packing_task_stock_selection
                          (tenant_id, sellfox_task_id, sellfox_item_id, wms_sku_id, stock_id, qty,
                           goods_location_id, goods_owner_id, sku_code, selected_by, selected_by_name,
                           create_time, last_update_time)
                        VALUES
                          (@TenantId, @TaskId, @ItemId, @WmsSkuId, @StockId, @qty,
                           @goods_location_id, @goods_owner_id, @SkuCode, @SelectedBy, @SelectedByName,
                           @Now, @Now)
                        """, values, transaction);
                }
                else
                {
                    await connection.ExecuteAsync("""
                        UPDATE wms_packing_task_stock_selection
                        SET wms_sku_id = @WmsSkuId, qty = @qty,
                            goods_location_id = @goods_location_id, goods_owner_id = @goods_owner_id,
                            sku_code = @SkuCode, selected_by = @SelectedBy,
                            selected_by_name = @SelectedByName, last_update_time = @Now
                        WHERE id = @Id
                        """, values, transaction);
                }

                await transaction.CommitAsync();
                return new PackingTaskStockSaveResult(true, "库存选择成功");
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }

        private static async Task<PackingTaskStockSaveResult> RollbackResultAsync(
            System.Data.Common.DbTransaction transaction,
            string message)
        {
            await transaction.RollbackAsync();
            return new PackingTaskStockSaveResult(false, message);
        }

        private static string StripVariantSuffix(string skuCode)
        {
            if (string.IsNullOrWhiteSpace(skuCode))
            {
                return skuCode;
            }

            var dashIndex = skuCode.LastIndexOf('-');
            return dashIndex > 0 && dashIndex < skuCode.Length - 1
                && skuCode[(dashIndex + 1)..].All(char.IsDigit)
                ? skuCode[..dashIndex]
                : skuCode;
        }

        private sealed class AvailabilityRow
        {
            public long ItemId { get; init; }
            public string SkuCode { get; init; } = string.Empty;
            public int Qty { get; init; }
        }

        private sealed class SelectableContext
        {
            public int WarehouseId { get; init; }
            public string WarehouseName { get; init; } = string.Empty;
            public int? MappedSkuId { get; init; }
            public string? MappedSkuCode { get; init; }
        }

        private sealed class SelectableRow
        {
            public int stock_id { get; init; }
            public int sku_id { get; init; }
            public string sku_code { get; init; } = string.Empty;
            public string spu_code { get; init; } = string.Empty;
            public string commodity_name { get; init; } = string.Empty;
            public string main_image { get; init; } = string.Empty;
            public int goods_location_id { get; init; }
            public string location_name { get; init; } = string.Empty;
            public int goods_owner_id { get; init; }
            public string goods_owner_name { get; init; } = string.Empty;
            public int qty { get; init; }
            public bool is_freeze { get; init; }
            public string series_number { get; init; } = string.Empty;
            public DateTime? expiry_date { get; init; }
            public int selected_qty { get; init; }
            public int locked_qty { get; init; }
        }

        private sealed class SelectionStockRow
        {
            public int sku_id { get; init; }
            public int goods_location_id { get; init; }
            public int goods_owner_id { get; init; }
            public bool is_freeze { get; init; }
            public string? sku_code { get; init; }
        }
    }
}
