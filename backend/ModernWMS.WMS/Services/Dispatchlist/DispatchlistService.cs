using System.Data;
using System.Text.Json;
using Dapper;
using Mapster;
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
using ModernWMS.WMS.Services.Dispatchlist;

namespace ModernWMS.WMS.Services;

public class DispatchlistService : BaseService<DispatchlistEntity>, IDispatchlistService
{
    private const string OutboundDeliveryAuthority = "delivered-delivery";
    private readonly IMySqlConnectionFactory _connectionFactory;
    private readonly IStringLocalizer<Core.MultiLanguage> _stringLocalizer;
    private readonly FunctionHelper _functionHelper;
    private readonly IDispatchSignNotificationClient? _dispatchSignNotificationClient;
    private readonly IStockAllocationMutationService? _stockAllocationMutationService;

    public DispatchlistService(IMySqlConnectionFactory connectionFactory,
        IStringLocalizer<Core.MultiLanguage> stringLocalizer,
        FunctionHelper functionHelper,
        IDispatchSignNotificationClient? dispatchSignNotificationClient = null,
        IStockAllocationMutationService? stockAllocationMutationService = null)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        _stringLocalizer = stringLocalizer ?? throw new ArgumentNullException(nameof(stringLocalizer));
        _functionHelper = functionHelper ?? throw new ArgumentNullException(nameof(functionHelper));
        _dispatchSignNotificationClient = dispatchSignNotificationClient;
        _stockAllocationMutationService = stockAllocationMutationService;
    }

    public async Task<(List<DispatchlistViewModel> data, int totals)> PageAsync(
        PageSearch pageSearch, CurrentUser currentUser)
    {
        var where = DapperSearchBuilder.Build(pageSearch.searchObjects, DispatchSearchColumns);
        where.Parameters.Add("tenantId", currentUser.tenant_id);
        where.Parameters.Add("offset", (pageSearch.pageIndex - 1) * pageSearch.pageSize);
        where.Parameters.Add("pageSize", pageSearch.pageSize);
        var predicates = new List<string> { "d.`tenant_id`=@tenantId" };
        if (!string.IsNullOrWhiteSpace(where.Sql)) predicates.Add(where.Sql);
        var title = pageSearch.sqlTitle ?? string.Empty;
        if (title.Contains("dispatch_status", StringComparison.Ordinal))
        {
            var status = Convert.ToByte(title.Trim().ToLowerInvariant().Replace("dispatch_status", "")
                .Replace("：", "").Replace(":", "").Replace("=", ""));
            predicates.Add("d.`dispatch_status`=@titleStatus");
            where.Parameters.Add("titleStatus", status);
        }
        else if (title == "package")
            predicates.Add("d.`picked_qty`=d.`qty` AND (d.`dispatch_status`=3 OR (d.`package_qty`<d.`picked_qty` AND d.`dispatch_status`=5) OR d.`dispatch_status`=4)");
        else if (title == "weight")
            predicates.Add("d.`picked_qty`=d.`qty` AND ((d.`weighing_qty`<d.`picked_qty` AND d.`dispatch_status`=4) OR d.`dispatch_status`=5)");
        else if (title == "delivery")
            predicates.Add("d.`picked_qty`=d.`qty` AND d.`dispatch_status` IN (3,4,5,6)");
        where.Parameters.Add("title", title);
        var whereSql = string.Join(" AND ", predicates);
        await using var connection = await _connectionFactory.OpenConnectionAsync();
        using var grid = await connection.QueryMultipleAsync($"""
            SELECT COUNT(*) {DispatchJoinSql} WHERE {whereSql};
            SELECT {DispatchViewColumns},
              CASE WHEN @title LIKE '%dispatch_status%' OR (@title='package' AND d.`dispatch_status`=4)
                     OR (@title='weight' AND d.`dispatch_status`=5)
                     OR (@title='delivery' AND d.`dispatch_status`=6) THEN 0 ELSE 1 END `is_todo`
            {DispatchJoinSql} WHERE {whereSql}
            ORDER BY `is_todo` DESC,d.`last_update_time` DESC LIMIT @pageSize OFFSET @offset;
            """, where.Parameters);
        var totals = await grid.ReadSingleAsync<int>();
        return ((await grid.ReadAsync<DispatchlistViewModel>()).AsList(), totals);
    }

    public async Task<List<DispatchlistViewModel>> GetByDispatchlistNo(
        string dispatch_no, CurrentUser currentUser)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync();
        return (await connection.QueryAsync<DispatchlistViewModel>($"""
            SELECT {DispatchViewColumns},sku.`sku_name`,sku.`unit`
            {DispatchJoinSql}
            WHERE d.`dispatch_no`=@dispatch_no AND d.`tenant_id`=@tenantId;
            """, new { dispatch_no, tenantId = currentUser.tenant_id })).AsList();
    }

    public async Task<(bool flag, string msg)> UpdateAsycn(
        List<DispatchlistViewModel> viewModels, CurrentUser currentUser)
    {
        var dispatchNo = viewModels.First().dispatch_no;
        var dispatchStatus = viewModels.First().dispatch_status;
        await using var connection = await _connectionFactory.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.Serializable);
        try
        {
            var entities = (await connection.QueryAsync<DispatchlistEntity>($"""
                SELECT {DispatchColumns} FROM `wms_dispatchlist`
                WHERE `dispatch_no`=@dispatchNo AND `tenant_id`=@tenantId FOR UPDATE;
                """, new { dispatchNo, tenantId = currentUser.tenant_id }, transaction)).AsList();
            if (entities.Any(t => t.dispatch_status is not (0 or 1)))
                return await RollbackResult((false, "[202]" + _stringLocalizer["data_changed"]), transaction);
            var skuIds = viewModels.Select(t => t.sku_id).Distinct().ToArray();
            var skus = skuIds.Length == 0 ? [] : (await connection.QueryAsync<SkuEntity>("""
                SELECT `id`,`sku_code`,`weight`,`volume` FROM `wms_sku` WHERE `id` IN @skuIds;
                """, new { skuIds }, transaction)).AsList();
            var removed = new HashSet<int>();
            var now = DateTime.Now;
            foreach (var vm in viewModels)
            {
                if (vm.id < 0)
                {
                    var entity = entities.FirstOrDefault(t => t.id == -vm.id);
                    if (entity == null) return await DataChanged(transaction);
                    await connection.ExecuteAsync("DELETE FROM `wms_dispatchlist` WHERE `id`=@id;",
                        new { entity.id }, transaction);
                    removed.Add(entity.id);
                }
                else
                {
                    var sku = skus.FirstOrDefault(t => t.id == vm.sku_id);
                    var weight = sku?.weight * vm.qty ?? 0;
                    var volume = sku?.volume * vm.qty ?? 0;
                    if (vm.id > 0)
                    {
                        var entity = entities.FirstOrDefault(t => t.id == vm.id);
                        if (entity == null) return await DataChanged(transaction);
                        entity.sku_id = vm.sku_id;
                        await connection.ExecuteAsync("""
                            UPDATE `wms_dispatchlist` SET `sku_id`=@skuId,`qty`=@qty,`weight`=@weight,
                              `volume`=@volume,`last_update_time`=@now WHERE `id`=@id;
                            """, new { skuId = vm.sku_id, vm.qty, weight, volume, now, vm.id }, transaction);
                    }
                    else
                        await InsertDispatchAsync(connection, transaction, new DispatchlistEntity {
                            dispatch_no=dispatchNo,dispatch_status=dispatchStatus,sku_id=vm.sku_id,qty=vm.qty,
                            weight=weight,volume=volume,creator=currentUser.user_name,create_time=now,
                            last_update_time=now,tenant_id=currentUser.tenant_id });
                }
            }
            var duplicateSkuIds = entities.Where(t => !removed.Contains(t.id)).Select(t => t.sku_id)
                .Concat(viewModels.Where(t => t.id == 0).Select(t => t.sku_id)).GroupBy(t => t)
                .Where(t => t.Count() > 1).Select(t => t.Key).ToArray();
            if (duplicateSkuIds.Length > 0)
            {
                var message = string.Concat(skus.Where(t => duplicateSkuIds.Contains(t.id)).Select(t =>
                    string.Format(_stringLocalizer["exists_entity"], _stringLocalizer["sku_code"], t.sku_code)));
                return await RollbackResult((false, message), transaction);
            }
            await transaction.CommitAsync();
            return (true, _stringLocalizer["save_success"]);
        }
        catch { await transaction.RollbackAsync(); throw; }
    }

    public async Task<List<DispatchpicklistViewModel>> GetPickListByDispatchID(
        int dispatch_id, CurrentUser currentUser)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync();
        return (await connection.QueryAsync<DispatchpicklistViewModel>("""
            SELECT p.`id`,p.`dispatchlist_id`,p.`stock_id`,p.`erp_stock_id`,p.`stock_allocation_id`,
              p.`goods_owner_id`,p.`goods_location_id`,p.`sku_id`,
              p.`pick_qty`,p.`picked_qty`,COALESCE(o.`goods_owner_name`,'') `goods_owner_name`,
              sku.`sku_code`,spu.`spu_code`,spu.`spu_description`,spu.`spu_name`,sku.`bar_code`,
              l.`location_name`,l.`warehouse_area_name`,l.`warehouse_area_property`,l.`warehouse_name`,
              p.`series_number`,p.`expiry_date`,p.`price`,p.`picker`,p.`picker_id`,p.`putaway_date`
            FROM `wms_dispatchpicklist` p
            INNER JOIN `wms_dispatchlist` d ON p.`dispatchlist_id`=d.`id`
            INNER JOIN `wms_sku` sku ON p.`sku_id`=sku.`id`
            INNER JOIN `wms_spu` spu ON sku.`spu_id`=spu.`id`
            LEFT JOIN `wms_goodsowner` o ON p.`goods_owner_id`=o.`id`
            INNER JOIN `wms_goodslocation` l ON p.`goods_location_id`=l.`id`
            WHERE p.`dispatchlist_id`=@dispatch_id AND d.`tenant_id`=@tenantId;
            """, new { dispatch_id, tenantId = currentUser.tenant_id })).AsList();
    }

    public async Task<(List<PreDispatchlistViewModel> data, int totals)> AdvancedDispatchlistPageAsync(
        PageSearch pageSearch, CurrentUser currentUser)
    {
        var where = DapperSearchBuilder.Build(pageSearch.searchObjects, PreDispatchSearchColumns);
        where.Parameters.Add("tenantId", currentUser.tenant_id);
        where.Parameters.Add("offset", (pageSearch.pageIndex - 1) * pageSearch.pageSize);
        where.Parameters.Add("pageSize", pageSearch.pageSize);
        var having = string.IsNullOrWhiteSpace(where.Sql) ? new List<string>() : [where.Sql];
        var title = pageSearch.sqlTitle ?? string.Empty;
        if (title.Contains("dispatch_status", StringComparison.Ordinal))
        {
            var status = Convert.ToByte(title.Trim().ToLowerInvariant().Replace("dispatch_status", "")
                .Replace("：", "").Replace(":", "").Replace("=", ""));
            having.Add("d.`dispatch_status`=@status"); where.Parameters.Add("status", status);
        }
        else if (title == "todo") having.Add("d.`dispatch_status` BETWEEN 2 AND 5");
        var havingSql = having.Count == 0 ? string.Empty : " HAVING " + string.Join(" AND ", having);
        await using var connection = await _connectionFactory.OpenConnectionAsync();
        using var grid = await connection.QueryMultipleAsync($"""
            SELECT COUNT(*) FROM (SELECT d.`dispatch_no`,d.`dispatch_status`,d.`creator`
              FROM `wms_dispatchlist` d WHERE d.`tenant_id`=@tenantId
              GROUP BY d.`dispatch_no`,d.`dispatch_status`,d.`creator`{havingSql}) q;
            SELECT d.`dispatch_no`,d.`dispatch_status`,SUM(d.`qty`) `qty`,d.`creator`,
              SUM(CASE spu.`volume_unit` WHEN 1 THEN d.`volume` WHEN 0 THEN d.`volume`/1000 ELSE d.`volume`*1000 END) `volume`,
              SUM(CASE spu.`weight_unit` WHEN 0 THEN d.`weight`/1000000 WHEN 1 THEN d.`weight`/1000 ELSE d.`weight` END) `weight`
            FROM `wms_dispatchlist` d INNER JOIN `wms_sku` sku ON d.`sku_id`=sku.`id`
              INNER JOIN `wms_spu` spu ON sku.`spu_id`=spu.`id`
            WHERE d.`tenant_id`=@tenantId GROUP BY d.`dispatch_no`,d.`dispatch_status`,d.`creator`{havingSql}
            ORDER BY d.`dispatch_no` DESC LIMIT @pageSize OFFSET @offset;
            """, where.Parameters);
        var totals = await grid.ReadSingleAsync<int>();
        return ((await grid.ReadAsync<PreDispatchlistViewModel>()).AsList(), totals);
    }

    public async Task<List<DispatchlistDetailViewModel>> GetAllAsync(
        string dispatch_no, CurrentUser currentUser)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync();
        return (await connection.QueryAsync<DispatchlistDetailViewModel>($"""
            SELECT {DispatchViewColumns} {DispatchJoinSql}
            WHERE d.`dispatch_no`=@dispatch_no AND d.`tenant_id`=@tenantId AND d.`dispatch_status` IN (0,1);
            """, new { dispatch_no, tenantId = currentUser.tenant_id })).AsList();
    }

    public async Task<(bool flag, string msg)> AddAsync(
        List<DispatchlistAddViewModel> viewModel, CurrentUser currentUser)
    {
        var entities = viewModel.Adapt<List<DispatchlistEntity>>();
        var skuIds = entities.Select(t => t.sku_id).Distinct().ToArray();
        var dispatchNo = await _functionHelper.GetFormNoAsync("Dispatchlist");
        await using var connection = await _connectionFactory.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted);
        try
        {
            var skus = (await connection.QueryAsync<SkuEntity>("""
                SELECT `id`,`weight`,`volume` FROM `wms_sku` WHERE `id` IN @skuIds;
                """, new { skuIds }, transaction)).AsList();
            var now = DateTime.Now;
            foreach (var entity in entities)
            {
                var sku = skus.FirstOrDefault(t => t.id == entity.sku_id);
                entity.dispatch_no=dispatchNo; entity.creator=currentUser.user_name; entity.create_time=now;
                entity.last_update_time=now; entity.tenant_id=currentUser.tenant_id;
                if (sku != null) { entity.weight=entity.qty*sku.weight; entity.volume=entity.qty*sku.volume; }
                await InsertDispatchAsync(connection, transaction, entity);
            }
            await transaction.CommitAsync();
            return entities.Count > 0 ? (true, _stringLocalizer["save_success"]) : (false, _stringLocalizer["save_failed"]);
        }
        catch { await transaction.RollbackAsync(); throw; }
    }

    public async Task<(bool flag, string msg)> PreparePickingAsync(string dispatchNo, int warehouseId,
        int goodsOwnerId, List<DispatchlistAddViewModel> viewModels, CurrentUser currentUser)
    {
        if (string.IsNullOrWhiteSpace(dispatchNo) || dispatchNo.Length > 32 || warehouseId <= 0
            || goodsOwnerId <= 0 || viewModels.Count == 0 || viewModels.Any(t => t.sku_id <= 0 || t.qty <= 0)
            || viewModels.GroupBy(t => t.sku_id).Any(t => t.Count() > 1))
            return (false, "FBA发货单的拣货数据无效");
        await using var connection = await _connectionFactory.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted);
        try
        {
            if (await connection.ExecuteScalarAsync<bool>("""
                SELECT EXISTS(SELECT 1 FROM `wms_dispatchlist`
                WHERE `tenant_id`=@tenantId AND `dispatch_no`=@dispatchNo FOR UPDATE);
                """, new { tenantId=currentUser.tenant_id, dispatchNo }, transaction))
                return await RollbackResult((false, "该FBA发货单已经准备拣货，请勿重复操作"), transaction);
            var runtime=await LoadDispatchRuntimeAsync(connection,transaction,currentUser.tenant_id,warehouseId);
            var skuIds=viewModels.Select(t=>t.sku_id).ToArray();
            var skus=(await connection.QueryAsync<SkuEntity>("SELECT `id`,`sku_code`,`weight`,`volume` FROM `wms_sku` WHERE `id` IN @skuIds;",new{skuIds},transaction)).AsList();
            if(skus.Count!=skuIds.Length) return await RollbackResult((false,"FBA商品未完整匹配到WMS商品资料"),transaction);
            if(runtime.Mode==CanonicalInventoryMode)
                return await PrepareCanonicalPickingAsync(connection,transaction,dispatchNo,warehouseId,
                    goodsOwnerId,viewModels,skus,currentUser,runtime.ErpWarehouseId);
            var now=DateTime.Now;
            foreach(var vm in viewModels)
            {
                var sku=skus.First(t=>t.id==vm.sku_id);
                var id=await InsertDispatchAsync(connection,transaction,new DispatchlistEntity{dispatch_no=dispatchNo,
                    dispatch_status=0,sku_id=vm.sku_id,qty=vm.qty,weight=sku.weight*vm.qty,volume=sku.volume*vm.qty,
                    creator=currentUser.user_name,create_time=now,last_update_time=now,tenant_id=currentUser.tenant_id});
                var remaining=vm.qty;
                var stocks=(await connection.QueryAsync<AvailableStockRow>(AvailableStockSql+"""
                    WHERE s.`tenant_id`=@tenantId AND s.`sku_id`=@skuId AND l.`warehouse_id`=@warehouseId
                      AND s.`goods_owner_id`=@goodsOwnerId HAVING `qty_available`>0
                    ORDER BY `qty_available` DESC FOR UPDATE;
                    """,new{tenantId=currentUser.tenant_id,skuId=vm.sku_id,warehouseId,goodsOwnerId},transaction)).AsList();
                foreach(var stock in stocks)
                {
                    var pickQty=Math.Min(remaining,stock.qty_available); if(pickQty<=0) continue;
                    await InsertPickAsync(connection,transaction,id,stock,pickQty); remaining-=pickQty; if(remaining==0) break;
                }
                if(remaining>0) return await RollbackResult((false,$"商品 {sku.sku_code} 在对应仓库和所属人下的可用库存不足"),transaction);
                await connection.ExecuteAsync("UPDATE `wms_dispatchlist` SET `dispatch_status`=2,`lock_qty`=@qty,`last_update_time`=@now WHERE `id`=@id;",new{vm.qty,now,id},transaction);
            }
            await transaction.CommitAsync(); return (true,"已生成待拣货单");
        }
        catch { await transaction.RollbackAsync(); throw; }
    }

    public async Task<(bool flag, string msg)> DeleteAsync(string dispatch_no, CurrentUser currentUser)
    {
        await using var connection=await _connectionFactory.OpenConnectionAsync();
        await using var transaction=await connection.BeginTransactionAsync(IsolationLevel.Serializable);
        try
        {
            var statuses=(await connection.QueryAsync<byte>("SELECT `dispatch_status` FROM `wms_dispatchlist` WHERE `dispatch_no`=@dispatch_no AND `tenant_id`=@tenantId FOR UPDATE;",new{dispatch_no,tenantId=currentUser.tenant_id},transaction)).AsList();
            if(statuses.Any(t=>t>1)) return await RollbackResult((false,_stringLocalizer["status_not_delete"]),transaction);
            var qty=await connection.ExecuteAsync("DELETE FROM `wms_dispatchlist` WHERE `dispatch_no`=@dispatch_no AND `tenant_id`=@tenantId;",new{dispatch_no,tenantId=currentUser.tenant_id},transaction);
            await transaction.CommitAsync();
            return qty>0?(true,_stringLocalizer["delete_success"]):(false,_stringLocalizer["delete_failed"]);
        }
        catch { await transaction.RollbackAsync(); throw; }
    }

    // Remaining workflow methods are implemented below with the same connection/transaction boundary.

    public async Task<List<DispatchlistConfirmDetailViewModel>> ConfirmOrderCheck(
        string dispatch_no, CurrentUser currentUser)
    {
        await using var connection=await _connectionFactory.OpenConnectionAsync();
        var canonicalEnabled=await connection.ExecuteScalarAsync<bool>("""
            SELECT EXISTS(SELECT 1 FROM `wms_inventory_runtime_config`
             WHERE `tenant_id`=@tenantId AND (`maintenance_enabled`=1 OR `mode`='CANONICAL_ERP'));
            """,new{tenantId=currentUser.tenant_id});
        if(canonicalEnabled)
            throw new InvalidOperationException("统一ERP库存模式下旧版手工库存分配入口已停用，请使用按仓库准备拣货流程");
        var rows=(await connection.QueryAsync<DispatchlistConfirmDetailViewModel>("""
            SELECT d.`id` `dispatchlist_id`,d.`sku_id`,d.`dispatch_no`,sku.`sku_code`,spu.`spu_code`,
              d.`dispatch_status`,spu.`spu_description`,spu.`spu_name`,sku.`bar_code`,d.`qty`
            FROM `wms_dispatchlist` d INNER JOIN `wms_sku` sku ON d.`sku_id`=sku.`id`
              INNER JOIN `wms_spu` spu ON sku.`spu_id`=spu.`id`
            WHERE d.`dispatch_no`=@dispatch_no AND d.`tenant_id`=@tenantId;
            """,new{dispatch_no,tenantId=currentUser.tenant_id})).AsList();
        foreach(var row in rows)
        {
            var stocks=(await connection.QueryAsync<AvailableStockRow>(AvailableStockSql+"""
                WHERE s.`tenant_id`=@tenantId AND s.`sku_id`=@skuId
                ORDER BY `qty_available` DESC;
                """,new{tenantId=currentUser.tenant_id,skuId=row.sku_id})).AsList();
            row.qty_available=stocks.Sum(t=>t.qty_available);
            row.confirm=row.qty<=row.qty_available;
            var picked=0;
            row.pick_list=stocks.Where(stock=>stock.qty_available>0).Select(stock=>
            {
                var qty=Math.Min(row.qty-picked,stock.qty_available); picked+=Math.Max(qty,0);
                return new DispatchlistConfirmPickDetailViewModel{stock_id=stock.stock_id,
                    dispatchlist_id=row.dispatchlist_id,goods_location_id=stock.goods_location_id,
                    warehouse_id=stock.warehouse_id,qty_available=stock.qty_available,
                    goods_owner_id=stock.goods_owner_id,goods_owner_name=stock.goods_owner_name,
                    location_name=stock.location_name,warehouse_area_name=stock.warehouse_area_name,
                    warehouse_name=stock.warehouse_name,pick_qty=Math.Max(qty,0),series_number=stock.series_number,
                    expiry_date=stock.expiry_date,price=stock.price,putaway_date=stock.putaway_date};
            }).ToList();
        }
        return rows;
    }

    public async Task<(bool flag,string msg)> ConfirmOrder(
        List<DispatchlistConfirmDetailViewModel> viewModels,CurrentUser currentUser)
    {
        if(viewModels.Count==0||viewModels.Any(t=>t.dispatchlist_id<=0)
           ||viewModels.Select(t=>t.dispatchlist_id).Distinct().Count()!=viewModels.Count)
            return (false,"[202]"+_stringLocalizer["data_changed"]);
        await using var connection=await _connectionFactory.OpenConnectionAsync();
        await using var transaction=await connection.BeginTransactionAsync(IsolationLevel.Serializable);
        try
        {
            var ids=viewModels.Select(t=>t.dispatchlist_id).ToArray();
            var dispatches=(await connection.QueryAsync<DispatchlistEntity>($"SELECT {DispatchColumns} FROM `wms_dispatchlist` WHERE `id` IN @ids AND `tenant_id`=@tenantId FOR UPDATE;",new{ids,tenantId=currentUser.tenant_id},transaction)).AsList();
            if(dispatches.Count!=ids.Length||dispatches.Any(t=>t.dispatch_status is not (0 or 1))) return await DataChanged(transaction);
            var requestedWarehouseIds=viewModels.Where(t=>t.confirm).SelectMany(t=>t.pick_list)
                .Where(t=>t.pick_qty>0).Select(t=>t.warehouse_id).Where(t=>t>0).Distinct().ToArray();
            foreach(var warehouseId in requestedWarehouseIds)
            {
                var runtime=await LoadDispatchRuntimeAsync(connection,transaction,currentUser.tenant_id,warehouseId);
                if(runtime.Mode==CanonicalInventoryMode)
                    return await RollbackResult((false,"统一ERP库存模式下旧版手工库存分配入口已停用"),transaction);
            }
            var stockIds=viewModels.Where(t=>t.confirm).SelectMany(t=>t.pick_list).Where(t=>t.pick_qty>0).Select(t=>t.stock_id).Distinct().ToArray();
            var stocks=stockIds.Length==0?[]:(await connection.QueryAsync<StockEntity>($"SELECT {StockColumns} FROM `wms_stock` WHERE `id` IN @stockIds AND `tenant_id`=@tenantId FOR UPDATE;",new{stockIds,tenantId=currentUser.tenant_id},transaction)).AsList();
            if(stocks.Count!=stockIds.Length) return await DataChanged(transaction);
            var requestedByStock=viewModels.Where(t=>t.confirm).SelectMany(t=>t.pick_list)
                .Where(t=>t.pick_qty>0).GroupBy(t=>t.stock_id).ToDictionary(t=>t.Key,t=>t.Sum(x=>x.pick_qty));
            foreach(var requested in requestedByStock)
                if(requested.Value>await GetAvailableQuantityAsync(connection,transaction,requested.Key,currentUser.tenant_id))
                    return await DataChanged(transaction);
            var now=DateTime.Now;
            var leftovers=new List<DispatchlistEntity>();
            foreach(var vm in viewModels)
            {
                var dispatch=dispatches.First(t=>t.id==vm.dispatchlist_id);
                if(!vm.confirm)
                {
                    leftovers.Add(new DispatchlistEntity{sku_id=vm.sku_id,dispatch_status=1,qty=vm.qty,tenant_id=currentUser.tenant_id});
                    await connection.ExecuteAsync("DELETE FROM `wms_dispatchlist` WHERE `id`=@id;",new{id=dispatch.id},transaction);
                    continue;
                }
                var selected=vm.pick_list.Where(t=>t.pick_qty>0).ToList();
                var selectedQty=selected.Sum(t=>t.pick_qty);
                if(dispatch.sku_id!=vm.sku_id||vm.pick_list.Any(t=>t.pick_qty<0)||selectedQty<=0||selectedQty>dispatch.qty) return await DataChanged(transaction);
                foreach(var pick in selected)
                {
                    var stock=stocks.FirstOrDefault(t=>t.id==pick.stock_id);
                    if(pick.dispatchlist_id!=dispatch.id||stock==null||stock.sku_id!=dispatch.sku_id||stock.goods_location_id!=pick.goods_location_id
                       ||stock.goods_owner_id!=pick.goods_owner_id||stock.series_number!=pick.series_number
                       ||stock.expiry_date!=pick.expiry_date||stock.price!=pick.price||stock.putaway_date!=pick.putaway_date)
                        return await DataChanged(transaction);
                    await InsertPickAsync(connection,transaction,dispatch.id,AvailableStockRow.From(stock),pick.pick_qty);
                }
                await connection.ExecuteAsync("UPDATE `wms_dispatchlist` SET `dispatch_status`=2,`lock_qty`=@selectedQty,`qty`=@selectedQty,`last_update_time`=@now WHERE `id`=@id;",new{selectedQty,now,id=dispatch.id},transaction);
                if(selectedQty<dispatch.qty) leftovers.Add(new DispatchlistEntity{sku_id=dispatch.sku_id,dispatch_status=1,qty=dispatch.qty-selectedQty,tenant_id=currentUser.tenant_id});
            }
            if(leftovers.Count>0)
            {
                var newNo=await _functionHelper.GetFormNoAsync("Dispatchlist");
                var skuIds=leftovers.Select(t=>t.sku_id).Distinct().ToArray();
                var skus=(await connection.QueryAsync<SkuEntity>("SELECT `id`,`weight`,`volume` FROM `wms_sku` WHERE `id` IN @skuIds;",new{skuIds},transaction)).AsList();
                foreach(var item in leftovers)
                {
                    var sku=skus.FirstOrDefault(t=>t.id==item.sku_id); item.dispatch_no=newNo; item.creator=currentUser.user_name; item.create_time=DateTime.Now;
                    if(sku!=null){item.weight=item.qty*sku.weight;item.volume=item.qty*sku.volume;}
                    await InsertDispatchAsync(connection,transaction,item);
                }
            }
            await transaction.CommitAsync(); return (true,_stringLocalizer["operation_success"]);
        }
        catch { await transaction.RollbackAsync(); throw; }
    }

    public async Task<(bool flag,string msg)> CancelOrderOpration(CancelOrderOprationViewModel viewModel,CurrentUser currentUser)
    {
        await using var connection=await _connectionFactory.OpenConnectionAsync();
        await using var transaction=await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted);
        try
        {
            var ids=(await connection.QueryAsync<int>("SELECT `id` FROM `wms_dispatchlist` WHERE `dispatch_no`=@dispatchNo AND `tenant_id`=@tenantId AND `dispatch_status`=@status FOR UPDATE;",new{dispatchNo=viewModel.dispatch_no,tenantId=currentUser.tenant_id,status=viewModel.dispatch_status},transaction)).AsList();
            if(ids.Count==0)return await RollbackResult((false,_stringLocalizer["status_changed"]),transaction);
            var now=DateTime.Now;int qty;
            if(viewModel.dispatch_status==3)
            {
                qty=await connection.ExecuteAsync("UPDATE `wms_dispatchpicklist` SET `picked_qty`=0,`last_update_time`=@now WHERE `dispatchlist_id` IN @ids; UPDATE `wms_dispatchlist` SET `picked_qty`=0,`last_update_time`=@now,`dispatch_status`=2 WHERE `id` IN @ids AND `dispatch_status`=3;",new{now,ids},transaction);
            }
            else if(viewModel.dispatch_status==2)
            {
                var picks=(await connection.QueryAsync<DispatchpicklistEntity>($"SELECT {PickColumns} FROM `wms_dispatchpicklist` WHERE `dispatchlist_id` IN @ids ORDER BY `id` FOR UPDATE;",new{ids},transaction)).AsList();
                if(picks.Any(x=>x.erp_stock_id is >0||x.stock_allocation_id is >0))
                {
                    if(picks.Any(x=>x.erp_stock_id is null or <=0||x.stock_allocation_id is null or <=0))
                        return await RollbackResult((false,"发货单同时包含新旧库存引用，已拒绝撤销"),transaction);
                    var mutation=_stockAllocationMutationService
                        ??throw new InvalidOperationException("统一ERP库存模式未注册库存分配变更服务，操作已拒绝");
                    var stockWarehouses=(await connection.QueryAsync<ErpStockWarehouseRow>("""
                        SELECT `id` ErpStockId,`warehouse_id` ErpWarehouseId FROM `trk_stock`
                         WHERE `id` IN @stockIds AND `deleted`=b'0';
                        """,new{stockIds=picks.Select(x=>x.erp_stock_id!.Value).Distinct().ToArray()},transaction)).AsList();
                    if(stockWarehouses.Count!=picks.Select(x=>x.erp_stock_id).Distinct().Count())
                        return await RollbackResult((false,"ERP库存引用不存在，已拒绝撤销"),transaction);
                    var warehouseByStock=stockWarehouses.ToDictionary(x=>x.ErpStockId,x=>x.ErpWarehouseId);
                    var releasePrelocks=picks.Select(pick=>new StockReservationPrelockRequest(
                        BuildLegacyDispatchMutationContext(currentUser,warehouseByStock[pick.erp_stock_id!.Value],
                            "DISPATCH_RELEASE",pick.dispatchlist_id,pick.id,pick.erp_stock_id.Value,
                            pick.stock_allocation_id!.Value,pick.pick_qty,$"CANCEL:{viewModel.dispatch_no}",
                            pick.reservation_id,pick.reservation_item_id),pick.erp_stock_id.Value,
                        pick.stock_allocation_id.Value,"UNLOCK")).ToArray();
                    await mutation.PrelockReservationOwnersAsync(connection,transaction,currentUser.tenant_id,
                        stockWarehouses.Select(x=>x.ErpWarehouseId).Distinct().OrderBy(x=>x).ToArray(),releasePrelocks);
                    foreach(var pick in picks.OrderBy(x=>x.erp_stock_id).ThenBy(x=>x.stock_allocation_id).ThenBy(x=>x.id))
                        await mutation.ReleaseAsync(connection,transaction,
                            BuildLegacyDispatchMutationContext(currentUser,warehouseByStock[pick.erp_stock_id!.Value],
                                "DISPATCH_RELEASE",pick.dispatchlist_id,
                                pick.id,pick.erp_stock_id!.Value,pick.stock_allocation_id!.Value,pick.pick_qty,
                                $"CANCEL:{viewModel.dispatch_no}",pick.reservation_id,pick.reservation_item_id),pick.erp_stock_id.Value,
                            pick.stock_allocation_id.Value,pick.pick_qty);
                }
                else
                {
                    var warehouseIds=(await connection.QueryAsync<int>("""
                        SELECT DISTINCT location.`warehouse_id`
                          FROM `wms_dispatchpicklist` pick
                          JOIN `wms_goodslocation` location ON location.`id`=pick.`goods_location_id`
                         WHERE pick.`dispatchlist_id` IN @ids;
                        """,new{ids},transaction)).AsList();
                    foreach(var warehouseId in warehouseIds)
                    {
                        var runtime=await LoadDispatchRuntimeAsync(connection,transaction,currentUser.tenant_id,warehouseId);
                        if(runtime.Mode==CanonicalInventoryMode)
                            return await RollbackResult((false,
                                "统一ERP库存模式检测到未迁移的旧库存锁定，已拒绝撤销"),transaction);
                    }
                }
                qty=await connection.ExecuteAsync("DELETE FROM `wms_dispatchpicklist` WHERE `dispatchlist_id` IN @ids; UPDATE `wms_dispatchlist` SET `lock_qty`=0,`last_update_time`=@now,`dispatch_status`=1 WHERE `id` IN @ids AND `dispatch_status`=2;",new{now,ids},transaction);
            }
            else qty=0;
            await transaction.CommitAsync();return qty>0?(true,_stringLocalizer["operation_success"]):(false,_stringLocalizer["operation_failed"]);
        }
        catch { await transaction.RollbackAsync(); throw; }
    }

    public Task<(bool flag,string msg)> ConfirmPickByDispatchNo(string dispatch_no,CurrentUser currentUser)=>
        ExecutePickTransitionAsync("""
            UPDATE `wms_dispatchpicklist` p INNER JOIN `wms_dispatchlist` d ON p.`dispatchlist_id`=d.`id`
              SET p.`picked_qty`=p.`pick_qty`,p.`last_update_time`=@now
              WHERE d.`dispatch_status`=2 AND d.`dispatch_no`=@dispatch_no AND d.`tenant_id`=@tenantId;
            UPDATE `wms_dispatchlist` SET `picked_qty`=`lock_qty`,`dispatch_status`=3,`last_update_time`=@now,
              `pick_checker`=@userName,`pick_checker_id`=@userId
              WHERE `dispatch_status`=2 AND `dispatch_no`=@dispatch_no AND `tenant_id`=@tenantId;
            """,new{dispatch_no,tenantId=currentUser.tenant_id,now=DateTime.Now,userName=currentUser.user_name,userId=currentUser.user_id});

    public async Task<(bool flag,string msg)> ConfirmPickDetail(List<int> picklist_id,CurrentUser currentUser)=>
        await SetPickerAsync(picklist_id,currentUser,true);

    public async Task<(bool flag,string msg)> CancelConfirmPickDetail(List<int> picklist_id,CurrentUser currentUser)=>
        await SetPickerAsync(picklist_id,currentUser,false);

    public async Task<(bool flag,string msg)> Package(List<DispatchlistPackageViewModel> viewModels,CurrentUser currentUser)
    {
        await using var connection=await _connectionFactory.OpenConnectionAsync();
        await using var transaction=await connection.BeginTransactionAsync(IsolationLevel.Serializable);
        try
        {
            var ids=viewModels.Select(t=>t.id).ToArray();var rows=(await connection.QueryAsync<DispatchlistEntity>($"SELECT {DispatchColumns} FROM `wms_dispatchlist` WHERE `id` IN @ids FOR UPDATE;",new{ids},transaction)).AsList();
            var now=DateTime.Now;var code=GetPackageOrWeightCode();var qty=0;
            foreach(var vm in viewModels)
            {
                var row=rows.FirstOrDefault(t=>t.id==vm.id&&t.dispatch_status==vm.dispatch_status);
                if(row==null)return await DataChanged(transaction);
                if(row.package_qty+vm.package_qty>row.picked_qty)return await RollbackResult((false,"[202]"+_stringLocalizer["unpackgeqty_lessthen"]),transaction);
                qty+=await connection.ExecuteAsync("UPDATE `wms_dispatchlist` SET `last_update_time`=@now,`package_person`=@user,`package_qty`=`package_qty`+@amount,`package_time`=@now,`package_no`=@code,`dispatch_status`=4 WHERE `id`=@id AND `dispatch_status`=@status;",new{now,user=currentUser.user_name,amount=vm.package_qty,code,vm.id,status=vm.dispatch_status},transaction);
            }
            await transaction.CommitAsync();return qty>0?(true,_stringLocalizer["operation_success"]):(false,_stringLocalizer["operation_failed"]);
        }
        catch { await transaction.RollbackAsync(); throw; }
    }

    public async Task<(bool flag,string msg)> Weight(List<DispatchlistWeightViewModel> viewModels,CurrentUser currentUser)
    {
        await using var connection=await _connectionFactory.OpenConnectionAsync();
        await using var transaction=await connection.BeginTransactionAsync(IsolationLevel.Serializable);
        try
        {
            var ids=viewModels.Select(t=>t.id).ToArray();var rows=(await connection.QueryAsync<DispatchlistEntity>($"SELECT {DispatchColumns} FROM `wms_dispatchlist` WHERE `id` IN @ids AND `tenant_id`=@tenantId FOR UPDATE;",new{ids,tenantId=currentUser.tenant_id},transaction)).AsList();
            var now=DateTime.Now;var code=GetPackageOrWeightCode();var qty=0;
            foreach(var vm in viewModels)
            {
                var row=rows.FirstOrDefault(t=>t.id==vm.id&&t.dispatch_status==vm.dispatch_status);if(row==null)return await DataChanged(transaction);
                if(row.weighing_qty+vm.weighing_qty!=row.picked_qty)return await RollbackResult((false,"[202]"+_stringLocalizer["weigh_all_qty"]),transaction);
                if(vm.weighing_weight<=0||vm.weighing_length<=0||vm.weighing_length>9999.99m||vm.weighing_width<=0||vm.weighing_width>9999.99m||vm.weighing_height<=0||vm.weighing_height>9999.99m)
                    return await RollbackResult((false,"[202]"+_stringLocalizer["weigh_measurement_invalid"]),transaction);
                var volume=Math.Round(vm.weighing_length*vm.weighing_width*vm.weighing_height,2,MidpointRounding.AwayFromZero);
                qty+=await connection.ExecuteAsync("""
                    UPDATE `wms_dispatchlist` SET `last_update_time`=@now,`weighing_person`=@user,
                      `weighing_qty`=`weighing_qty`+@amount,`weighing_weight`=`weighing_weight`+@weight,
                      `weighing_length`=@length,`weighing_width`=@width,`weighing_height`=@height,
                      `weighing_volume`=@volume,`weighing_no`=@code,`dispatch_status`=5
                    WHERE `id`=@id AND `dispatch_status`=@status;
                    """,new{now,user=currentUser.user_name,amount=vm.weighing_qty,weight=vm.weighing_weight,
                        length=vm.weighing_length,width=vm.weighing_width,height=vm.weighing_height,volume,code,vm.id,status=vm.dispatch_status},transaction);
            }
            await transaction.CommitAsync();return qty>0?(true,_stringLocalizer["operation_success"]):(false,_stringLocalizer["operation_failed"]);
        }
        catch { await transaction.RollbackAsync(); throw; }
    }

    public async Task<(bool flag,string msg)> Delivery(List<DispatchlistDeliveryViewModel> viewModels,CurrentUser currentUser)
    {
        if(!await HasActionAuthorityAsync(currentUser,OutboundDeliveryAuthority))return(false,"没有出库操作权限");
        var ids=viewModels.Select(t=>t.id).Where(t=>t>0).Distinct().ToArray();
        if(ids.Length==0)return(false,_stringLocalizer["data_changed"]);
        await using var connection=await _connectionFactory.OpenConnectionAsync();
        await using var transaction=await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted);
        try
        {
            var dispatches=(await connection.QueryAsync<DispatchlistEntity>($"SELECT {DispatchColumns} FROM `wms_dispatchlist` WHERE `id` IN @ids AND `tenant_id`=@tenantId AND `dispatch_status`=5 FOR UPDATE;",new{ids,tenantId=currentUser.tenant_id},transaction)).AsList();
            if(dispatches.Count!=ids.Length)return await DataChanged(transaction);
            var picks=(await connection.QueryAsync<DispatchpicklistEntity>($"SELECT {PickColumns} FROM `wms_dispatchpicklist` WHERE `dispatchlist_id` IN @ids ORDER BY `id` FOR UPDATE;",new{ids},transaction)).AsList();
            if(picks.Count==0||picks.Any(t=>t.is_update_stock||t.picked_qty<=0)||dispatches.Any(d=>picks.Where(t=>t.dispatchlist_id==d.id).Sum(t=>t.picked_qty)!=d.picked_qty))return await DataChanged(transaction);
            var now=DateTime.Now;var operatorName=(currentUser.user_name??string.Empty).Trim();if(operatorName.Length>64)operatorName=operatorName[..64];
            var canonical=picks.All(x=>x.erp_stock_id is >0&&x.stock_allocation_id is >0);
            if(!canonical&&picks.Any(x=>x.erp_stock_id is >0||x.stock_allocation_id is >0))
                return await RollbackResult((false,"发货单同时包含新旧库存引用，已拒绝出库"),transaction);
            if(canonical)
            {
                var runtimes=(await connection.QueryAsync<DispatchRuntimeRow>("""
                    SELECT stock.`id` ErpStockId,config.`mode` Mode,
                           config.`maintenance_enabled` MaintenanceEnabled,stock.`warehouse_id` ErpWarehouseId
                      FROM `trk_stock` stock
                      LEFT JOIN `wms_inventory_runtime_config` config
                        ON config.`tenant_id`=@tenantId AND config.`erp_warehouse_id`=stock.`warehouse_id`
                     WHERE stock.`id` IN @stockIds AND stock.`deleted`=b'0';
                    """,new{tenantId=currentUser.tenant_id,
                        stockIds=picks.Select(x=>x.erp_stock_id!.Value).Distinct().ToArray()},transaction)).AsList();
                if(runtimes.Count!=picks.Select(x=>x.erp_stock_id).Distinct().Count()
                    ||runtimes.Any(x=>x.MaintenanceEnabled||x.Mode!=CanonicalInventoryMode))
                    return await RollbackResult((false,"ERP仓库未处于可写的统一库存模式，已拒绝出库"),transaction);
                var mutation=_stockAllocationMutationService
                    ??throw new InvalidOperationException("统一ERP库存模式未注册库存分配变更服务，操作已拒绝");
                var shipPrelocks=picks.Select(pick=>new StockReservationPrelockRequest(
                    BuildLegacyDispatchMutationContext(currentUser,
                        runtimes.Single(x=>x.ErpStockId==pick.erp_stock_id!.Value).ErpWarehouseId,
                        "DISPATCH_SHIP_OUT",pick.dispatchlist_id,pick.id,pick.erp_stock_id.Value,
                        pick.stock_allocation_id!.Value,pick.picked_qty,$"LEGACY:{pick.dispatchlist_id}",
                        pick.reservation_id,pick.reservation_item_id),pick.erp_stock_id.Value,
                    pick.stock_allocation_id.Value,"SHIP_OUT")).ToArray();
                await mutation.PrelockReservationOwnersAsync(connection,transaction,currentUser.tenant_id,
                    runtimes.Select(x=>x.ErpWarehouseId).Distinct().OrderBy(x=>x).ToArray(),shipPrelocks);
                foreach(var pick in picks.OrderBy(x=>x.erp_stock_id).ThenBy(x=>x.stock_allocation_id).ThenBy(x=>x.id))
                    await mutation.ShipLockedAsync(connection,transaction,
                        BuildLegacyDispatchMutationContext(currentUser,
                            runtimes.Single(x=>x.ErpStockId==pick.erp_stock_id!.Value).ErpWarehouseId,
                            "DISPATCH_SHIP_OUT",pick.dispatchlist_id,
                            pick.id,pick.erp_stock_id!.Value,pick.stock_allocation_id!.Value,pick.picked_qty,
                            $"LEGACY:{pick.dispatchlist_id}",pick.reservation_id,pick.reservation_item_id),
                        pick.erp_stock_id.Value,pick.stock_allocation_id.Value,pick.picked_qty);
            }
            else
            {
            var legacyWarehouseIds=(await connection.QueryAsync<int>("""
                SELECT DISTINCT location.`warehouse_id`
                  FROM `wms_dispatchpicklist` pick
                  JOIN `wms_goodslocation` location ON location.`id`=pick.`goods_location_id`
                 WHERE pick.`dispatchlist_id` IN @ids;
                """,new{ids},transaction)).AsList();
            foreach(var warehouseId in legacyWarehouseIds)
            {
                var runtime=await LoadDispatchRuntimeAsync(connection,transaction,currentUser.tenant_id,warehouseId);
                if(runtime.Mode==CanonicalInventoryMode)
                    return await RollbackResult((false,
                        "统一ERP库存模式检测到遗留旧库存拣货明细，已拒绝写入 wms_stock；请先完成锁定迁移"),transaction);
            }
            foreach(var group in picks.GroupBy(t=>new{t.stock_id,t.goods_location_id,t.sku_id,t.goods_owner_id,t.series_number,t.expiry_date,t.price,t.putaway_date}))
            {
                var key=group.Key;
                var stock=key.stock_id>0
                    ?await connection.QuerySingleOrDefaultAsync<StockEntity>($"SELECT {StockColumns} FROM `wms_stock` WHERE `id`=@stockId AND `tenant_id`=@tenantId FOR UPDATE;",new{stockId=key.stock_id,tenantId=currentUser.tenant_id},transaction)
                    :await connection.QueryFirstOrDefaultAsync<StockEntity>($"SELECT {StockColumns} FROM `wms_stock` WHERE `tenant_id`=@tenantId AND `goods_location_id`=@goodsLocationId AND `sku_id`=@skuId AND `goods_owner_id`=@goodsOwnerId AND `series_number`<=>@seriesNumber AND `expiry_date`<=>@expiryDate AND `price`<=>@price AND `putaway_date`<=>@putawayDate ORDER BY `id` LIMIT 1 FOR UPDATE;",new{tenantId=currentUser.tenant_id,goodsLocationId=key.goods_location_id,skuId=key.sku_id,goodsOwnerId=key.goods_owner_id,seriesNumber=key.series_number,expiryDate=key.expiry_date,key.price,putawayDate=key.putaway_date},transaction);
                var total=group.Sum(t=>t.picked_qty);
                if(stock==null||stock.goods_location_id!=key.goods_location_id||stock.sku_id!=key.sku_id||stock.goods_owner_id!=key.goods_owner_id||stock.series_number!=key.series_number||stock.expiry_date!=key.expiry_date||stock.price!=key.price||stock.putaway_date!=key.putaway_date||stock.qty<total)return await DataChanged(transaction);
                var running=stock.qty;
                foreach(var pick in group.OrderBy(t=>t.id))
                {
                    var after=running-pick.picked_qty;
                    var cycle=await connection.ExecuteScalarAsync<int>("SELECT COUNT(*)+1 FROM `wms_stock_record` WHERE `tenant_id`=@tenantId AND `biz_id`=@dispatchId AND `biz_item_id`=@pickId AND `stock_id`=@stockId AND `biz_type` LIKE 'DISPATCH_OUT%';",new{tenantId=currentUser.tenant_id,dispatchId=pick.dispatchlist_id,pickId=pick.id,stockId=stock.id},transaction);
                    var bizType=cycle==1?"DISPATCH_OUT":$"DISPATCH_OUT_{cycle}";
                    await connection.ExecuteAsync("""
                        INSERT INTO `wms_stock_record` (`record_no`,`biz_type`,`biz_id`,`biz_item_id`,`stock_id`,`sku_id`,
                          `goods_location_id`,`goods_owner_id`,`change_qty`,`before_qty`,`after_qty`,`direction`,
                          `operator_id`,`operator_name`,`remark`,`operate_time`,`tenant_id`)
                        VALUES (@recordNo,@bizType,@dispatchId,@pickId,@stockId,@skuId,@locationId,@ownerId,
                          @changeQty,@beforeQty,@afterQty,'OUT',@operatorId,@operatorName,'发货单确认出库',@now,@tenantId);
                        """,new{recordNo=$"MWMS-DO-{pick.dispatchlist_id}-{pick.id}-{cycle}",bizType,
                            dispatchId=pick.dispatchlist_id,pickId=pick.id,stockId=stock.id,skuId=pick.sku_id,
                            locationId=pick.goods_location_id,ownerId=pick.goods_owner_id,changeQty=-pick.picked_qty,
                            beforeQty=running,afterQty=after,operatorId=currentUser.user_id,operatorName,now,tenantId=currentUser.tenant_id},transaction);
                    running=after;
                }
                var affected=await connection.ExecuteAsync("UPDATE `wms_stock` SET `qty`=`qty`-@total,`last_update_time`=@now WHERE `id`=@id AND `qty`>=@total;",new{total,now,id=stock.id},transaction);
                if(affected!=1)return await DataChanged(transaction);
            }
            }
            var qty=await connection.ExecuteAsync("UPDATE `wms_dispatchlist` SET `last_update_time`=@now,`dispatch_status`=6,`lock_qty`=0,`actual_qty`=`picked_qty`,`intrasit_qty`=`picked_qty` WHERE `id` IN @ids AND `dispatch_status`=5; UPDATE `wms_dispatchpicklist` SET `is_update_stock`=1,`last_update_time`=@now WHERE `dispatchlist_id` IN @ids AND `is_update_stock`=0;",new{now,ids},transaction);
            await transaction.CommitAsync();return qty>0?(true,_stringLocalizer["operation_success"]):(false,_stringLocalizer["operation_failed"]);
        }
        catch(MySqlConnector.MySqlException){await transaction.RollbackAsync();return(false,"[202]"+_stringLocalizer["data_changed"]);}
        catch{await transaction.RollbackAsync();throw;}
    }

    public async Task<(bool flag,string msg)> SetFreightfee(List<DispatchlistFreightfeeViewModel> viewModels)
    {
        await using var connection=await _connectionFactory.OpenConnectionAsync();
        await using var transaction=await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted);
        try
        {
            var ids=viewModels.Select(t=>t.id).ToArray();var feeIds=viewModels.Select(t=>t.freightfee_id).Distinct().ToArray();
            var rows=(await connection.QueryAsync<DispatchlistEntity>($"SELECT {DispatchColumns} FROM `wms_dispatchlist` WHERE `id` IN @ids FOR UPDATE;",new{ids},transaction)).AsList();
            var fees=(await connection.QueryAsync<FreightfeeEntity>("SELECT `id`,`carrier`,`price_per_weight`,`price_per_volume`,`min_payment` FROM `wms_freightfee` WHERE `id` IN @feeIds;",new{feeIds},transaction)).AsList();
            var qty=0;var now=DateTime.Now;
            foreach(var row in rows)
            {
                var vm=viewModels.FirstOrDefault(t=>t.id==row.id);var fee=vm==null?null:fees.FirstOrDefault(t=>t.id==vm.freightfee_id);if(vm==null||fee==null)continue;
                var amount=row.weighing_no!=""?Math.Max(row.weighing_weight*fee.price_per_weight,fee.min_payment):Math.Max(Math.Max(row.weight*fee.price_per_weight,row.volume*fee.price_per_volume),fee.min_payment);
                qty+=await connection.ExecuteAsync("UPDATE `wms_dispatchlist` SET `last_update_time`=@now,`carrier`=@carrier,`waybill_no`=@waybill,`freightfee`=@amount WHERE `id`=@id;",new{now,fee.carrier,waybill=vm.waybill_no,amount,id=row.id},transaction);
            }
            await transaction.CommitAsync();return qty>0?(true,_stringLocalizer["operation_success"]):(false,_stringLocalizer["operation_failed"]);
        }
        catch{await transaction.RollbackAsync();throw;}
    }

    public async Task<(bool flag,string msg)> SignForArrival(List<DispatchlistSignViewModel> viewModels)
    {
        await using var connection=await _connectionFactory.OpenConnectionAsync();
        await using var transaction=await connection.BeginTransactionAsync(IsolationLevel.Serializable);
        List<DispatchlistEntity> rows;
        try
        {
            var ids=viewModels.Select(t=>t.id).ToArray();rows=(await connection.QueryAsync<DispatchlistEntity>($"SELECT {DispatchColumns} FROM `wms_dispatchlist` WHERE `id` IN @ids FOR UPDATE;",new{ids},transaction)).AsList();var qty=0;var now=DateTime.Now;
            foreach(var row in rows)
            {
                var vm=viewModels.FirstOrDefault(t=>t.id==row.id&&t.dispatch_status==row.dispatch_status);if(vm==null)return await DataChanged(transaction);
                qty+=await connection.ExecuteAsync("UPDATE `wms_dispatchlist` SET `sign_qty`=`actual_qty`-@damage,`damage_qty`=@damage,`last_update_time`=@now,`dispatch_status`=7 WHERE `id`=@id AND `dispatch_status`=@status;",new{damage=vm.damage_qty,now,id=row.id,status=row.dispatch_status},transaction);
            }
            await transaction.CommitAsync();if(qty<=0)return(false,_stringLocalizer["operation_failed"]);
        }
        catch{await transaction.RollbackAsync();throw;}
        if(_dispatchSignNotificationClient!=null)
            foreach(var dispatchNo in rows.Select(t=>t.dispatch_no).Where(t=>!string.IsNullOrWhiteSpace(t)).Distinct())await _dispatchSignNotificationClient.NotifySignedAsync(dispatchNo);
        return(true,_stringLocalizer["operation_success"]);
    }

    public async Task<string> GetOrderCode(CurrentUser currentUser)
    {
        await using var connection=await _connectionFactory.OpenConnectionAsync();
        var maxNo=await connection.ExecuteScalarAsync<string?>("SELECT MAX(`dispatch_no`) FROM `wms_dispatchlist` WHERE `tenant_id`=@tenantId;",new{tenantId=currentUser.tenant_id});
        var date=DateTime.Now.ToString("yyyyMMdd");if(maxNo==null)return date+"-0001";
        var maxDate=maxNo.Substring(0,8);var maxDateNo=maxNo.Substring(9,4);if(date!=maxDate)return date+"-0001";int.TryParse(maxDateNo,out var number);return date+"-"+(number+1).ToString("0000");
    }

    public async Task<(bool flag,string msg)> Import(List<DispatchlistImportViewModel> viewModels,CurrentUser currentUser)
    {
        var codes=viewModels.Select(t=>t.sku_code).Distinct().ToArray();var groups=viewModels.Select(t=>t.import_group).Distinct().ToList();var groupCodes=await _functionHelper.GetFormNoListAsync("Dispatchlist",groups.Count);var map=groups.Select((g,i)=>(g,i)).ToDictionary(t=>t.g,t=>groupCodes[t.i]);
        await using var connection=await _connectionFactory.OpenConnectionAsync();await using var transaction=await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted);
        try
        {
            var skus=(await connection.QueryAsync<SkuEntity>("SELECT sku.`id`,sku.`sku_code`,sku.`weight`,sku.`volume` FROM `wms_sku` sku INNER JOIN `wms_spu` spu ON sku.`spu_id`=spu.`id` WHERE spu.`tenant_id`=@tenantId AND sku.`sku_code` IN @codes;",new{tenantId=currentUser.tenant_id,codes},transaction)).AsList();var now=DateTime.Now;var qty=0;
            foreach(var vm in viewModels)
            {
                var sku=skus.FirstOrDefault(t=>t.sku_code==vm.sku_code);if(sku==null)return await RollbackResult((false,_stringLocalizer["sku_name"]+":"+vm.sku_name+"-"+_stringLocalizer["sku_code"]+":"+vm.sku_code+" "+_stringLocalizer["not_exists_entity"]),transaction);
                await InsertDispatchAsync(connection,transaction,new DispatchlistEntity{sku_id=sku.id,qty=vm.qty,creator=currentUser.user_name,create_time=now,last_update_time=now,tenant_id=currentUser.tenant_id,dispatch_no=map[vm.import_group]});qty++;
            }
            await transaction.CommitAsync();return qty>0?(true,_stringLocalizer["save_success"]):(false,_stringLocalizer["save_failed"]);
        }
        catch{await transaction.RollbackAsync();throw;}
    }

    public async Task<List<string>> GetOrderCodeList(CurrentUser currentUser,int cnt)
    {
        await using var connection=await _connectionFactory.OpenConnectionAsync();var maxNo=await connection.ExecuteScalarAsync<string?>("SELECT MAX(`dispatch_no`) FROM `wms_dispatchlist` WHERE `tenant_id`=@tenantId;",new{tenantId=currentUser.tenant_id});var date=DateTime.Now.ToString("yyyyMMdd");var result=new List<string>();
        if(maxNo==null){for(var i=1;i<=cnt;i++)result.Add(date+"-"+cnt.ToString("0000"));return result;}
        var maxDate=maxNo.Substring(0,8);var maxDateNo=maxNo.Substring(9,4);int.TryParse(maxDateNo,out var number);for(var i=1;i<=cnt;i++)result.Add(date+"-"+(date==maxDate?number+cnt:cnt).ToString("0000"));return result;
    }

    public string GetPackageOrWeightCode(){var date=DateTime.Now.ToString("yyyyMMdd");var start=new DateTime(1970,1,1,8,0,0);var stamp=Convert.ToInt32(DateTime.Now.Subtract(start).TotalSeconds);return date+stamp;}

    private async Task<bool> HasActionAuthorityAsync(CurrentUser currentUser,string requiredAuthority)
    {
        var role=(currentUser.user_role??string.Empty).Trim();if(string.Equals(role,"admin",StringComparison.OrdinalIgnoreCase))return true;
        await using var connection=await _connectionFactory.OpenConnectionAsync();
        var roleId=await connection.QuerySingleOrDefaultAsync<int?>("SELECT `id` FROM `wms_userrole` WHERE `tenant_id`=@tenantId AND `is_valid`=1 AND `role_name`=@role LIMIT 1;",new{tenantId=currentUser.tenant_id,role});if(!roleId.HasValue)return false;
        var values=await connection.QueryAsync<string>("SELECT `menu_actions_authority` FROM `wms_rolemenu` WHERE `tenant_id`=@tenantId AND `userrole_id`=@roleId AND `authority`=1;",new{tenantId=currentUser.tenant_id,roleId=roleId.Value});
        return values.Any(value=>{try{return(JsonSerializer.Deserialize<List<string>>(value)??[]).Any(action=>string.Equals(action?.Trim(),requiredAuthority,StringComparison.Ordinal));}catch(JsonException){return false;}});
    }

    private async Task<(bool flag,string msg)> SetPickerAsync(List<int> ids,CurrentUser currentUser,bool assign)
    {
        if(ids.Count==0)return(false,_stringLocalizer["operation_failed"]);
        await using var connection=await _connectionFactory.OpenConnectionAsync();await using var transaction=await connection.BeginTransactionAsync(IsolationLevel.Serializable);
        try
        {
            var rows=(await connection.QueryAsync<DispatchpicklistEntity>($"SELECT {PickColumns} FROM `wms_dispatchpicklist` WHERE `id` IN @ids FOR UPDATE;",new{ids},transaction)).AsList();
            if(assign?rows.Any(t=>t.picker_id>0||t.picked_qty>0):rows.Any(t=>t.picker_id==0||t.picked_qty>0))return await RollbackResult((false,_stringLocalizer["data_changed"]),transaction);
            var qty=await connection.ExecuteAsync(assign?"UPDATE `wms_dispatchpicklist` SET `picker`=@user,`picker_id`=@userId WHERE `id` IN @ids;":"UPDATE `wms_dispatchpicklist` SET `picker`='',`picker_id`=0 WHERE `id` IN @ids;",new{user=currentUser.user_name,userId=currentUser.user_id,ids},transaction);
            await transaction.CommitAsync();return qty>0?(true,_stringLocalizer["operation_success"]):(false,_stringLocalizer["operation_failed"]);
        }
        catch{await transaction.RollbackAsync();throw;}
    }

    private async Task<(bool flag,string msg)> ExecutePickTransitionAsync(string sql,object parameters)
    {
        await using var connection=await _connectionFactory.OpenConnectionAsync();await using var transaction=await connection.BeginTransactionAsync(IsolationLevel.Serializable);
        try{var qty=await connection.ExecuteAsync(sql,parameters,transaction);await transaction.CommitAsync();return qty>0?(true,_stringLocalizer["operation_success"]):(false,_stringLocalizer["operation_failed"]);}catch{await transaction.RollbackAsync();throw;}
    }

    private async Task<(bool flag,string msg)> DataChanged(IDbTransaction transaction)=>
        await RollbackResult((false,"[202]"+_stringLocalizer["data_changed"]),transaction);

    private const string LegacyInventoryMode="LEGACY_READ";
    private const string CanonicalInventoryMode="CANONICAL_ERP";

    private async Task<(bool flag,string msg)> PrepareCanonicalPickingAsync(
        IDbConnection connection,IDbTransaction transaction,string dispatchNo,int warehouseId,int goodsOwnerId,
        IReadOnlyCollection<DispatchlistAddViewModel> viewModels,IReadOnlyCollection<SkuEntity> skus,
        CurrentUser user,long erpWarehouseId)
    {
        var candidates=(await connection.QueryAsync<CanonicalAvailableStockRow>("""
            SELECT allocation.`id` StockAllocationId,allocation.`erp_stock_id` ErpStockId,
                   map.`wms_sku_id` SkuId,allocation.`goods_location_id` GoodsLocationId,
                   allocation.`goods_owner_id` GoodsOwnerId,allocation.`series_number` SeriesNumber,
                   allocation.`expiry_date` ExpiryDate,allocation.`price` Price,
                   allocation.`putaway_date` PutawayDate,
                   allocation.`allocated_qty`-allocation.`occupied_qty` QtyAvailable
              FROM `wms_erp_stock_allocation` allocation
              JOIN `trk_stock` stock ON stock.`id`=allocation.`erp_stock_id`
                AND stock.`warehouse_id`=@erpWarehouseId AND stock.`deleted`=b'0'
              JOIN `wms_erp_commodity_map` map ON map.`tenant_id`=allocation.`tenant_id`
                AND map.`erp_commodity_id`=stock.`commodity_id` AND map.`wms_sku_id` IN @skuIds
              JOIN `wms_goodslocation` location ON location.`id`=allocation.`goods_location_id`
                AND location.`warehouse_id`=@warehouseId AND location.`is_valid`=1
                AND location.`warehouse_area_property`<>5
             WHERE allocation.`tenant_id`=@tenantId AND allocation.`goods_owner_id`=@goodsOwnerId
               AND allocation.`location_state`='ACTIVE'
               AND allocation.`allocated_qty`>allocation.`occupied_qty`
             ORDER BY map.`wms_sku_id`,QtyAvailable DESC,allocation.`id`;
            """,new{tenantId=user.tenant_id,erpWarehouseId,warehouseId,goodsOwnerId,
                skuIds=viewModels.Select(x=>x.sku_id).ToArray()},transaction)).AsList();
        var plans=new List<CanonicalPickPlan>();
        foreach(var vm in viewModels)
        {
            var remaining=vm.qty;
            foreach(var candidate in candidates.Where(x=>x.SkuId==vm.sku_id))
            {
                var quantity=checked((int)Math.Min((long)remaining,candidate.QtyAvailable));
                if(quantity<=0)continue;
                plans.Add(new CanonicalPickPlan(vm,candidate,quantity));
                remaining-=quantity;
                if(remaining==0)break;
            }
            if(remaining>0)
            {
                var sku=skus.First(x=>x.id==vm.sku_id);
                return await RollbackResult((false,$"商品 {sku.sku_code} 在对应仓库和所属人下的可用库存不足"),transaction);
            }
        }
        var now=DateTime.Now;var detailIds=new Dictionary<int,int>();
        foreach(var vm in viewModels)
        {
            var sku=skus.First(x=>x.id==vm.sku_id);
            detailIds[vm.sku_id]=await InsertDispatchAsync(connection,transaction,new DispatchlistEntity{
                dispatch_no=dispatchNo,dispatch_status=0,sku_id=vm.sku_id,qty=vm.qty,
                weight=sku.weight*vm.qty,volume=sku.volume*vm.qty,creator=user.user_name,
                create_time=now,last_update_time=now,tenant_id=user.tenant_id});
        }
        var mutation=_stockAllocationMutationService
            ?? throw new InvalidOperationException("统一ERP库存模式未注册库存分配变更服务，操作已拒绝");
        var reservePrelocks=plans.Select(plan=>
        {
            var detailId=detailIds[plan.Request.sku_id];
            var context=BuildLegacyDispatchMutationContext(user,erpWarehouseId,"DISPATCH_LOCK",detailId,
                plan.Stock.StockAllocationId,plan.Stock.ErpStockId,plan.Stock.StockAllocationId,
                plan.Quantity,dispatchNo);
            return new StockReservationPrelockRequest(context,plan.Stock.ErpStockId,
                plan.Stock.StockAllocationId,"LOCK");
        }).ToArray();
        await mutation.PrelockReservationOwnersAsync(connection,transaction,user.tenant_id,
            [erpWarehouseId],reservePrelocks);
        foreach(var plan in plans.OrderBy(x=>x.Stock.ErpStockId).ThenBy(x=>x.Stock.StockAllocationId))
        {
            var detailId=detailIds[plan.Request.sku_id];
            var reservationResult=await mutation.ReserveAsync(connection,transaction,
                BuildLegacyDispatchMutationContext(user,erpWarehouseId,"DISPATCH_LOCK",detailId,
                    plan.Stock.StockAllocationId,plan.Stock.ErpStockId,plan.Stock.StockAllocationId,
                    plan.Quantity,dispatchNo),
                plan.Stock.ErpStockId,plan.Stock.StockAllocationId,plan.Quantity);
            await connection.ExecuteAsync("""
                INSERT INTO `wms_dispatchpicklist`
                  (`dispatchlist_id`,`packing_task_item_id`,`stock_id`,`erp_stock_id`,`stock_allocation_id`,
                   `reservation_id`,`reservation_item_id`,
                   `goods_owner_id`,`goods_location_id`,`sku_id`,`pick_qty`,`picked_qty`,`is_update_stock`,
                   `last_update_time`,`series_number`,`picker_id`,`picker`,`expiry_date`,`price`,`putaway_date`)
                VALUES (@detailId,NULL,0,@erpStockId,@allocationId,@reservationId,@reservationItemId,
                   @ownerId,@locationId,@skuId,
                   @quantity,0,0,@now,@series,0,'',@expiry,@price,@putaway);
                """,new{detailId,erpStockId=plan.Stock.ErpStockId,allocationId=plan.Stock.StockAllocationId,
                    reservationId=reservationResult.ReservationId,reservationItemId=reservationResult.ReservationItemId,
                    ownerId=plan.Stock.GoodsOwnerId,locationId=plan.Stock.GoodsLocationId,
                    skuId=plan.Stock.SkuId,plan.Quantity,now,series=plan.Stock.SeriesNumber,
                    expiry=plan.Stock.ExpiryDate??ModernWMS.Core.Utility.UtilConvert.MinDate,
                    plan.Stock.Price,putaway=plan.Stock.PutawayDate??ModernWMS.Core.Utility.UtilConvert.MinDate},transaction);
        }
        foreach(var vm in viewModels)
            await connection.ExecuteAsync("""
                UPDATE `wms_dispatchlist` SET `dispatch_status`=2,`lock_qty`=@qty,`last_update_time`=@now
                 WHERE `id`=@id;
                """,new{vm.qty,now,id=detailIds[vm.sku_id]},transaction);
        if(transaction is System.Data.Common.DbTransaction db)await db.CommitAsync();else transaction.Commit();
        return(true,"已生成待拣货单");
    }

    private static async Task<DispatchRuntimeRow> LoadDispatchRuntimeAsync(
        IDbConnection connection,IDbTransaction transaction,long tenantId,int warehouseId)
    {
        var erpWarehouseId=await connection.QuerySingleOrDefaultAsync<long?>("""
            SELECT `erp_warehouse_id` FROM `wms_warehouse`
             WHERE `id`=@warehouseId AND `is_valid`=1 LIMIT 1;
            """,new{warehouseId},transaction)
            ?? throw new InvalidOperationException("仓库不存在或未映射ERP仓库");
        var runtime=await connection.QuerySingleOrDefaultAsync<DispatchRuntimeRow>("""
            SELECT `mode` Mode,`maintenance_enabled` MaintenanceEnabled,
                   @erpWarehouseId ErpWarehouseId
              FROM `wms_inventory_runtime_config`
             WHERE `tenant_id`=@tenantId AND `erp_warehouse_id`=@erpWarehouseId FOR UPDATE;
            """,new{tenantId,erpWarehouseId},transaction)
            ??new DispatchRuntimeRow{Mode=LegacyInventoryMode,ErpWarehouseId=erpWarehouseId};
        if(runtime.MaintenanceEnabled)
            throw new InvalidOperationException($"ERP仓库 {erpWarehouseId} 正处于库存维护窗口，出库操作已暂停");
        if(runtime.Mode is not(LegacyInventoryMode or CanonicalInventoryMode))
            throw new InvalidOperationException("库存运行模式无效，操作已拒绝");
        return runtime;
    }

    private static StockMutationContext BuildLegacyDispatchMutationContext(CurrentUser user,long erpWarehouseId,string bizType,
        long bizId,long bizItemId,long erpStockId,long allocationId,long quantity,string requestIdentity,
        long? reservationId=null,long? reservationItemId=null)
    {
        var key=DispatchWorkflow.DispatchWorkflowService.HashText(
            $"{bizType}:{bizId}:{bizItemId}:{erpStockId}:{allocationId}:{quantity}:{requestIdentity}");
        var operatorName=string.IsNullOrWhiteSpace(user.user_name)?$"用户{user.user_id}":user.user_name.Trim();
        if(operatorName.Length>64)operatorName=operatorName[..64];
        return new StockMutationContext(user.tenant_id,erpWarehouseId,key,bizType,bizId,bizItemId,user.user_id,
            operatorName,bizType,new StockReservationMutationContext(
                "WMS_RESERVATION_V1",key,"MODERN_WMS","LEGACY_DISPATCH",bizId,null,
                null,null,"DISPATCH_PICK",bizItemId,$"DISPATCH:{bizId}:{bizItemId}:{allocationId}",
                reservationId,reservationItemId));
    }

    private static async Task<T> RollbackResult<T>(T result,IDbTransaction transaction)
    {
        if(transaction is System.Data.Common.DbTransaction db)await db.RollbackAsync();else transaction.Rollback();return result;
    }

    private static async Task<int> InsertDispatchAsync(IDbConnection connection,IDbTransaction transaction,DispatchlistEntity e)=>
        await connection.ExecuteScalarAsync<int>("""
            INSERT INTO `wms_dispatchlist`
             (`dispatch_order_id`,`packing_task_id`,`packing_task_item_id`,`dispatch_no`,`dispatch_status`,`sku_id`,`qty`,`weight`,`volume`,`creator`,`create_time`,
              `damage_qty`,`lock_qty`,`picked_qty`,`intrasit_qty`,`package_qty`,`weighing_qty`,`actual_qty`,`sign_qty`,`package_no`,`package_person`,`package_time`,
              `weighing_no`,`weighing_person`,`weighing_weight`,`weighing_length`,`weighing_width`,`weighing_height`,`weighing_volume`,`waybill_no`,`carrier`,
              `carrier_warehouse_id`,`carrier_unit`,`volume_divisor`,`freightfee`,`last_update_time`,`tenant_id`,`pick_checker_id`,`pick_checker`)
            VALUES
             (@dispatch_order_id,@packing_task_id,@packing_task_item_id,@dispatch_no,@dispatch_status,@sku_id,@qty,@weight,@volume,@creator,@create_time,
              @damage_qty,@lock_qty,@picked_qty,@intrasit_qty,@package_qty,@weighing_qty,@actual_qty,@sign_qty,@package_no,@package_person,@package_time,
              @weighing_no,@weighing_person,@weighing_weight,@weighing_length,@weighing_width,@weighing_height,@weighing_volume,@waybill_no,@carrier,
              @carrier_warehouse_id,@carrier_unit,@volume_divisor,@freightfee,@last_update_time,@tenant_id,@pick_checker_id,@pick_checker);
            SELECT LAST_INSERT_ID();
            """,e,transaction);

    private static Task<int> InsertPickAsync(IDbConnection connection,IDbTransaction transaction,int dispatchId,AvailableStockRow stock,int qty)=>
        connection.ExecuteAsync("""
            INSERT INTO `wms_dispatchpicklist`
             (`dispatchlist_id`,`packing_task_item_id`,`stock_id`,`goods_owner_id`,`goods_location_id`,`sku_id`,`pick_qty`,`picked_qty`,`is_update_stock`,
              `last_update_time`,`series_number`,`picker_id`,`picker`,`expiry_date`,`price`,`putaway_date`)
            VALUES (@dispatchId,NULL,@stockId,@ownerId,@locationId,@skuId,@qty,0,0,@now,@seriesNumber,0,'',@expiryDate,@price,@putawayDate);
            """,new{dispatchId,stockId=stock.stock_id,ownerId=stock.goods_owner_id,locationId=stock.goods_location_id,skuId=stock.sku_id,qty,now=DateTime.Now,seriesNumber=stock.series_number,expiryDate=stock.expiry_date,stock.price,putawayDate=stock.putaway_date},transaction);

    private static async Task<int> GetAvailableQuantityAsync(IDbConnection connection,IDbTransaction transaction,int stockId,long tenantId)
    {
        var row=await connection.QuerySingleOrDefaultAsync<AvailableStockRow>(AvailableStockSql+" WHERE s.`id`=@stockId AND s.`tenant_id`=@tenantId;",new{stockId,tenantId},transaction);return row?.qty_available??int.MinValue;
    }

    private sealed class AvailableStockRow
    {
        public int stock_id{get;set;} public int sku_id{get;set;} public int goods_location_id{get;set;} public int warehouse_id{get;set;}
        public int goods_owner_id{get;set;} public string goods_owner_name{get;set;}=string.Empty;public string location_name{get;set;}=string.Empty;
        public string warehouse_area_name{get;set;}=string.Empty;public string warehouse_name{get;set;}=string.Empty;public int qty_available{get;set;}
        public string series_number{get;set;}=string.Empty;public DateTime expiry_date{get;set;}public decimal price{get;set;}public DateTime putaway_date{get;set;}
        public static AvailableStockRow From(StockEntity s)=>new(){stock_id=s.id,sku_id=s.sku_id,goods_location_id=s.goods_location_id,goods_owner_id=s.goods_owner_id,series_number=s.series_number,expiry_date=s.expiry_date,price=s.price,putaway_date=s.putaway_date};
    }

    private sealed class CanonicalAvailableStockRow
    {
        public long StockAllocationId{get;init;} public long ErpStockId{get;init;} public int SkuId{get;init;}
        public int GoodsLocationId{get;init;} public int GoodsOwnerId{get;init;}
        public string SeriesNumber{get;init;}=string.Empty; public DateTime? ExpiryDate{get;init;}
        public decimal Price{get;init;} public DateTime? PutawayDate{get;init;} public long QtyAvailable{get;init;}
    }
    private sealed record CanonicalPickPlan(DispatchlistAddViewModel Request,CanonicalAvailableStockRow Stock,int Quantity);
    private sealed class DispatchRuntimeRow
    {
        public string Mode{get;init;}=LegacyInventoryMode;
        public bool MaintenanceEnabled{get;init;}
        public long ErpWarehouseId{get;init;}
        public long ErpStockId{get;init;}
    }
    private sealed class ErpStockWarehouseRow
    {public long ErpStockId{get;init;}public long ErpWarehouseId{get;init;}}

    private const string AvailableStockSql="""
        SELECT s.`id` `stock_id`,s.`sku_id`,s.`goods_location_id`,CASE WHEN l.`warehouse_area_property`<>5 THEN l.`warehouse_id` ELSE 0 END `warehouse_id`,s.`goods_owner_id`,
          COALESCE(o.`goods_owner_name`,'') `goods_owner_name`,CASE WHEN l.`warehouse_area_property`<>5 THEN l.`location_name` ELSE '' END `location_name`,
          CASE WHEN l.`warehouse_area_property`<>5 THEN l.`warehouse_area_name` ELSE '' END `warehouse_area_name`,
          CASE WHEN l.`warehouse_area_property`<>5 THEN l.`warehouse_name` ELSE '' END `warehouse_name`,
          s.`series_number`,s.`expiry_date`,s.`price`,s.`putaway_date`,
          s.`qty`-(CASE WHEN s.`is_freeze`=1 THEN s.`qty` ELSE 0 END)
          -COALESCE((SELECT SUM(p.`pick_qty`) FROM `wms_dispatchpicklist` p INNER JOIN `wms_dispatchlist` d ON p.`dispatchlist_id`=d.`id`
              WHERE d.`tenant_id`=s.`tenant_id` AND d.`dispatch_status`>1 AND d.`dispatch_status`<6 AND p.`sku_id`=s.`sku_id`
                AND p.`goods_location_id`=s.`goods_location_id` AND p.`goods_owner_id`=s.`goods_owner_id` AND p.`series_number`=s.`series_number`
                AND p.`expiry_date`=s.`expiry_date` AND p.`price`=s.`price` AND p.`putaway_date`=s.`putaway_date`),0)
          -COALESCE((SELECT SUM(pd.`qty`) FROM `wms_stockprocessdetail` pd WHERE pd.`tenant_id`=s.`tenant_id` AND pd.`is_update_stock`=0
                AND pd.`sku_id`=s.`sku_id` AND pd.`goods_location_id`=s.`goods_location_id` AND pd.`goods_owner_id`=s.`goods_owner_id`
                AND pd.`series_number`=s.`series_number` AND pd.`expiry_date`=s.`expiry_date` AND pd.`price`=s.`price` AND pd.`putaway_date`=s.`putaway_date`),0)
          -COALESCE((SELECT SUM(m.`qty`) FROM `wms_stockmove` m WHERE m.`tenant_id`=s.`tenant_id` AND m.`move_status`=0
                AND m.`sku_id`=s.`sku_id` AND m.`orig_goods_location_id`=s.`goods_location_id` AND m.`goods_owner_id`=s.`goods_owner_id`
                AND m.`series_number`=s.`series_number` AND m.`expiry_date`=s.`expiry_date` AND m.`price`=s.`price` AND m.`putaway_date`=s.`putaway_date`),0) `qty_available`
        FROM `wms_stock` s INNER JOIN `wms_goodslocation` l ON s.`goods_location_id`=l.`id`
          LEFT JOIN `wms_goodsowner` o ON s.`goods_owner_id`=o.`id`
        """;

    private const string DispatchJoinSql="""FROM `wms_dispatchlist` d INNER JOIN `wms_sku` sku ON d.`sku_id`=sku.`id` INNER JOIN `wms_spu` spu ON sku.`spu_id`=spu.`id`""";
    private const string DispatchViewColumns="""
        d.`id`,d.`dispatch_no`,d.`dispatch_status`,d.`sku_id`,d.`qty`,d.`weight`,d.`volume`,d.`creator`,d.`create_time`,d.`damage_qty`,d.`lock_qty`,
        d.`picked_qty`,d.`qty`-d.`picked_qty` `unpicked_qty`,d.`intrasit_qty`,d.`package_qty`,d.`picked_qty`-d.`package_qty` `unpackage_qty`,
        d.`weighing_qty`,d.`picked_qty`-d.`weighing_qty` `unweighing_qty`,d.`actual_qty`,d.`sign_qty`,d.`package_no`,d.`package_person`,d.`package_time`,
        d.`weighing_no`,d.`weighing_person`,d.`weighing_weight`,d.`weighing_length`,d.`weighing_width`,d.`weighing_height`,d.`weighing_volume`,
        d.`waybill_no`,d.`carrier`,d.`carrier_warehouse_id`,d.`carrier_unit`,d.`volume_divisor`,d.`freightfee`,d.`last_update_time`,d.`tenant_id`,
        sku.`sku_code`,spu.`spu_code`,spu.`spu_description`,spu.`spu_name`,sku.`bar_code`,spu.`length_unit`,spu.`volume_unit`,spu.`weight_unit`,
        d.`pick_checker`,d.`pick_checker_id`
        """;
    private const string DispatchColumns="""
        `id`,`dispatch_order_id`,`packing_task_id`,`packing_task_item_id`,`dispatch_no`,`dispatch_status`,`sku_id`,`qty`,`weight`,`volume`,`creator`,`create_time`,
        `damage_qty`,`lock_qty`,`picked_qty`,`intrasit_qty`,`package_qty`,`weighing_qty`,`actual_qty`,`sign_qty`,`package_no`,`package_person`,`package_time`,
        `weighing_no`,`weighing_person`,`weighing_weight`,`weighing_length`,`weighing_width`,`weighing_height`,`weighing_volume`,`waybill_no`,`carrier`,
        `carrier_warehouse_id`,`carrier_unit`,`volume_divisor`,`freightfee`,`last_update_time`,`tenant_id`,`pick_checker_id`,`pick_checker`
        """;
    private const string PickColumns="""`id`,`dispatchlist_id`,`packing_task_item_id`,`stock_id`,`erp_stock_id`,`stock_allocation_id`,`reservation_id`,`reservation_item_id`,`goods_owner_id`,`goods_location_id`,`sku_id`,`pick_qty`,`picked_qty`,`is_update_stock`,`last_update_time`,`series_number`,`picker_id`,`picker`,`expiry_date`,`price`,`putaway_date`""";
    private const string StockColumns="""`id`,`sku_id`,`goods_location_id`,`qty`,`goods_owner_id`,`is_freeze`,`last_update_time`,`tenant_id`,`series_number`,`expiry_date`,`price`,`putaway_date`""";

    private static readonly IReadOnlyDictionary<string,string> DispatchSearchColumns=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase)
    {
        ["id"]="d.`id`",["dispatch_no"]="d.`dispatch_no`",["dispatch_status"]="d.`dispatch_status`",["sku_id"]="d.`sku_id`",["qty"]="d.`qty`",
        ["weight"]="d.`weight`",["volume"]="d.`volume`",["creator"]="d.`creator`",["create_time"]="d.`create_time`",["damage_qty"]="d.`damage_qty`",
        ["lock_qty"]="d.`lock_qty`",["picked_qty"]="d.`picked_qty`",["intrasit_qty"]="d.`intrasit_qty`",["package_qty"]="d.`package_qty`",
        ["weighing_qty"]="d.`weighing_qty`",["actual_qty"]="d.`actual_qty`",["sign_qty"]="d.`sign_qty`",["package_no"]="d.`package_no`",
        ["package_person"]="d.`package_person`",["weighing_no"]="d.`weighing_no`",["weighing_person"]="d.`weighing_person`",["waybill_no"]="d.`waybill_no`",
        ["carrier"]="d.`carrier`",["freightfee"]="d.`freightfee`",["last_update_time"]="d.`last_update_time`",["tenant_id"]="d.`tenant_id`",
        ["sku_code"]="sku.`sku_code`",["spu_code"]="spu.`spu_code`",["spu_name"]="spu.`spu_name`",["bar_code"]="sku.`bar_code`",
        ["pick_checker"]="d.`pick_checker`",["pick_checker_id"]="d.`pick_checker_id`"
    };
    private static readonly IReadOnlyDictionary<string,string> PreDispatchSearchColumns=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase)
    { ["dispatch_no"]="d.`dispatch_no`",["dispatch_status"]="d.`dispatch_status`",["qty"]="SUM(d.`qty`)",["creator"]="d.`creator`" };
}
