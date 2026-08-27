using System.Data;
using Dapper;
using Mapster;
using Microsoft.Extensions.Localization;
using ModernWMS.Core;
using ModernWMS.Core.Database;
using ModernWMS.Core.JWT;
using ModernWMS.Core.Models;
using ModernWMS.Core.Services;
using ModernWMS.Core.Utility;
using ModernWMS.WMS.Entities.Models;
using ModernWMS.WMS.Entities.ViewModels;
using ModernWMS.WMS.IServices;
using ModernWMS.WMS.IServices.StockAllocation;

namespace ModernWMS.WMS.Services;

/// <summary>Stocktaking Service.</summary>
public class StocktakingService : BaseService<StocktakingEntity>, IStocktakingService
{
    private readonly IMySqlConnectionFactory _connectionFactory;
    private readonly IStringLocalizer<Core.MultiLanguage> _stringLocalizer;
    private readonly FunctionHelper _functionHelper;
    private readonly IStockAllocationMutationService _stockMutationService;

    /// <summary>初始化盘点服务。</summary>
    public StocktakingService(
        IMySqlConnectionFactory connectionFactory,
        IStringLocalizer<Core.MultiLanguage> stringLocalizer,
        FunctionHelper functionHelper,
        IStockAllocationMutationService stockMutationService)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        _stringLocalizer = stringLocalizer ?? throw new ArgumentNullException(nameof(stringLocalizer));
        _functionHelper = functionHelper ?? throw new ArgumentNullException(nameof(functionHelper));
        _stockMutationService = stockMutationService ?? throw new ArgumentNullException(nameof(stockMutationService));
    }

    /// <inheritdoc />
    public async Task<(List<StocktakingViewModel> data, int totals)> PageAsync(
        PageSearch pageSearch, CurrentUser currentUser)
    {
        var where = DapperSearchBuilder.Build(pageSearch.searchObjects, SearchColumns);
        where.Parameters.Add("offset", (pageSearch.pageIndex - 1) * pageSearch.pageSize);
        where.Parameters.Add("pageSize", pageSearch.pageSize);
        var filter = string.IsNullOrWhiteSpace(where.Sql) ? string.Empty : $" AND {where.Sql}";
        await using var connection = await _connectionFactory.OpenConnectionAsync();
        using var grid = await connection.QueryMultipleAsync($"""
            SELECT COUNT(*) {FromSql} WHERE 1=1{filter};
            SELECT {ViewColumns} {FromSql}
            WHERE 1=1{filter}
            ORDER BY st.`last_update_time` DESC
            LIMIT @pageSize OFFSET @offset;
            """, where.Parameters);
        var totals = await grid.ReadSingleAsync<int>();
        return ((await grid.ReadAsync<StocktakingViewModel>()).AsList(), totals);
    }

    /// <inheritdoc />
    public async Task<StocktakingViewModel> GetAsync(int id)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync();
        return await connection.QuerySingleOrDefaultAsync<StocktakingViewModel>($"""
            SELECT {ViewColumns} {FromSql} WHERE st.`id`=@id LIMIT 1;
            """, new { id }) ?? new StocktakingViewModel();
    }

    /// <inheritdoc />
    public async Task<(int id, string msg)> AddAsync(
        StocktakingBasicViewModel viewModel, CurrentUser currentUser)
    {
        var entity = viewModel.Adapt<StocktakingEntity>();
        entity.id = 0;
        entity.job_code = await _functionHelper.GetFormNoAsync("Stocktaking");
        entity.creator = currentUser.user_name;
        entity.create_time = DateTime.Now;
        entity.last_update_time = DateTime.Now;
        await using var connection = await _connectionFactory.OpenConnectionAsync();
        var routeSnapshot = await CanonicalInventorySupport.GetRouteAsync(
            connection, entity.goods_location_id);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted);
        try
        {
            var route = await CanonicalInventorySupport.LockRouteAsync(
                connection, transaction, routeSnapshot);
            if (route.Mode == CanonicalInventorySupport.CanonicalMode)
            {
                var allocation = await CanonicalInventorySupport.ResolveAllocationAsync(
                    connection, transaction, entity.sku_id,
                    entity.goods_location_id, entity.goods_owner_id, entity.series_number,
                    entity.expiry_date, entity.price, entity.putaway_date);
                entity.erp_stock_id = allocation.ErpStockId;
                entity.stock_allocation_id = allocation.AllocationId;
            }
            entity.id = await connection.ExecuteScalarAsync<int>("""
                INSERT INTO `wms_stocktaking`
                  (`job_code`,`job_status`,`sku_id`,`goods_owner_id`,`goods_location_id`,`series_number`,
                   `expiry_date`,`price`,`putaway_date`,`book_qty`,`counted_qty`,`difference_qty`,`creator`,
                   `create_time`,`last_update_time`,`erp_stock_id`,`stock_allocation_id`,`handler`,`handle_time`)
                VALUES
                  (@job_code,@job_status,@sku_id,@goods_owner_id,@goods_location_id,@series_number,
                   @expiry_date,@price,@putaway_date,@book_qty,@counted_qty,@difference_qty,@creator,
                   @create_time,@last_update_time,@erp_stock_id,@stock_allocation_id,@handler,@handle_time);
                SELECT LAST_INSERT_ID();
                """, entity, transaction);
            await transaction.CommitAsync();
            return entity.id > 0
                ? (entity.id, _stringLocalizer["save_success"])
                : (0, _stringLocalizer["save_failed"]);
        }
        catch { await transaction.RollbackAsync(); throw; }
    }

    /// <inheritdoc />
    public async Task<string> GetOrderCode(CurrentUser currentUser)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync();
        var maxNo = await connection.ExecuteScalarAsync<string?>("""
            SELECT MAX(`job_code`) FROM `wms_stocktaking`;
            """);
        var date = DateTime.Now.ToString("yyyyMMdd");
        if (string.IsNullOrEmpty(maxNo)) return date + "-0001";
        try
        {
            var maxDate = maxNo[..8];
            var maxDateNo = maxNo[9..];
            if (date != maxDate) return date + "-0001";
            int.TryParse(maxDateNo, out var number);
            return date + "-" + (number + 1).ToString("0000");
        }
        catch { return date + "-0001"; }
    }

    /// <inheritdoc />
    public async Task<(bool flag, string msg)> PutAsync(
        StocktakingConfirmViewModel viewModel, CurrentUser currentUser)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.Serializable);
        try
        {
            var entity = await GetForUpdateAsync(connection, transaction, viewModel.id);
            if (entity == null)
                return await RollbackResult((false, _stringLocalizer["not_exists_entity"]), transaction);
            await using var routeConnection = await _connectionFactory.OpenConnectionAsync();
            var routeSnapshot = await CanonicalInventorySupport.GetRouteAsync(
                routeConnection, entity.goods_location_id);
            _ = await CanonicalInventorySupport.LockRouteAsync(
                connection, transaction, routeSnapshot);
            var now = DateTime.Now;
            var qty = await connection.ExecuteAsync("""
                UPDATE `wms_stocktaking`
                SET `counted_qty`=@countedQty,`difference_qty`=@differenceQty,
                    `last_update_time`=@now,`handler`=@handler,`handle_time`=@now,`job_status`=1
                WHERE `id`=@id;
                """, new { countedQty = viewModel.counted_qty,
                    differenceQty = viewModel.counted_qty - entity.book_qty,
                    now, handler = currentUser.user_name, viewModel.id }, transaction);
            await transaction.CommitAsync();
            return qty > 0
                ? (true, _stringLocalizer["save_success"])
                : (false, _stringLocalizer["save_failed"]);
        }
        catch { await transaction.RollbackAsync(); throw; }
    }

    /// <inheritdoc />
    public async Task<(bool flag, string msg)> ConfirmAsync(int id, CurrentUser currentUser)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.Serializable);
        try
        {
            var entity = await GetForUpdateAsync(connection, transaction, id);
            if (entity == null)
                return await RollbackResult((false, _stringLocalizer["not_exists_entity"]), transaction);
            await using var routeConnection = await _connectionFactory.OpenConnectionAsync();
            var routeSnapshot = await CanonicalInventorySupport.GetRouteAsync(
                routeConnection, entity.goods_location_id);
            var route = await CanonicalInventorySupport.LockRouteAsync(
                connection, transaction, routeSnapshot);
            var now = DateTime.Now;
            var qty = 0;
            if (route.Mode == CanonicalInventorySupport.CanonicalMode)
            {
                if (!entity.erp_stock_id.HasValue || !entity.stock_allocation_id.HasValue)
                    return await RollbackResult((false, "盘点单未绑定ERP库存分配，禁止确认"), transaction);
                if (entity.difference_qty != 0)
                {
                    await _stockMutationService.PrelockAsync(
                        connection, transaction,
                        [route.ErpWarehouseId],
                        [entity.erp_stock_id.Value], [entity.stock_allocation_id.Value]);
                    await _stockMutationService.AdjustAvailableAsync(
                        connection, transaction,
                        CanonicalInventorySupport.Context(
                            route.ErpWarehouseId,
                            $"MWMS:TA:{entity.id}", "STOCKTAKING_ADJUST",
                            entity.id, entity.id, currentUser, entity.creator, "盘点差异调整"),
                        entity.erp_stock_id.Value, entity.stock_allocation_id.Value,
                        entity.difference_qty);
                    qty++;
                }
            }
            else
            {
                var stockId = await connection.QuerySingleOrDefaultAsync<int?>("""
                SELECT `id` FROM `wms_stock`
                WHERE `sku_id`=@sku_id AND `goods_owner_id`=@goods_owner_id
                  AND `goods_location_id`=@goods_location_id AND `series_number`=@series_number
                  AND `expiry_date`=@expiry_date AND `price`=@price AND `putaway_date`=@putaway_date
                LIMIT 1 FOR UPDATE;
                """, entity, transaction);
                qty = stockId.HasValue
                    ? await connection.ExecuteAsync("""
                    UPDATE `wms_stock` SET `qty`=`qty`+@differenceQty,`last_update_time`=@now
                    WHERE `id`=@stockId;
                    """, new { differenceQty = entity.difference_qty, now, stockId }, transaction)
                : await connection.ExecuteAsync("""
                    INSERT INTO `wms_stock`
                      (`sku_id`,`goods_location_id`,`qty`,`goods_owner_id`,`is_freeze`,`last_update_time`,
                       `series_number`,`expiry_date`,`price`,`putaway_date`)
                    VALUES
                      (@skuId,@goodsLocationId,@qty,@goodsOwnerId,0,@now,@seriesNumber,
                       @expiryDate,@price,@putawayDate);
                    """, new { skuId = entity.sku_id, goodsLocationId = entity.goods_location_id,
                        qty = entity.difference_qty, goodsOwnerId = entity.goods_owner_id, now,
                        expiryDate = entity.expiry_date, entity.price,
                        putawayDate = DateTime.Now.ToString("yyyy-MM-dd").ObjToDate() }, transaction);
            }
            qty += await connection.ExecuteAsync("""
                INSERT INTO `wms_stockadjust`
                  (`job_code`,`sku_id`,`goods_owner_id`,`goods_location_id`,`qty`,`creator`,`create_time`,
                   `last_update_time`,`is_update_stock`,`job_type`,`source_table_id`,
                   `erp_stock_id`,`stock_allocation_id`,`series_number`,`expiry_date`,`price`,`putaway_date`)
                VALUES
                  (@jobCode,@skuId,@goodsOwnerId,@goodsLocationId,@differenceQty,@creator,@now,
                   @now,@1,1,@sourceId,@erpStockId,@allocationId,@seriesNumber,@expiryDate,@price,@putawayDate);
                """, new { jobCode = entity.job_code, skuId = entity.sku_id,
                    goodsOwnerId = entity.goods_owner_id, goodsLocationId = entity.goods_location_id,
                    differenceQty = entity.difference_qty, creator = currentUser.user_name, now,
                    erpStockId = entity.erp_stock_id, allocationId = entity.stock_allocation_id,
                    seriesNumber = entity.series_number, expiryDate = entity.expiry_date,
                    entity.price, putawayDate = entity.putaway_date }, transaction);
            await transaction.CommitAsync();
            return qty > 0
                ? (true, _stringLocalizer["operation_success"])
                : (false, _stringLocalizer["operation_failed"]);
        }
        catch { await transaction.RollbackAsync(); throw; }
    }

    /// <inheritdoc />
    public async Task<(bool flag, string msg)> DeleteAsync(int id)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted);
        try
        {
            var entity = await GetForUpdateAsync(connection, transaction, id);
            if (entity == null)
                return await RollbackResult((false, _stringLocalizer["not_exists_entity"]), transaction);
            await using var routeConnection = await _connectionFactory.OpenConnectionAsync();
            var routeSnapshot = await CanonicalInventorySupport.GetRouteAsync(
                routeConnection, entity.goods_location_id);
            _ = await CanonicalInventorySupport.LockRouteAsync(
                connection, transaction, routeSnapshot);
            var qty = await connection.ExecuteAsync(
                "DELETE FROM `wms_stocktaking` WHERE `id`=@id;", new { id }, transaction);
            await transaction.CommitAsync();
            return qty > 0
                ? (true, _stringLocalizer["delete_success"])
                : (false, _stringLocalizer["delete_failed"]);
        }
        catch { await transaction.RollbackAsync(); throw; }
    }

    private static Task<StocktakingEntity?> GetForUpdateAsync(
        IDbConnection connection, IDbTransaction transaction, int id) =>
        connection.QuerySingleOrDefaultAsync<StocktakingEntity>($"""
            SELECT {EntityColumns} FROM `wms_stocktaking` WHERE `id`=@id FOR UPDATE;
            """, new { id }, transaction);

    private static async Task<T> RollbackResult<T>(T result, IDbTransaction transaction)
    {
        if (transaction is System.Data.Common.DbTransaction dbTransaction)
            await dbTransaction.RollbackAsync();
        else transaction.Rollback();
        return result;
    }

    private const string EntityColumns = """
        `id`,`job_code`,`job_status`,`sku_id`,`goods_owner_id`,`goods_location_id`,`series_number`,
        `expiry_date`,`price`,`putaway_date`,`book_qty`,`counted_qty`,`difference_qty`,`creator`,
        `create_time`,`last_update_time`,`erp_stock_id`,`stock_allocation_id`,`handler`,`handle_time`
        """;

    private const string FromSql = """
        FROM `wms_stocktaking` st
        INNER JOIN `wms_sku` sku ON st.`sku_id`=sku.`id`
        INNER JOIN `wms_spu` spu ON sku.`spu_id`=spu.`id`
        INNER JOIN `wms_goodslocation` gsl ON st.`goods_location_id`=gsl.`id`
        LEFT JOIN `wms_goodsowner` gso ON st.`goods_owner_id`=gso.`id`
        LEFT JOIN `wms_stockadjust` adj
          ON st.`id`=adj.`source_table_id` AND adj.`job_type`=1
        """;

    private const string ViewColumns = """
        st.`id`,st.`job_code`,st.`job_status`,(adj.`id` IS NOT NULL) `adjust_status`,
        sku.`id` `sku_id`,sku.`sku_code`,sku.`sku_name`,spu.`spu_code`,spu.`spu_name`,
        st.`goods_location_id`,gsl.`warehouse_name`,gsl.`location_name`,st.`goods_owner_id`,
        COALESCE(gso.`goods_owner_name`,'') `goods_owner_name`,st.`expiry_date`,st.`price`,
        st.`putaway_date`,st.`series_number`,st.`book_qty`,st.`counted_qty`,st.`difference_qty`,
        st.`erp_stock_id`,st.`stock_allocation_id`,
        st.`creator`,st.`create_time`,st.`handler`,st.`handle_time`,st.`last_update_time`
        """;

    private static readonly IReadOnlyDictionary<string, string> SearchColumns =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["id"]="st.`id`", ["job_code"]="st.`job_code`", ["job_status"]="st.`job_status`",
            ["adjust_status"]="(adj.`id` IS NOT NULL)", ["sku_id"]="sku.`id`",
            ["sku_code"]="sku.`sku_code`", ["sku_name"]="sku.`sku_name`",
            ["spu_code"]="spu.`spu_code`", ["spu_name"]="spu.`spu_name`",
            ["goods_location_id"]="st.`goods_location_id`", ["warehouse_name"]="gsl.`warehouse_name`",
            ["location_name"]="gsl.`location_name`", ["goods_owner_id"]="st.`goods_owner_id`",
            ["goods_owner_name"]="gso.`goods_owner_name`", ["expiry_date"]="st.`expiry_date`",
            ["price"]="st.`price`", ["putaway_date"]="st.`putaway_date`",
            ["series_number"]="st.`series_number`", ["book_qty"]="st.`book_qty`",
            ["counted_qty"]="st.`counted_qty`", ["difference_qty"]="st.`difference_qty`",
            ["creator"]="st.`creator`", ["create_time"]="st.`create_time`",
            ["handler"]="st.`handler`", ["handle_time"]="st.`handle_time`",
            ["last_update_time"]="st.`last_update_time`"
        };
}
