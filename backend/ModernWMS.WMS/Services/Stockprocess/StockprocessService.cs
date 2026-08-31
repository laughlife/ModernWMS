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
using MySqlConnector;

namespace ModernWMS.WMS.Services;

/// <summary>库存加工业务服务。</summary>
public class StockprocessService : BaseService<StockprocessEntity>, IStockprocessService
{
    private static readonly IReadOnlyDictionary<string, string> SearchColumns =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["id"] = "m.`id`", ["job_code"] = "m.`job_code`", ["job_type"] = "m.`job_type`",
            ["process_status"] = "m.`process_status`", ["processor"] = "m.`processor`",
            ["process_time"] = "m.`process_time`", ["creator"] = "m.`creator`",
            ["create_time"] = "m.`create_time`", ["last_update_time"] = "m.`last_update_time`",
            ["adjust_status"] = "(m.`process_status` AND EXISTS(SELECT 1 FROM `wms_stockadjust` sa INNER JOIN `wms_stockprocessdetail` sd ON sd.`id`=sa.`source_table_id` WHERE sa.`job_type`=2 AND sd.`stock_process_id`=m.`id`))"
        };

    private const string MasterProjection = """
        m.`id`,m.`job_code`,m.`job_type`,m.`process_status`,m.`processor`,m.`process_time`,
        m.`creator`,m.`create_time`,m.`last_update_time`,
        (m.`process_status` AND EXISTS(
            SELECT 1 FROM `wms_stockadjust` a
            INNER JOIN `wms_stockprocessdetail` d ON d.`id`=a.`source_table_id`
            WHERE a.`job_type`=2 AND d.`stock_process_id`=m.`id`)) AS `adjust_status`
        """;

    private readonly IMySqlConnectionFactory _connectionFactory;
    private readonly IStringLocalizer<Core.MultiLanguage> _stringLocalizer;
    private readonly FunctionHelper _functionHelper;
    private readonly IStockAllocationMutationService _stockMutationService;

    /// <summary>初始化库存加工服务。</summary>
    public StockprocessService(IMySqlConnectionFactory connectionFactory,
        IStringLocalizer<Core.MultiLanguage> stringLocalizer, FunctionHelper functionHelper,
        IStockAllocationMutationService stockMutationService)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        _stringLocalizer = stringLocalizer ?? throw new ArgumentNullException(nameof(stringLocalizer));
        _functionHelper = functionHelper ?? throw new ArgumentNullException(nameof(functionHelper));
        _stockMutationService = stockMutationService ?? throw new ArgumentNullException(nameof(stockMutationService));
    }

    /// <inheritdoc />
    public async Task<(List<StockprocessGetViewModel> data, int totals)> PageAsync(
        PageSearch pageSearch, CurrentUser currentUser)
    {
        var where = DapperSearchBuilder.Build(pageSearch.searchObjects, SearchColumns);
        where.Parameters.Add("offset", Math.Max(0, (pageSearch.pageIndex - 1) * pageSearch.pageSize));
        where.Parameters.Add("pageSize", pageSearch.pageSize);
        var filter = string.IsNullOrWhiteSpace(where.Sql) ? string.Empty : $" AND {where.Sql}";
        await using var connection = await _connectionFactory.OpenConnectionAsync();
        using var grid = await connection.QueryMultipleAsync($"""
            SELECT COUNT(*) FROM `wms_stockprocess` m WHERE 1=1{filter};
            SELECT {MasterProjection} FROM `wms_stockprocess` m
            WHERE 1=1{filter}
            ORDER BY m.`last_update_time` DESC LIMIT @pageSize OFFSET @offset;
            """, where.Parameters);
        var totals = await grid.ReadSingleAsync<int>();
        return ((await grid.ReadAsync<StockprocessGetViewModel>()).AsList(), totals);
    }

    /// <inheritdoc />
    public async Task<List<StockprocessGetViewModel>> GetAllAsync(CurrentUser currentUser)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync();
        return (await connection.QueryAsync<StockprocessGetViewModel>($"""
            SELECT {MasterProjection} FROM `wms_stockprocess` m;
            """)).AsList();
    }

    /// <inheritdoc />
    public async Task<StockprocessWithDetailViewModel?> GetAsync(int id)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync();
        var master = await connection.QuerySingleOrDefaultAsync<StockprocessWithDetailViewModel>($"""
            SELECT {MasterProjection} FROM `wms_stockprocess` m WHERE m.`id`=@id LIMIT 1;
            """, new { id });
        if (master == null) return null;
        var details = (await connection.QueryAsync<StockprocessdetailViewModel>("""
            SELECT d.`id`,d.`stock_process_id`,d.`sku_id`,d.`goods_owner_id`,d.`goods_location_id`,
                d.`qty`,d.`last_update_time`,d.`is_source`,d.`is_update_stock`,
                d.`erp_stock_id`,d.`stock_allocation_id`,
                d.`series_number`,d.`expiry_date`,d.`price`,d.`putaway_date`,
                sku.`sku_code`,spu.`spu_code`,spu.`spu_name`,sku.`unit`,COALESCE(gl.`location_name`,'') `location_name`
            FROM `wms_stockprocessdetail` d
            INNER JOIN `wms_sku` sku ON sku.`id`=d.`sku_id`
            INNER JOIN `wms_spu` spu ON spu.`id`=sku.`spu_id`
            LEFT JOIN `wms_goodslocation` gl ON gl.`id`=d.`goods_location_id`
            WHERE d.`stock_process_id`=@id;
            """, new { id })).AsList();
        master.source_detail_list = details.Where(x => x.is_source).ToList();
        master.target_detail_list = details.Where(x => !x.is_source).ToList();
        return master;
    }

    /// <inheritdoc />
    public async Task<(int id, string msg)> AddAsync(StockprocessViewModel viewModel, CurrentUser currentUser)
    {
        var jobCode = await _functionHelper.GetFormNoAsync("Stockprocess");
        await using var connection = await _connectionFactory.OpenConnectionAsync();
        var details = viewModel.detailList ?? [];
        if (details.Count == 0) return (0, "加工明细不能为空");
        var routeSnapshots = new List<CanonicalInventorySupport.InventoryRoute>();
        foreach (var locationId in details.Select(x => x.goods_location_id).Distinct())
            routeSnapshots.Add(await CanonicalInventorySupport.GetRouteAsync(
                connection, locationId));
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.Serializable);
        try
        {
            await CanonicalInventorySupport.LockRoutesAsync(
                connection, transaction, routeSnapshots);
            var routes = routeSnapshots;
            if (routes.Select(x => new { x.ErpWarehouseId, x.Mode }).Distinct().Count() > 1)
                return await Rollback(0, "加工明细跨越不同ERP仓库或库存运行模式，禁止创建", transaction);
            foreach (var detail in details)
            {
                CanonicalInventorySupport.CanonicalAllocation allocation;
                try
                {
                    allocation = await CanonicalInventorySupport.ResolveAllocationAsync(
                        connection, transaction, detail.sku_id,
                        detail.goods_location_id, detail.goods_owner_id, detail.series_number,
                        detail.expiry_date, detail.price, detail.putaway_date);
                }
                catch (InvalidOperationException ex) when (!detail.is_source)
                {
                    throw new InvalidOperationException(
                        "加工产出无法唯一匹配已有ERP库存分配；ModernWMS不创建缺少货代/部门/订购人来源的ERP POOL，请先通过ERP入库建立目标库存后再加工",
                        ex);
                }
                detail.erp_stock_id = allocation.ErpStockId;
                detail.stock_allocation_id = allocation.AllocationId;
                if (!detail.is_source) continue;
                var lockedQty = await connection.ExecuteScalarAsync<long>("""
                    SELECT COALESCE(SUM(`qty`),0) FROM `wms_stockprocessdetail`
                     WHERE `stock_allocation_id`=@allocationId
                       AND `is_source`=1 AND `is_update_stock`=0;
                    """, new {
                        allocationId = allocation.AllocationId }, transaction);
                if (allocation.AllocatedQty - allocation.OccupiedQty - lockedQty < detail.qty)
                    return await Rollback(0, _stringLocalizer["data_changed"], transaction);
            }

            var now = DateTime.Now;
            var id = await connection.ExecuteScalarAsync<int>("""
                INSERT INTO `wms_stockprocess`
                    (`job_code`,`job_type`,`process_status`,`processor`,`process_time`,`creator`,`create_time`,`last_update_time`)
                VALUES (@jobCode,@jobType,@processStatus,@processor,@processTime,@creator,@now,@now);
                SELECT LAST_INSERT_ID();
                """, new { jobCode, jobType = viewModel.job_type, processStatus = viewModel.process_status,
                    viewModel.processor, viewModel.process_time, creator = currentUser.user_name, now }, transaction);
            foreach (var detail in details)
                await InsertDetailAsync(connection, transaction, id, detail, now);
            await transaction.CommitAsync();
            return id > 0 ? (id, _stringLocalizer["save_success"]) : (0, _stringLocalizer["save_failed"]);
        }
        catch { await transaction.RollbackAsync(); throw; }
    }

    /// <inheritdoc />
    public async Task<(bool flag, string msg)> UpdateAsync(StockprocessViewModel viewModel)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted);
        try
        {
            var exists = await connection.ExecuteScalarAsync<bool>(
                "SELECT EXISTS(SELECT 1 FROM `wms_stockprocess` WHERE `id`=@id FOR UPDATE);",
                new { viewModel.id }, transaction);
            if (!exists) return await Rollback(false, _stringLocalizer["not_exists_entity"], transaction);
            await GateProcessLocationsAsync(connection, transaction, viewModel.id);
            var affected = await connection.ExecuteAsync("""
                UPDATE `wms_stockprocess` SET `job_code`=@job_code,`job_type`=@job_type,
                    `process_status`=@process_status,`processor`=@processor,`process_time`=@process_time,
                    `last_update_time`=@now WHERE `id`=@id;
                """, new { viewModel.id, viewModel.job_code, viewModel.job_type, viewModel.process_status,
                    viewModel.processor, viewModel.process_time, now = DateTime.Now }, transaction);
            await transaction.CommitAsync();
            return affected > 0 ? (true, _stringLocalizer["save_success"]) : (false, _stringLocalizer["save_failed"]);
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
            var exists = await connection.ExecuteScalarAsync<bool>("""
                SELECT EXISTS(SELECT 1 FROM `wms_stockprocess`
                WHERE `id`=@id AND `process_status`=0 FOR UPDATE);
                """, new { id }, transaction);
            if (!exists) return await Rollback(false, _stringLocalizer["delete_failed"], transaction);
            await GateProcessLocationsAsync(connection, transaction, id);
            await connection.ExecuteAsync("DELETE FROM `wms_stockprocessdetail` WHERE `stock_process_id`=@id;", new { id }, transaction);
            var affected = await connection.ExecuteAsync("DELETE FROM `wms_stockprocess` WHERE `id`=@id AND `process_status`=0;", new { id }, transaction);
            await transaction.CommitAsync();
            return affected > 0 ? (true, _stringLocalizer["delete_success"]) : (false, _stringLocalizer["delete_failed"]);
        }
        catch { await transaction.RollbackAsync(); throw; }
    }

    /// <inheritdoc />
    public async Task<(bool flag, string msg)> ConfirmAdjustment(int id, CurrentUser currentUser)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.Serializable);
        try
        {
            var entity = await connection.QuerySingleOrDefaultAsync<StockprocessEntity>("""
                SELECT `id`,`job_code`,`job_type`,`process_status`,`processor`,`process_time`,`creator`,`create_time`,`last_update_time`
                FROM `wms_stockprocess` WHERE `id`=@id FOR UPDATE;
                """, new { id}, transaction);
            if (entity == null) return await Rollback(false, _stringLocalizer["not_exists_entity"], transaction);
            var adjusted = await connection.ExecuteScalarAsync<bool>("""
                SELECT EXISTS(SELECT 1 FROM `wms_stockadjust` a
                INNER JOIN `wms_stockprocessdetail` d ON d.`id`=a.`source_table_id`
                WHERE a.`job_type`=2 AND d.`stock_process_id`=@id);
                """, new { id }, transaction);
            if (adjusted)
                return await Rollback(false, _stringLocalizer["status_changed"], transaction);

            var details = (await connection.QueryAsync<StockprocessdetailEntity>("""
                SELECT `id`,`stock_process_id`,`sku_id`,`goods_owner_id`,`goods_location_id`,`qty`,
                    `last_update_time`,`erp_stock_id`,`stock_allocation_id`,`is_source`,`is_update_stock`,
                    `series_number`,`expiry_date`,`price`,`putaway_date`
                FROM `wms_stockprocessdetail` WHERE `stock_process_id`=@id FOR UPDATE;
                """, new { id}, transaction)).AsList();
            if (details.Count == 0)
                return await Rollback(false, "加工明细不能为空", transaction);
            var adjustmentPutawayDates = details.ToDictionary(x => x.id, x => x.putaway_date);
            await using var routeConnection = await _connectionFactory.OpenConnectionAsync();
            var routes = new List<CanonicalInventorySupport.InventoryRoute>();
            foreach (var locationId in details.Select(x => x.goods_location_id).Distinct())
                routes.Add(await CanonicalInventorySupport.GetRouteAsync(
                    routeConnection, locationId));
            await CanonicalInventorySupport.LockRoutesAsync(
                connection, transaction, routes);
            if (routes.Select(x => new { x.ErpWarehouseId, x.Mode }).Distinct().Count() > 1)
                return await Rollback(false, "加工明细跨越不同ERP仓库或库存运行模式，禁止确认", transaction);
            if (details.Any(x => !x.erp_stock_id.HasValue || !x.stock_allocation_id.HasValue))
                return await Rollback(false, "加工明细未绑定ERP库存分配，旧库存加工路径已停用", transaction);
            await _stockMutationService.PrelockAsync(
                connection, transaction,
                routes.Select(x => x.ErpWarehouseId).Distinct().OrderBy(x => x).ToArray(),
                details.Select(x => x.erp_stock_id!.Value).Distinct().OrderBy(x => x).ToArray(),
                details.Select(x => x.stock_allocation_id!.Value).Distinct().OrderBy(x => x).ToArray());
            var now = DateTime.Now;
            foreach (var detail in details)
            {
                var erpStockId = detail.erp_stock_id
                    ?? throw new InvalidOperationException("加工明细缺少ERP库存引用");
                var stockAllocationId = detail.stock_allocation_id
                    ?? throw new InvalidOperationException("加工明细缺少库位分配引用");
                await _stockMutationService.AdjustAvailableAsync(
                    connection, transaction,
                    CanonicalInventorySupport.Context(
                        routes[0].ErpWarehouseId,
                        $"MWMS:PROC:{id}:{detail.id}",
                        detail.is_source ? "STOCK_PROCESS_CONSUME" : "STOCK_PROCESS_PRODUCE",
                        id, detail.id, currentUser, entity.creator,
                        detail.is_source ? "加工来源扣减" : "加工目标增加"),
                    erpStockId, stockAllocationId,
                    detail.is_source ? -detail.qty : detail.qty);
                await connection.ExecuteAsync("""
                    UPDATE `wms_stockprocessdetail`
                       SET `is_update_stock`=1,`last_update_time`=@now
                     WHERE `id`=@id;
                    """, new { detail.id, now }, transaction);
            }

            var adjustCode = await GetNextCodeAsync(connection, transaction, "wms_stockadjust");
            foreach (var detail in details)
                await connection.ExecuteAsync("""
                    INSERT INTO `wms_stockadjust` (`job_code`,`sku_id`,`goods_owner_id`,`goods_location_id`,`qty`,`creator`,
                        `create_time`,`last_update_time`,`is_update_stock`,`job_type`,`source_table_id`,
                        `erp_stock_id`,`stock_allocation_id`,`series_number`,`expiry_date`,`price`,`putaway_date`)
                    VALUES (@jobCode,@sku_id,@goods_owner_id,@goods_location_id,@qty,@creator,@now,@now,1,2,@id,
                        @erp_stock_id,@stock_allocation_id,@series_number,@expiry_date,@price,@putaway_date);
                    """, new { jobCode = adjustCode, detail.sku_id, detail.goods_owner_id, detail.goods_location_id,
                        qty = detail.is_source ? -detail.qty : detail.qty, creator = currentUser.user_name, now,
                        detail.id, detail.series_number, detail.expiry_date,
                        detail.price, detail.erp_stock_id, detail.stock_allocation_id,
                        putaway_date = adjustmentPutawayDates[detail.id] }, transaction);
            await connection.ExecuteAsync("UPDATE `wms_stockprocess` SET `last_update_time`=@now WHERE `id`=@id;", new { now, id }, transaction);
            await transaction.CommitAsync();
            return (true, _stringLocalizer["operation_success"]);
        }
        catch { await transaction.RollbackAsync(); throw; }
    }

    /// <inheritdoc />
    public async Task<(bool flag, string msg)> ConfirmProcess(int id, CurrentUser currentUser)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted);
        try
        {
            var status = await connection.QuerySingleOrDefaultAsync<bool?>("""
                SELECT `process_status` FROM `wms_stockprocess`
                WHERE `id`=@id FOR UPDATE;
                """, new { id}, transaction);
            if (status == null) return await Rollback(false, _stringLocalizer["not_exists_entity"], transaction);
            if (status.Value) return await Rollback(false, _stringLocalizer["status_changed"], transaction);
            await GateProcessLocationsAsync(connection, transaction, id);
            var now = DateTime.Now;
            var affected = await connection.ExecuteAsync("""
                UPDATE `wms_stockprocess` SET `process_status`=1,`processor`=@processor,
                    `process_time`=@now,`last_update_time`=@now WHERE `id`=@id ;
                """, new { id, now }, transaction);
            await transaction.CommitAsync();
            return affected > 0 ? (true, _stringLocalizer["operation_success"]) : (false, _stringLocalizer["operation_failed"]);
        }
        catch { await transaction.RollbackAsync(); throw; }
    }

    /// <inheritdoc />
    public async Task<string> GetOrderCode(CurrentUser currentUser)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync();
        return await GetNextCodeAsync(connection, null, "wms_stockprocess");
    }

    /// <inheritdoc />
    public async Task<string> GetAdjustOrderCode(CurrentUser currentUser)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync();
        return await GetNextCodeAsync(connection, null, "wms_stockadjust");
    }

    private static Task InsertDetailAsync(MySqlConnection connection, IDbTransaction transaction, int processId,
        StockprocessdetailViewModel detail, DateTime now) => connection.ExecuteAsync("""
            INSERT INTO `wms_stockprocessdetail` (`stock_process_id`,`sku_id`,`goods_owner_id`,`goods_location_id`,`qty`,
                `last_update_time`,`erp_stock_id`,`stock_allocation_id`,`is_source`,`is_update_stock`,
                `series_number`,`expiry_date`,`price`,`putaway_date`)
            VALUES (@processId,@sku_id,@goods_owner_id,@goods_location_id,@qty,@now,@
                @erp_stock_id,@stock_allocation_id,@is_source,@is_update_stock,
                @series_number,@expiry_date,@price,@putaway_date);
            """, new { processId, detail.sku_id, detail.goods_owner_id, detail.goods_location_id, detail.qty, now,
                detail.erp_stock_id, detail.stock_allocation_id, detail.is_source, detail.is_update_stock,
                detail.series_number, detail.expiry_date, detail.price, detail.putaway_date }, transaction);

    private static async Task<string> GetNextCodeAsync(MySqlConnection connection, IDbTransaction? transaction,
        string tableName)
    {
        var maxNo = await connection.QuerySingleOrDefaultAsync<string>(
            $"SELECT MAX(`job_code`) FROM `{tableName}`;", transaction: transaction);
        var date = DateTime.Now.ToString("yyyyMMdd");
        if (string.IsNullOrEmpty(maxNo) || maxNo.Length < 13 || maxNo[..8] != date) return date + "-0001";
        int.TryParse(maxNo.Substring(9, 4), out var number);
        return date + "-" + (number + 1).ToString("0000");
    }

    private static async Task<(T value, string msg)> Rollback<T>(T value, string msg, MySqlTransaction transaction)
    {
        await transaction.RollbackAsync();
        return (value, msg);
    }

    private async Task GateProcessLocationsAsync(
        MySqlConnection connection, IDbTransaction transaction, int processId)
    {
        var locations = (await connection.QueryAsync<ProcessLocationRow>("""
            SELECT DISTINCT `goods_location_id` GoodsLocationId
              FROM `wms_stockprocessdetail`
             WHERE `stock_process_id`=@processId;
            """, new { processId }, transaction)).AsList();
        await using var routeConnection = await _connectionFactory.OpenConnectionAsync();
        var routes = new List<CanonicalInventorySupport.InventoryRoute>();
        foreach (var location in locations)
        {
            routes.Add(await CanonicalInventorySupport.GetRouteAsync(
                routeConnection, location.GoodsLocationId));
        }
        await CanonicalInventorySupport.LockRoutesAsync(connection, transaction, routes);
    }

    private sealed class ProcessLocationRow
    {
        public int GoodsLocationId { get; init; }
    }
}
