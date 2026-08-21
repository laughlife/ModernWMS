using System.Data;
using Dapper;
using Microsoft.Extensions.Localization;
using ModernWMS.Core.Database;
using ModernWMS.Core.JWT;
using ModernWMS.Core.Models;
using ModernWMS.Core.Services;
using ModernWMS.WMS.Entities.Models;
using ModernWMS.WMS.Entities.ViewModels;
using ModernWMS.WMS.IServices;
using ModernWMS.WMS.IServices.StockAllocation;
using MySqlConnector;

namespace ModernWMS.WMS.Services;

/// <summary>Stock adjustment service.</summary>
public class StockadjustService : BaseService<StockadjustEntity>, IStockadjustService
{
    private const string EntityColumns = """
        a.`id`,a.`job_code`,a.`sku_id`,a.`goods_owner_id`,a.`goods_location_id`,a.`qty`,a.`creator`,
        a.`create_time`,a.`last_update_time`,a.`tenant_id`,a.`is_update_stock`,a.`job_type`,a.`source_table_id`,
        a.`erp_stock_id`,a.`stock_allocation_id`,a.`series_number`,a.`expiry_date`,a.`price`,a.`putaway_date`
        """;

    private const string PageSelect = """
        SELECT a.`id`,a.`job_code`,a.`is_update_stock`,a.`job_type`,a.`qty`,a.`source_table_id`,a.`tenant_id`,
               sku.`id` sku_id,sku.`sku_code`,sku.`sku_name`,spu.`spu_code`,spu.`spu_name`,
               a.`goods_location_id`,gl.`warehouse_name`,gl.`location_name`,a.`goods_owner_id`,
               COALESCE(go.`goods_owner_name`,'') goods_owner_name,a.`creator`,a.`create_time`,a.`last_update_time`,
               a.`erp_stock_id`,a.`stock_allocation_id`,a.`series_number`,a.`expiry_date`,a.`price`,a.`putaway_date`
        FROM `wms_stockadjust` a
        JOIN `wms_sku` sku ON sku.`id`=a.`sku_id`
        JOIN `wms_spu` spu ON spu.`id`=sku.`spu_id`
        JOIN `wms_goodslocation` gl ON gl.`id`=a.`goods_location_id`
        LEFT JOIN `wms_goodsowner` go ON go.`id`=a.`goods_owner_id`
        WHERE a.`tenant_id`=@tenantId
        """;

    private static readonly IReadOnlyDictionary<string, string> SearchColumns = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["id"]="q.`id`", ["job_code"]="q.`job_code`", ["is_update_stock"]="q.`is_update_stock`",
        ["job_type"]="q.`job_type`", ["qty"]="q.`qty`", ["source_table_id"]="q.`source_table_id`",
        ["tenant_id"]="q.`tenant_id`", ["sku_id"]="q.`sku_id`", ["sku_code"]="q.`sku_code`",
        ["sku_name"]="q.`sku_name`", ["spu_code"]="q.`spu_code`", ["spu_name"]="q.`spu_name`",
        ["goods_location_id"]="q.`goods_location_id`", ["warehouse_name"]="q.`warehouse_name`",
        ["location_name"]="q.`location_name`", ["goods_owner_id"]="q.`goods_owner_id`",
        ["goods_owner_name"]="q.`goods_owner_name`", ["creator"]="q.`creator`", ["create_time"]="q.`create_time`",
        ["last_update_time"]="q.`last_update_time`", ["series_number"]="q.`series_number`",
        ["expiry_date"]="q.`expiry_date`", ["price"]="q.`price`", ["putaway_date"]="q.`putaway_date`"
    };

    private readonly IMySqlConnectionFactory _connectionFactory;
    private readonly IStringLocalizer<ModernWMS.Core.MultiLanguage> _stringLocalizer;
    private readonly IStockAllocationMutationService _stockMutationService;

    /// <summary>初始化库存调整服务。</summary>
    public StockadjustService(IMySqlConnectionFactory connectionFactory,
        IStringLocalizer<ModernWMS.Core.MultiLanguage> stringLocalizer,
        IStockAllocationMutationService stockMutationService)
    {
        _connectionFactory = connectionFactory;
        _stringLocalizer = stringLocalizer;
        _stockMutationService = stockMutationService;
    }

    /// <inheritdoc />
    public async Task<(List<StockadjustViewModel> data, int totals)> PageAsync(PageSearch pageSearch, CurrentUser currentUser)
    {
        var filter = DapperSearchBuilder.Build(pageSearch.searchObjects, SearchColumns);
        filter.Parameters.Add("tenantId", currentUser.tenant_id);
        filter.Parameters.Add("offset", (pageSearch.pageIndex - 1) * pageSearch.pageSize);
        filter.Parameters.Add("pageSize", pageSearch.pageSize);
        var where = filter.Sql.Length == 0 ? "1=1" : filter.Sql;

        await using var connection = await _connectionFactory.OpenConnectionAsync();
        using var result = await connection.QueryMultipleAsync($"""
            SELECT COUNT(*) FROM ({PageSelect}) q WHERE {where};
            SELECT q.* FROM ({PageSelect}) q WHERE {where}
            ORDER BY q.`create_time` DESC LIMIT @pageSize OFFSET @offset;
            """, filter.Parameters);
        var totals = await result.ReadSingleAsync<int>();
        return ((await result.ReadAsync<StockadjustViewModel>()).AsList(), totals);
    }

    /// <inheritdoc />
    public async Task<List<StockadjustViewModel>> GetAllAsync(CurrentUser currentUser)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync();
        return (await connection.QueryAsync<StockadjustViewModel>($"""
            SELECT {EntityColumns} FROM `wms_stockadjust` a WHERE a.`tenant_id`=@tenantId;
            """, new { tenantId = currentUser.tenant_id })).AsList();
    }

    /// <inheritdoc />
    public async Task<StockadjustViewModel> GetAsync(int id)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync();
        return await connection.QuerySingleOrDefaultAsync<StockadjustViewModel>($"""
            SELECT {EntityColumns} FROM `wms_stockadjust` a WHERE a.`id`=@id LIMIT 1;
            """, new { id });
    }

    /// <inheritdoc />
    public async Task<(int id, string msg)> AddAsync(StockadjustViewModel viewModel, CurrentUser currentUser)
    {
        var now = DateTime.Now;
        await using var connection = await _connectionFactory.OpenConnectionAsync();
        var routeSnapshot = await CanonicalInventorySupport.GetRouteAsync(
            connection, currentUser.tenant_id, viewModel.goods_location_id);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.Serializable);
        var route = await CanonicalInventorySupport.LockRouteAsync(
            connection, transaction, currentUser.tenant_id, routeSnapshot);
        CanonicalInventorySupport.CanonicalAllocation? allocation = null;
        if (route.Mode == CanonicalInventorySupport.CanonicalMode)
            allocation = await CanonicalInventorySupport.ResolveAllocationAsync(
                connection, transaction, currentUser.tenant_id, viewModel.sku_id,
                viewModel.goods_location_id, viewModel.goods_owner_id, viewModel.series_number,
                viewModel.expiry_date, viewModel.price, viewModel.putaway_date);
        var id = await connection.ExecuteScalarAsync<int>("""
            INSERT INTO `wms_stockadjust`
              (`job_code`,`sku_id`,`goods_owner_id`,`goods_location_id`,`qty`,`creator`,`create_time`,`last_update_time`,
               `tenant_id`,`is_update_stock`,`job_type`,`source_table_id`,`erp_stock_id`,`stock_allocation_id`,
               `series_number`,`expiry_date`,`price`,`putaway_date`)
            VALUES
              (@job_code,@sku_id,@goods_owner_id,@goods_location_id,@qty,@creator,@now,@now,
               @tenantId,@is_update_stock,@job_type,@source_table_id,@erpStockId,@allocationId,
               @series_number,@expiry_date,@price,@putaway_date);
            SELECT LAST_INSERT_ID();
            """, new
        {
            viewModel.job_code, viewModel.sku_id, viewModel.goods_owner_id, viewModel.goods_location_id,
            viewModel.qty, creator = currentUser.user_name, now, tenantId = currentUser.tenant_id,
            viewModel.is_update_stock, viewModel.job_type, viewModel.source_table_id, viewModel.series_number,
            viewModel.expiry_date, viewModel.price, viewModel.putaway_date,
            erpStockId = allocation?.ErpStockId, allocationId = allocation?.AllocationId
        }, transaction);
        await transaction.CommitAsync();
        return id > 0 ? (id, _stringLocalizer["save_success"]) : (0, _stringLocalizer["save_failed"]);
    }

    /// <inheritdoc />
    public async Task<(bool flag, string msg)> UpdateAsync(StockadjustViewModel viewModel)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.Serializable);
        var existing = await connection.QuerySingleOrDefaultAsync<StockadjustEntity>(
            "SELECT * FROM `wms_stockadjust` WHERE `id`=@id FOR UPDATE;", new { viewModel.id }, transaction);
        if (existing == null)
        {
            await transaction.RollbackAsync();
            return (false, _stringLocalizer["not_exists_entity"]);
        }
        await using var routeConnection = await _connectionFactory.OpenConnectionAsync();
        var routeSnapshot = await CanonicalInventorySupport.GetRouteAsync(
            routeConnection, existing.tenant_id, viewModel.goods_location_id);
        var route = await CanonicalInventorySupport.LockRouteAsync(
            connection, transaction, existing.tenant_id, routeSnapshot);
        CanonicalInventorySupport.CanonicalAllocation? allocation = null;
        if (route.Mode == CanonicalInventorySupport.CanonicalMode)
            allocation = await CanonicalInventorySupport.ResolveAllocationAsync(
                connection, transaction, existing.tenant_id, viewModel.sku_id,
                viewModel.goods_location_id, viewModel.goods_owner_id, viewModel.series_number,
                viewModel.expiry_date, viewModel.price, viewModel.putaway_date);
        var affected = await connection.ExecuteAsync("""
            UPDATE `wms_stockadjust` SET
              `job_code`=@job_code,`sku_id`=@sku_id,`goods_owner_id`=@goods_owner_id,
              `goods_location_id`=@goods_location_id,`qty`=@qty,`is_update_stock`=@is_update_stock,
              `job_type`=@job_type,`source_table_id`=@source_table_id,`last_update_time`=@now,
              `erp_stock_id`=@erpStockId,`stock_allocation_id`=@allocationId,
              `series_number`=@series_number,`expiry_date`=@expiry_date,`price`=@price,`putaway_date`=@putaway_date
            WHERE `id`=@id;
            """, new
        {
            viewModel.id, viewModel.job_code, viewModel.sku_id, viewModel.goods_owner_id,
            viewModel.goods_location_id, viewModel.qty, viewModel.is_update_stock, viewModel.job_type,
            viewModel.source_table_id, now = DateTime.Now, viewModel.series_number, viewModel.expiry_date,
            viewModel.price, viewModel.putaway_date,
            erpStockId = allocation?.ErpStockId, allocationId = allocation?.AllocationId
        }, transaction);
        await transaction.CommitAsync();
        return affected > 0
            ? (true, _stringLocalizer["save_success"])
            : (false, _stringLocalizer["not_exists_entity"]);
    }

    /// <inheritdoc />
    public async Task<(bool flag, string msg)> DeleteAsync(int id)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.Serializable);
        var existing = await connection.QuerySingleOrDefaultAsync<StockadjustEntity>(
            "SELECT * FROM `wms_stockadjust` WHERE `id`=@id FOR UPDATE;", new { id }, transaction);
        if (existing == null)
        {
            await transaction.RollbackAsync();
            return (false, _stringLocalizer["not_exists_entity"]);
        }
        await using var routeConnection = await _connectionFactory.OpenConnectionAsync();
        var routeSnapshot = await CanonicalInventorySupport.GetRouteAsync(
            routeConnection, existing.tenant_id, existing.goods_location_id);
        _ = await CanonicalInventorySupport.LockRouteAsync(
            connection, transaction, existing.tenant_id, routeSnapshot);
        var affected = await connection.ExecuteAsync(
            "DELETE FROM `wms_stockadjust` WHERE `id`=@id;", new { id }, transaction);
        await transaction.CommitAsync();
        return affected > 0
            ? (true, _stringLocalizer["delete_success"])
            : (false, _stringLocalizer["not_exists_entity"]);
    }

    /// <inheritdoc />
    public async Task<(bool flag, string msg)> ConfirmAdjustment(int id)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted);
        try
        {
            var adjustment = await connection.QuerySingleOrDefaultAsync<StockadjustEntity>("""
                SELECT * FROM `wms_stockadjust` WHERE `id`=@id FOR UPDATE;
                """, new { id }, transaction);
            if (adjustment == null)
            {
                await transaction.RollbackAsync();
                return (false, _stringLocalizer["not_exists_entity"]);
            }
            if (adjustment.is_update_stock)
            {
                await transaction.RollbackAsync();
                return (false, _stringLocalizer["status_changed"]);
            }

            var now = DateTime.Now;
            var affected = 0;
            await using var routeConnection = await _connectionFactory.OpenConnectionAsync();
            var routeSnapshot = await CanonicalInventorySupport.GetRouteAsync(
                routeConnection, adjustment.tenant_id, adjustment.goods_location_id);
            var route = await CanonicalInventorySupport.LockRouteAsync(
                connection, transaction, adjustment.tenant_id, routeSnapshot);
            if (adjustment.job_type == 2)
            {
                affected += await connection.ExecuteAsync("""
                    UPDATE `wms_stockprocessdetail` SET `last_update_time`=@now,`is_update_stock`=1
                    WHERE `id`=@sourceId;
                    """, new { now, sourceId = adjustment.source_table_id }, transaction);
            }

            if (route.Mode == CanonicalInventorySupport.CanonicalMode)
            {
                if (!adjustment.erp_stock_id.HasValue || !adjustment.stock_allocation_id.HasValue)
                    throw new InvalidOperationException("调整单未绑定ERP库存分配，禁止确认");
                if (adjustment.qty != 0)
                {
                    await _stockMutationService.PrelockAsync(
                        connection, transaction, adjustment.tenant_id,
                        [route.ErpWarehouseId],
                        [adjustment.erp_stock_id.Value], [adjustment.stock_allocation_id.Value]);
                    await _stockMutationService.AdjustAvailableAsync(
                        connection, transaction,
                        CanonicalInventorySupport.Context(
                            adjustment.tenant_id, route.ErpWarehouseId,
                            $"MWMS:ADJ:{adjustment.id}", "STOCK_ADJUST_CONFIRM",
                            adjustment.id, adjustment.source_table_id, null, adjustment.creator, "库存可用量调整"),
                        adjustment.erp_stock_id.Value, adjustment.stock_allocation_id.Value,
                        adjustment.qty);
                    affected++;
                }
            }
            else
            {
                affected += await connection.ExecuteAsync("""
                UPDATE `wms_stock` SET `qty`=`qty`+@qty,`goods_owner_id`=@goods_owner_id,`last_update_time`=@now
                WHERE `goods_owner_id`=@goods_owner_id AND `series_number`=@series_number
                  AND `goods_location_id`=@goods_location_id AND `sku_id`=@sku_id
                  AND `expiry_date`=@expiry_date AND `price`=@price AND `putaway_date`=@putaway_date
                LIMIT 1;
                """, new
            {
                adjustment.qty, adjustment.goods_owner_id, adjustment.series_number,
                adjustment.goods_location_id, adjustment.sku_id, adjustment.expiry_date,
                adjustment.price, adjustment.putaway_date, now
                }, transaction);
            }

            affected += await connection.ExecuteAsync("""
                UPDATE `wms_stockadjust` SET `is_update_stock`=1,`last_update_time`=@now WHERE `id`=@id;
                """, new { id, now }, transaction);
            await transaction.CommitAsync();
            return affected > 0
                ? (true, _stringLocalizer["operation_success"])
                : (false, _stringLocalizer["operation_failed"]);
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }
}

internal static class CanonicalInventorySupport
{
    internal const string LegacyMode = "LEGACY_READ";
    internal const string CanonicalMode = "CANONICAL_ERP";

    internal static async Task<InventoryRoute> GetRouteAsync(
        MySqlConnection connection,
        long tenantId,
        int goodsLocationId)
    {
        var warehouse = await connection.QuerySingleOrDefaultAsync<RouteWarehouse>("""
            SELECT w.`erp_warehouse_id` ErpWarehouseId
              FROM `wms_goodslocation` l
              JOIN `wms_warehouse` w
                ON w.`id`=l.`warehouse_id` AND w.`tenant_id`=@tenantId AND w.`is_valid`=1
             WHERE l.`id`=@goodsLocationId AND l.`tenant_id`=@tenantId AND l.`is_valid`=1
             LIMIT 1;
            """, new { tenantId, goodsLocationId });
        if (warehouse == null || warehouse.ErpWarehouseId <= 0)
            throw new InvalidOperationException("库位未映射有效的ERP仓库，禁止库存操作");
        var config = await connection.QuerySingleOrDefaultAsync<RuntimeGate>("""
            SELECT `mode` Mode,`maintenance_enabled` MaintenanceEnabled
              FROM `wms_inventory_runtime_config`
             WHERE `tenant_id`=@tenantId AND `erp_warehouse_id`=@erpWarehouseId
             LIMIT 1;
            """, new { tenantId, erpWarehouseId = warehouse.ErpWarehouseId });
        var route = new InventoryRoute
        {
            GoodsLocationId = goodsLocationId,
            ErpWarehouseId = warehouse.ErpWarehouseId,
            Mode = config?.Mode ?? LegacyMode,
            MaintenanceEnabled = config?.MaintenanceEnabled ?? false
        };
        if (route.MaintenanceEnabled)
            throw new InvalidOperationException($"ERP仓库 {route.ErpWarehouseId} 正处于库存维护窗口，禁止库存操作");
        if (route.Mode is not LegacyMode and not CanonicalMode)
            throw new InvalidOperationException($"ERP仓库 {route.ErpWarehouseId} 的库存运行模式无效：{route.Mode}");
        return route;
    }

    internal static async Task<InventoryRoute> LockRouteAsync(
        MySqlConnection connection,
        IDbTransaction transaction,
        long tenantId,
        InventoryRoute snapshot)
    {
        var locked = await LockRuntimeAsync(connection, transaction, tenantId, snapshot);
        await ValidateLocationRouteAsync(connection, transaction, tenantId, snapshot);
        return locked;
    }

    private static async Task<InventoryRoute> LockRuntimeAsync(
        MySqlConnection connection,
        IDbTransaction transaction,
        long tenantId,
        InventoryRoute snapshot)
    {
        var config = await connection.QuerySingleOrDefaultAsync<RuntimeGate>("""
            SELECT `mode` Mode,`maintenance_enabled` MaintenanceEnabled
              FROM `wms_inventory_runtime_config`
             WHERE `tenant_id`=@tenantId AND `erp_warehouse_id`=@erpWarehouseId
             FOR SHARE;
            """, new { tenantId, erpWarehouseId = snapshot.ErpWarehouseId }, transaction);
        var locked = new InventoryRoute
        {
            GoodsLocationId = snapshot.GoodsLocationId,
            ErpWarehouseId = snapshot.ErpWarehouseId,
            Mode = config?.Mode ?? LegacyMode,
            MaintenanceEnabled = config?.MaintenanceEnabled ?? false
        };
        if (locked.MaintenanceEnabled)
            throw new InvalidOperationException($"ERP仓库 {locked.ErpWarehouseId} 正处于库存维护窗口，禁止库存操作");
        if (locked.Mode is not LegacyMode and not CanonicalMode)
            throw new InvalidOperationException($"ERP仓库 {locked.ErpWarehouseId} 的库存运行模式无效：{locked.Mode}");
        if (locked.Mode != snapshot.Mode)
            throw new InvalidOperationException("库存运行模式在业务操作期间发生变化，请重试");
        return locked;
    }

    private static async Task ValidateLocationRouteAsync(
        MySqlConnection connection,
        IDbTransaction transaction,
        long tenantId,
        InventoryRoute snapshot)
    {
        var warehouseId = await connection.ExecuteScalarAsync<long?>("""
            SELECT w.`erp_warehouse_id`
              FROM `wms_goodslocation` l
              JOIN `wms_warehouse` w
                ON w.`id`=l.`warehouse_id` AND w.`tenant_id`=@tenantId AND w.`is_valid`=1
             WHERE l.`id`=@goodsLocationId AND l.`tenant_id`=@tenantId AND l.`is_valid`=1
             LIMIT 1;
            """, new { tenantId, goodsLocationId = snapshot.GoodsLocationId }, transaction);
        if (warehouseId != snapshot.ErpWarehouseId)
            throw new InvalidOperationException("库位的ERP仓库映射在业务操作期间发生变化，请重试");
    }

    internal static async Task LockRoutesAsync(
        MySqlConnection connection,
        IDbTransaction transaction,
        long tenantId,
        IEnumerable<InventoryRoute> snapshots)
    {
        var routes = snapshots
            .GroupBy(x => x.ErpWarehouseId)
            .OrderBy(x => x.Key)
            .ToList();
        foreach (var group in routes)
        {
            if (group.Select(x => x.Mode).Distinct(StringComparer.Ordinal).Count() != 1)
                throw new InvalidOperationException($"ERP仓库 {group.Key} 的库存路由快照不一致，请重试");
            _ = await LockRuntimeAsync(connection, transaction, tenantId, group.First());
        }
        foreach (var snapshot in routes.SelectMany(x => x).OrderBy(x => x.ErpWarehouseId).ThenBy(x => x.GoodsLocationId))
            await ValidateLocationRouteAsync(connection, transaction, tenantId, snapshot);
    }

    internal static async Task<CanonicalAllocation> ResolveAllocationAsync(
        MySqlConnection connection,
        IDbTransaction transaction,
        long tenantId,
        int skuId,
        int goodsLocationId,
        int goodsOwnerId,
        string seriesNumber,
        DateTime expiryDate,
        decimal price,
        DateTime putawayDate,
        bool forUpdate = true)
    {
        var rows = (await connection.QueryAsync<CanonicalAllocation>($"""
            SELECT a.`id` AllocationId,a.`erp_stock_id` ErpStockId,
                   a.`allocated_qty` AllocatedQty,a.`occupied_qty` OccupiedQty,
                   s.`warehouse_id` ErpWarehouseId
              FROM `wms_erp_stock_allocation` a
              JOIN `trk_stock` s ON s.`id`=a.`erp_stock_id` AND s.`deleted`=b'0'
              JOIN `wms_erp_commodity_map` m
                ON m.`tenant_id`=a.`tenant_id` AND m.`erp_commodity_id`=s.`commodity_id`
             WHERE a.`tenant_id`=@tenantId AND m.`wms_sku_id`=@skuId
               AND a.`goods_location_id`=@goodsLocationId
               AND a.`goods_owner_id`=@goodsOwnerId
               AND a.`series_number`=@seriesNumber
               AND a.`expiry_date`=@expiryDate AND a.`price`=@price
               AND a.`putaway_date`=@putawayDate AND a.`location_state`='ACTIVE'
             ORDER BY s.`id`,a.`id` {(forUpdate ? "FOR UPDATE" : string.Empty)};
            """, new
        {
            tenantId, skuId, goodsLocationId, goodsOwnerId,
            seriesNumber = seriesNumber ?? string.Empty, expiryDate, price, putawayDate
        }, transaction)).AsList();
        if (rows.Count != 1)
            throw new InvalidOperationException(rows.Count == 0
                ? "无法根据SKU、货主、库位和批次唯一解析ERP库存分配，禁止写入第二套库存"
                : "匹配到多个ERP库存分配，禁止执行有歧义的库存操作");
        return rows[0];
    }

    internal static async Task<CanonicalAllocation> ResolveSimpleAllocationAsync(
        MySqlConnection connection,
        IDbTransaction? transaction,
        long tenantId,
        int skuId,
        int goodsLocationId,
        int goodsOwnerId,
        string seriesNumber,
        bool forUpdate = true)
    {
        var rows = (await connection.QueryAsync<CanonicalAllocation>($"""
            SELECT a.`id` AllocationId,a.`erp_stock_id` ErpStockId,
                   a.`allocated_qty` AllocatedQty,a.`occupied_qty` OccupiedQty,
                   s.`warehouse_id` ErpWarehouseId
              FROM `wms_erp_stock_allocation` a
              JOIN `trk_stock` s ON s.`id`=a.`erp_stock_id` AND s.`deleted`=b'0'
              JOIN `wms_erp_commodity_map` m
                ON m.`tenant_id`=a.`tenant_id` AND m.`erp_commodity_id`=s.`commodity_id`
             WHERE a.`tenant_id`=@tenantId AND m.`wms_sku_id`=@skuId
               AND a.`goods_location_id`=@goodsLocationId
               AND a.`goods_owner_id`=@goodsOwnerId
               AND a.`series_number`=@seriesNumber AND a.`location_state`='ACTIVE'
             ORDER BY s.`id`,a.`id` {(forUpdate ? "FOR UPDATE" : string.Empty)};
            """, new { tenantId, skuId, goodsLocationId, goodsOwnerId,
                seriesNumber = seriesNumber ?? string.Empty }, transaction)).AsList();
        if (rows.Count != 1)
            throw new InvalidOperationException(rows.Count == 0
                ? "无法唯一解析ERP库存分配，禁止库存操作"
                : "该操作匹配多个批次库存分配，请按具体批次操作");
        return rows[0];
    }

    internal static async Task<long> GetOrCreateTargetAllocationAsync(
        MySqlConnection connection,
        IDbTransaction transaction,
        StockmoveEntity move,
        CanonicalAllocation source,
        int targetLocationId,
        string operatorName)
    {
        var target = await connection.QuerySingleOrDefaultAsync<long?>("""
            SELECT `id` FROM `wms_erp_stock_allocation`
             WHERE `tenant_id`=@tenantId AND `erp_stock_id`=@erpStockId
               AND `goods_location_id`=@targetLocationId AND `goods_owner_id`=@goodsOwnerId
               AND `series_number`=@seriesNumber AND `expiry_date`=@expiryDate
               AND `price`=@price AND `putaway_date`=@putawayDate
               AND `location_state`='ACTIVE'
             LIMIT 1 FOR UPDATE;
            """, new
        {
            tenantId = move.tenant_id, erpStockId = source.ErpStockId, targetLocationId,
            goodsOwnerId = move.goods_owner_id, seriesNumber = move.series_number,
            expiryDate = move.expiry_date, move.price, putawayDate = move.putaway_date
        }, transaction);
        if (target.HasValue) return target.Value;

        var areaId = await connection.QuerySingleOrDefaultAsync<int?>("""
            SELECT `warehouse_area_id` FROM `wms_goodslocation`
             WHERE `id`=@targetLocationId AND `tenant_id`=@tenantId AND `is_valid`=1
             LIMIT 1 FOR UPDATE;
            """, new { targetLocationId, tenantId = move.tenant_id }, transaction);
        if (!areaId.HasValue)
            throw new InvalidOperationException("目标库位不存在或已停用");
        var now = DateTime.Now;
        await connection.ExecuteAsync("""
            INSERT INTO `wms_erp_stock_allocation`
                (`tenant_id`,`erp_stock_id`,`warehouse_area_id`,`goods_location_id`,`goods_owner_id`,
                 `series_number`,`expiry_date`,`price`,`putaway_date`,`allocated_qty`,`occupied_qty`,
                 `location_state`,`row_version`,`creator`,`create_time`,`updater`,`update_time`)
            VALUES
                (@tenantId,@erpStockId,@areaId,@targetLocationId,@goodsOwnerId,
                 @seriesNumber,@expiryDate,@price,@putawayDate,0,0,'ACTIVE',0,@operatorName,@now,@operatorName,@now);
            """, new
        {
            tenantId = move.tenant_id, erpStockId = source.ErpStockId, areaId,
            targetLocationId, goodsOwnerId = move.goods_owner_id,
            seriesNumber = move.series_number, expiryDate = move.expiry_date,
            move.price, putawayDate = move.putaway_date, operatorName, now
        }, transaction);
        return await connection.ExecuteScalarAsync<long>("SELECT LAST_INSERT_ID();", transaction: transaction);
    }

    internal static Task<long?> FindTargetAllocationIdAsync(
        MySqlConnection connection,
        IDbTransaction? transaction,
        StockmoveEntity move,
        long erpStockId,
        int targetLocationId) => connection.QuerySingleOrDefaultAsync<long?>("""
            SELECT `id` FROM `wms_erp_stock_allocation`
             WHERE `tenant_id`=@tenantId AND `erp_stock_id`=@erpStockId
               AND `goods_location_id`=@targetLocationId AND `goods_owner_id`=@goodsOwnerId
               AND `series_number`=@seriesNumber AND `expiry_date`=@expiryDate
               AND `price`=@price AND `putaway_date`=@putawayDate
               AND `location_state`='ACTIVE'
             LIMIT 1;
            """, new
        {
            tenantId = move.tenant_id, erpStockId, targetLocationId,
            goodsOwnerId = move.goods_owner_id, seriesNumber = move.series_number,
            expiryDate = move.expiry_date, move.price, putawayDate = move.putaway_date
        }, transaction);

    internal static StockMutationContext Context(
        long tenantId,
        long erpWarehouseId,
        string operationKey,
        string bizType,
        long bizId,
        long bizItemId,
        CurrentUser? user,
        string fallbackOperator,
        string remark) => new(
            tenantId,
            erpWarehouseId,
            operationKey.Length <= 64 ? operationKey : throw new InvalidOperationException("库存操作幂等键超过64个字符"),
            bizType,
            bizId,
            bizItemId,
            user?.user_id ?? 0,
            OperatorName(user, fallbackOperator),
            remark);

    private static string OperatorName(CurrentUser? user, string fallbackOperator)
    {
        var value = string.IsNullOrWhiteSpace(user?.user_name) ? fallbackOperator : user.user_name;
        return value.Length <= 64 ? value : value[..64];
    }

    internal sealed class InventoryRoute
    {
        public int GoodsLocationId { get; init; }
        public long ErpWarehouseId { get; init; }
        public string Mode { get; init; } = LegacyMode;
        public bool MaintenanceEnabled { get; init; }
    }

    private sealed class RouteWarehouse
    {
        public long ErpWarehouseId { get; init; }
    }

    private sealed class RuntimeGate
    {
        public string Mode { get; init; } = LegacyMode;
        public bool MaintenanceEnabled { get; init; }
    }

    internal sealed class CanonicalAllocation
    {
        public long AllocationId { get; init; }
        public long ErpStockId { get; init; }
        public long AllocatedQty { get; init; }
        public long OccupiedQty { get; init; }
        public long ErpWarehouseId { get; init; }
    }
}
