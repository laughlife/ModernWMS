using System.Data;
using Dapper;
using Microsoft.Extensions.Localization;
using ModernWMS.Core;
using ModernWMS.Core.Database;
using ModernWMS.Core.JWT;
using ModernWMS.Core.Models;
using ModernWMS.Core.Services;
using ModernWMS.WMS.Entities.Models;
using ModernWMS.WMS.Entities.ViewModels;
using ModernWMS.WMS.IServices;
using ModernWMS.WMS.IServices.StockAllocation;

namespace ModernWMS.WMS.Services;

/// <summary>库存冻结业务服务。</summary>
public class StockfreezeService : BaseService<StockfreezeEntity>, IStockfreezeService
{
    private const string ViewSql = """
        SELECT f.`id`,f.`job_code`,f.`job_type`,f.`sku_id`,f.`goods_owner_id`,f.`goods_location_id`,
               f.`handler`,f.`handle_time`,f.`last_update_time`,f.`tenant_id`,f.`series_number`,
               f.`erp_stock_id`,f.`stock_allocation_id`,f.`reservation_id`,f.`reservation_item_id`,
               f.`source_freeze_id`,
               k.`sku_code`,p.`spu_code`,p.`spu_name`,l.`location_name`,l.`warehouse_name`
          FROM `wms_stockfreeze` f
          INNER JOIN `wms_sku` k ON k.`id`=f.`sku_id`
          INNER JOIN `wms_spu` p ON p.`id`=k.`spu_id`
          INNER JOIN `wms_goodslocation` l ON l.`id`=f.`goods_location_id`
        """;

    private static readonly IReadOnlyDictionary<string, string> SearchColumns =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["id"] = "f.`id`", ["job_code"] = "f.`job_code`", ["job_type"] = "f.`job_type`",
            ["sku_id"] = "f.`sku_id`", ["goods_owner_id"] = "f.`goods_owner_id`",
            ["goods_location_id"] = "f.`goods_location_id`", ["handler"] = "f.`handler`",
            ["handle_time"] = "f.`handle_time`", ["last_update_time"] = "f.`last_update_time`",
            ["tenant_id"] = "f.`tenant_id`", ["series_number"] = "f.`series_number`",
            ["sku_code"] = "k.`sku_code`", ["spu_code"] = "p.`spu_code`", ["spu_name"] = "p.`spu_name`",
            ["location_name"] = "l.`location_name`", ["warehouse_name"] = "l.`warehouse_name`"
        };

    private readonly IMySqlConnectionFactory _connectionFactory;
    private readonly IStringLocalizer<ModernWMS.Core.MultiLanguage> _stringLocalizer;
    private readonly FunctionHelper _functionHelper;
    private readonly IStockAllocationMutationService _stockMutationService;

    /// <summary>初始化库存冻结服务。</summary>
    public StockfreezeService(
        IMySqlConnectionFactory connectionFactory,
        IStringLocalizer<ModernWMS.Core.MultiLanguage> stringLocalizer,
        FunctionHelper functionHelper,
        IStockAllocationMutationService stockMutationService)
    {
        _connectionFactory = connectionFactory;
        _stringLocalizer = stringLocalizer;
        _functionHelper = functionHelper;
        _stockMutationService = stockMutationService;
    }

    /// <inheritdoc />
    public async Task<(List<StockfreezeViewModel> data, int totals)> PageAsync(
        PageSearch pageSearch,
        CurrentUser currentUser)
    {
        var filter = DapperSearchBuilder.Build(pageSearch.searchObjects, SearchColumns);
        var where = "f.`tenant_id`=@tenantId" +
                    (string.IsNullOrWhiteSpace(filter.Sql) ? string.Empty : $" AND {filter.Sql}");
        filter.Parameters.Add("tenantId", currentUser.tenant_id);
        filter.Parameters.Add("offset", (pageSearch.pageIndex - 1) * pageSearch.pageSize);
        filter.Parameters.Add("pageSize", pageSearch.pageSize);

        await using var connection = await _connectionFactory.OpenConnectionAsync();
        using var result = await connection.QueryMultipleAsync($"""
            SELECT COUNT(*) FROM `wms_stockfreeze` f
            INNER JOIN `wms_sku` k ON k.`id`=f.`sku_id`
            INNER JOIN `wms_spu` p ON p.`id`=k.`spu_id`
            INNER JOIN `wms_goodslocation` l ON l.`id`=f.`goods_location_id`
            WHERE {where};
            {ViewSql} WHERE {where} ORDER BY f.`last_update_time` DESC LIMIT @pageSize OFFSET @offset;
            """, filter.Parameters);
        var totals = await result.ReadSingleAsync<int>();
        var rows = (await result.ReadAsync<StockfreezeViewModel>()).AsList();
        return (rows, totals);
    }

    /// <inheritdoc />
    public async Task<List<StockfreezeViewModel>> GetAllAsync(CurrentUser currentUser)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync();
        return (await connection.QueryAsync<StockfreezeViewModel>("""
            SELECT `id`,`job_code`,`job_type`,`sku_id`,`goods_owner_id`,`goods_location_id`,`handler`,
                   `handle_time`,`last_update_time`,`tenant_id`,`erp_stock_id`,`stock_allocation_id`,
                   `source_freeze_id`,`series_number`
              FROM `wms_stockfreeze` WHERE `tenant_id`=@tenantId;
            """, new { tenantId = currentUser.tenant_id })).AsList();
    }

    /// <inheritdoc />
    public async Task<StockfreezeViewModel?> GetAsync(int id)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync();
        return await connection.QuerySingleOrDefaultAsync<StockfreezeViewModel>($"""
            {ViewSql} WHERE f.`id`=@id LIMIT 1;
            """, new { id });
    }

    /// <inheritdoc />
    public async Task<(int id, string msg)> AddAsync(StockfreezeViewModel viewModel, CurrentUser currentUser)
    {
        var jobCode = await _functionHelper.GetFormNoAsync("Stockfreeze");
        await using var connection = await _connectionFactory.OpenConnectionAsync();
        var routeSnapshot = await CanonicalInventorySupport.GetRouteAsync(
            connection, currentUser.tenant_id, viewModel.goods_location_id);
        CanonicalInventorySupport.CanonicalAllocation? freezeAllocationSnapshot = null;
        if (viewModel.job_type && routeSnapshot.Mode == CanonicalInventorySupport.CanonicalMode)
            freezeAllocationSnapshot = await CanonicalInventorySupport.ResolveSimpleAllocationAsync(
                connection, null, currentUser.tenant_id, viewModel.sku_id,
                viewModel.goods_location_id, viewModel.goods_owner_id, viewModel.series_number,
                forUpdate: false);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted);
        try
        {
            StockfreezeEntity? sourceFreeze = null;
            if (!viewModel.job_type && viewModel.source_freeze_id.HasValue)
                sourceFreeze = await connection.QuerySingleOrDefaultAsync<StockfreezeEntity>("""
                    SELECT * FROM `wms_stockfreeze`
                     WHERE `id`=@sourceFreezeId AND `tenant_id`=@tenantId AND `job_type`=1
                     FOR UPDATE;
                    """, new { sourceFreezeId = viewModel.source_freeze_id.Value,
                        tenantId = currentUser.tenant_id }, transaction);
            var route = await CanonicalInventorySupport.LockRouteAsync(
                connection, transaction, currentUser.tenant_id, routeSnapshot);
            CanonicalInventorySupport.CanonicalAllocation? allocation = null;
            if (route.Mode == CanonicalInventorySupport.CanonicalMode)
            {
                if (!viewModel.job_type)
                {
                    if (sourceFreeze == null || !sourceFreeze.erp_stock_id.HasValue
                        || !sourceFreeze.stock_allocation_id.HasValue)
                        throw new InvalidOperationException("统一库存模式解冻必须明确关联有效的源冻结单");
                    if (sourceFreeze.sku_id != viewModel.sku_id
                        || sourceFreeze.goods_location_id != viewModel.goods_location_id
                        || sourceFreeze.goods_owner_id != viewModel.goods_owner_id
                        || sourceFreeze.series_number != viewModel.series_number)
                        throw new InvalidOperationException("解冻维度与源冻结单不一致");
                    allocation = new CanonicalInventorySupport.CanonicalAllocation
                    {
                        ErpStockId = sourceFreeze.erp_stock_id.Value,
                        AllocationId = sourceFreeze.stock_allocation_id.Value,
                        ErpWarehouseId = route.ErpWarehouseId
                    };
                }
                else
                {
                    if (viewModel.source_freeze_id.HasValue)
                        throw new InvalidOperationException("冻结操作不能关联源冻结单");
                    allocation = freezeAllocationSnapshot
                        ?? throw new InvalidOperationException("冻结库存分配预解析失败，请重试");
                }
            }
            var parameters = new
            {
                viewModel.goods_location_id,
                viewModel.goods_owner_id,
                viewModel.sku_id,
                viewModel.series_number,
                tenantId = currentUser.tenant_id
            };
            if (await connection.ExecuteScalarAsync<bool>("""
                SELECT EXISTS(SELECT 1 FROM `wms_stockprocessdetail`
                    WHERE `goods_location_id`=@goods_location_id AND `goods_owner_id`=@goods_owner_id
                      AND `sku_id`=@sku_id AND `is_update_stock`=0 AND `tenant_id`=@tenantId);
                """, parameters, transaction))
            {
                await transaction.RollbackAsync();
                return (0, _stringLocalizer["process_not_comfirm"]);
            }
            if (await connection.ExecuteScalarAsync<bool>("""
                SELECT EXISTS(SELECT 1 FROM `wms_dispatchpicklist`
                    WHERE `goods_location_id`=@goods_location_id AND `sku_id`=@sku_id
                      AND `is_update_stock`=0);
                """, parameters, transaction))
            {
                await transaction.RollbackAsync();
                return (0, _stringLocalizer["dispatch_not_comfirm"]);
            }
            if (await connection.ExecuteScalarAsync<bool>("""
                SELECT EXISTS(SELECT 1 FROM `wms_stockmove`
                    WHERE (`orig_goods_location_id`=@goods_location_id OR `dest_googs_location_id`=@goods_location_id)
                      AND `sku_id`=@sku_id AND `move_status`=0 AND `tenant_id`=@tenantId);
                """, parameters, transaction))
            {
                await transaction.RollbackAsync();
                return (0, _stringLocalizer["move_not_comfirm"]);
            }

            var now = DateTime.Now;
            if (route.Mode == CanonicalInventorySupport.LegacyMode)
                await connection.ExecuteAsync("""
                UPDATE `wms_stock` SET `is_freeze`=@isFreeze
                 WHERE `goods_location_id`=@goods_location_id AND `goods_owner_id`=@goods_owner_id
                   AND `sku_id`=@sku_id AND `series_number`=@series_number AND `tenant_id`=@tenantId;
                """, new
                {
                    isFreeze = viewModel.job_type,
                    viewModel.goods_location_id,
                    viewModel.goods_owner_id,
                    viewModel.sku_id,
                    viewModel.series_number,
                    tenantId = currentUser.tenant_id
                }, transaction);
            var id = await connection.ExecuteScalarAsync<int>("""
                INSERT INTO `wms_stockfreeze` (`job_code`,`job_type`,`sku_id`,`goods_owner_id`,`goods_location_id`,
                    `handler`,`handle_time`,`last_update_time`,`tenant_id`,`erp_stock_id`,`stock_allocation_id`,
                    `source_freeze_id`,`series_number`)
                VALUES (@jobCode,@jobType,@skuId,@goodsOwnerId,@goodsLocationId,@handler,@handleTime,@lastUpdate,
                    @tenantId,@erpStockId,@allocationId,@sourceFreezeId,@seriesNumber); SELECT LAST_INSERT_ID();
                """, new
                {
                    jobCode,
                    jobType = viewModel.job_type,
                    skuId = viewModel.sku_id,
                    goodsOwnerId = viewModel.goods_owner_id,
                    goodsLocationId = viewModel.goods_location_id,
                    handler = currentUser.user_name,
                    handleTime = now,
                    lastUpdate = now,
                    tenantId = currentUser.tenant_id,
                    erpStockId = allocation?.ErpStockId,
                    allocationId = allocation?.AllocationId,
                    sourceFreezeId = viewModel.source_freeze_id,
                    seriesNumber = viewModel.series_number
                }, transaction);
            if (allocation != null)
            {
                allocation = await connection.QuerySingleAsync<CanonicalInventorySupport.CanonicalAllocation>("""
                    SELECT `id` AllocationId,`erp_stock_id` ErpStockId,
                           `allocated_qty` AllocatedQty,`occupied_qty` OccupiedQty
                      FROM `wms_erp_stock_allocation`
                     WHERE `tenant_id`=@tenantId AND `id`=@allocationId;
                    """, new { tenantId = currentUser.tenant_id,
                        allocationId = allocation.AllocationId }, transaction);
                var operationKey = $"MWMS:FRZ:{id}";
                var reservationOwner = viewModel.job_type
                    ? null
                    : sourceFreeze ?? throw new InvalidOperationException(
                        "统一库存模式解冻必须明确关联有效的源冻结单");
                var reservationOwnerId = reservationOwner?.id ?? id;
                var context = CanonicalInventorySupport.Context(
                    currentUser.tenant_id, route.ErpWarehouseId, operationKey,
                    viewModel.job_type ? "STOCK_FREEZE_RESERVE" : "STOCK_FREEZE_RELEASE", id, id,
                    currentUser, currentUser.user_name, viewModel.job_type ? "冻结库存" : "解冻库存") with
                {
                    Reservation = new StockReservationMutationContext(
                        "WMS_RESERVATION_V1",operationKey,"MODERN_WMS","STOCK_FREEZE",
                        reservationOwnerId,jobCode,null,null,
                        "STOCK_FREEZE",reservationOwnerId,
                        $"STOCK_FREEZE:{reservationOwnerId}:{allocation.AllocationId}",
                        reservationOwner?.reservation_id,
                        reservationOwner?.reservation_item_id)
                };
                StockAllocationMutationResult mutationResult;
                if (viewModel.job_type)
                {
                    var quantity = allocation.AllocatedQty - allocation.OccupiedQty;
                    if (quantity <= 0)
                        throw new InvalidOperationException("该库存分配没有可冻结数量");
                    mutationResult = await _stockMutationService.ReserveAsync(
                        connection, transaction, context, allocation.ErpStockId, allocation.AllocationId, quantity);
                }
                else
                {
                    var releaseSource = reservationOwner
                        ?? throw new InvalidOperationException(
                            "统一库存模式解冻必须明确关联有效的源冻结单");
                    var quantity = await connection.ExecuteScalarAsync<long>("""
                        SELECT reserve_qty-COALESCE(released_qty,0)
                          FROM (
                            SELECT COALESCE(SUM(CASE
                                       WHEN l.`biz_type`='STOCK_FREEZE_RESERVE'
                                        AND l.`biz_id`=@sourceFreezeId THEN l.`occupied_delta`
                                       ELSE 0 END),0) reserve_qty,
                                   COALESCE(SUM(CASE
                                       WHEN l.`biz_type`='STOCK_FREEZE_RELEASE'
                                        AND f.`source_freeze_id`=@sourceFreezeId THEN -l.`occupied_delta`
                                       ELSE 0 END),0) released_qty
                              FROM `wms_erp_stock_allocation_log` l
                              LEFT JOIN `wms_stockfreeze` f
                                ON f.`id`=l.`biz_id` AND f.`tenant_id`=l.`tenant_id`
                             WHERE l.`tenant_id`=@tenantId
                               AND l.`erp_stock_id`=@erpStockId
                               AND l.`allocation_id`=@allocationId
                               AND l.`biz_type` IN ('STOCK_FREEZE_RESERVE','STOCK_FREEZE_RELEASE')
                          ) hold_qty;
                        """, new { tenantId = currentUser.tenant_id,
                            erpStockId = allocation.ErpStockId, allocationId = allocation.AllocationId,
                            sourceFreezeId = releaseSource.id }, transaction);
                    if (quantity <= 0)
                        throw new InvalidOperationException("源冻结单已全部解冻或没有可解冻持有量");
                    if (quantity > allocation.OccupiedQty)
                        throw new InvalidOperationException("源冻结单剩余持有量超过当前占用量，禁止解冻");
                    mutationResult = await _stockMutationService.ReleaseAsync(
                        connection, transaction, context, allocation.ErpStockId, allocation.AllocationId, quantity);
                }
                await connection.ExecuteAsync("""
                    UPDATE `wms_stockfreeze`
                       SET `reservation_id`=@reservationId,`reservation_item_id`=@reservationItemId
                     WHERE `id`=@id AND `tenant_id`=@tenantId;
                    """,new{id,tenantId=currentUser.tenant_id,
                        reservationId=mutationResult.ReservationId,
                        reservationItemId=mutationResult.ReservationItemId},transaction);
            }
            await transaction.CommitAsync();
            return id > 0 ? (id, _stringLocalizer["save_success"]) : (0, _stringLocalizer["save_failed"]);
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<(bool flag, string msg)> UpdateAsync(StockfreezeViewModel viewModel)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.Serializable);
        try
        {
            var existing = await connection.QuerySingleOrDefaultAsync<StockfreezeEntity>(
                "SELECT * FROM `wms_stockfreeze` WHERE `id`=@id FOR UPDATE;",
                new { viewModel.id }, transaction);
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
            var canonical = await connection.ExecuteScalarAsync<bool>("""
                SELECT `erp_stock_id` IS NOT NULL FROM `wms_stockfreeze` WHERE `id`=@id;
                """, new { viewModel.id }, transaction);
            if (canonical)
            {
                await transaction.RollbackAsync();
                return (false, "统一库存模式下冻结记录不可编辑，请执行新的冻结或解冻操作");
            }
            var qty = await connection.ExecuteAsync("""
                UPDATE `wms_stockfreeze` SET `job_code`=@job_code,`job_type`=@job_type,`sku_id`=@sku_id,
                    `goods_owner_id`=@goods_owner_id,`goods_location_id`=@goods_location_id,`handler`=@handler,
                    `handle_time`=@handle_time,`last_update_time`=@lastUpdate,`series_number`=@series_number
                 WHERE `id`=@id;
                """, new
                {
                    viewModel.id, viewModel.job_code, viewModel.job_type, viewModel.sku_id, viewModel.goods_owner_id,
                    viewModel.goods_location_id, viewModel.handler, viewModel.handle_time,
                    lastUpdate = DateTime.Now, viewModel.series_number
                }, transaction);
            await transaction.CommitAsync();
            return qty > 0
                ? (true, _stringLocalizer["save_success"])
                : (false, _stringLocalizer["save_failed"]);
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<(bool flag, string msg)> DeleteAsync(int id)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.Serializable);
        try
        {
            var existing = await connection.QuerySingleOrDefaultAsync<StockfreezeEntity>(
                "SELECT * FROM `wms_stockfreeze` WHERE `id`=@id FOR UPDATE;", new { id }, transaction);
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
            var canonical = await connection.ExecuteScalarAsync<bool>("""
                SELECT COALESCE(`erp_stock_id` IS NOT NULL,0) FROM `wms_stockfreeze` WHERE `id`=@id FOR UPDATE;
                """, new { id }, transaction);
            if (canonical)
            {
                await transaction.RollbackAsync();
                return (false, "统一库存模式下冻结记录不可删除");
            }
            var qty = await connection.ExecuteAsync("DELETE FROM `wms_stockfreeze` WHERE `id`=@id;", new { id }, transaction);
            await transaction.CommitAsync();
            return qty > 0
                ? (true, _stringLocalizer["delete_success"])
                : (false, _stringLocalizer["delete_failed"]);
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<string> GetOrderCode(CurrentUser currentUser)
    {
        var date = DateTime.Now.ToString("yyyyMMdd");
        await using var connection = await _connectionFactory.OpenConnectionAsync();
        var maxNo = await connection.QuerySingleAsync<string?>("""
            SELECT MAX(`job_code`) FROM `wms_stockfreeze` WHERE `tenant_id`=@tenantId;
            """, new { tenantId = currentUser.tenant_id });
        if (maxNo == null) return date + "-0001";
        var maxDate = maxNo.Substring(0, 8);
        var maxDateNo = maxNo.Substring(9, 4);
        if (date != maxDate) return date + "-0001";
        int.TryParse(maxDateNo, out var number);
        return date + "-" + (number + 1).ToString("0000");
    }
}
