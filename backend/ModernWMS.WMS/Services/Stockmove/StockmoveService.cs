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

/// <summary>移库业务服务。</summary>
public class StockmoveService : BaseService<StockmoveEntity>, IStockmoveService
{
    private const string ViewSql = """
        SELECT m.`id`,m.`job_code`,m.`move_status`,m.`sku_id`,m.`orig_goods_location_id`,
               m.`dest_googs_location_id`,m.`qty`,m.`goods_owner_id`,m.`handler`,m.`handle_time`,
               m.`creator`,m.`create_time`,m.`last_update_time`,m.`series_number`,
               m.`erp_stock_id`,m.`stock_allocation_id`,
               m.`expiry_date`,m.`price`,m.`putaway_date`,sku.`sku_code`,sku.`sku_name`,
               spu.`spu_code`,spu.`spu_name`,dest.`location_name` `dest_googs_location_name`,
               dest.`warehouse_name` `dest_googs_warehouse`,orig.`location_name` `orig_goods_location_name`,
               orig.`warehouse_name` `orig_goods_warehouse`
        FROM `wms_stockmove` m
        JOIN `wms_sku` sku ON sku.`id`=m.`sku_id`
        JOIN `wms_spu` spu ON spu.`id`=sku.`spu_id`
        JOIN `wms_goodslocation` orig ON orig.`id`=m.`orig_goods_location_id`
        JOIN `wms_goodslocation` dest ON dest.`id`=m.`dest_googs_location_id`
        """;

    private static readonly IReadOnlyDictionary<string,string> SearchColumns=
        new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase)
        {
            ["id"]="m.`id`",["job_code"]="m.`job_code`",["move_status"]="m.`move_status`",
            ["sku_id"]="m.`sku_id`",["orig_goods_location_id"]="m.`orig_goods_location_id`",
            ["dest_googs_location_id"]="m.`dest_googs_location_id`",["qty"]="m.`qty`",
            ["goods_owner_id"]="m.`goods_owner_id`",["handler"]="m.`handler`",
            ["handle_time"]="m.`handle_time`",["creator"]="m.`creator`",["create_time"]="m.`create_time`",
            ["sku_code"]="sku.`sku_code`",["sku_name"]="sku.`sku_name`",["spu_code"]="spu.`spu_code`",
            ["spu_name"]="spu.`spu_name`",["dest_googs_location_name"]="dest.`location_name`",
            ["dest_googs_warehouse"]="dest.`warehouse_name`",["orig_goods_location_name"]="orig.`location_name`",
            ["orig_goods_warehouse"]="orig.`warehouse_name`",["series_number"]="m.`series_number`",
            ["expiry_date"]="m.`expiry_date`",["price"]="m.`price`",["putaway_date"]="m.`putaway_date`"
        };

    private readonly IMySqlConnectionFactory _connectionFactory;
    private readonly IStringLocalizer<Core.MultiLanguage> _stringLocalizer;
    private readonly FunctionHelper _functionHelper;
    private readonly IStockAllocationMutationService _stockMutationService;

    /// <summary>初始化移库服务。</summary>
    public StockmoveService(IMySqlConnectionFactory connectionFactory,
        IStringLocalizer<Core.MultiLanguage> stringLocalizer,FunctionHelper functionHelper,
        IStockAllocationMutationService stockMutationService)
    {
        _connectionFactory=connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        _stringLocalizer=stringLocalizer ?? throw new ArgumentNullException(nameof(stringLocalizer));
        _functionHelper=functionHelper ?? throw new ArgumentNullException(nameof(functionHelper));
        _stockMutationService=stockMutationService ?? throw new ArgumentNullException(nameof(stockMutationService));
    }

    /// <inheritdoc />
    public async Task<(List<StockmoveViewModel> data,int totals)> PageAsync(PageSearch pageSearch,CurrentUser currentUser)
    {
        var filter=DapperSearchBuilder.Build(pageSearch.searchObjects,SearchColumns);
        var where=string.IsNullOrWhiteSpace(filter.Sql)?"1=1":filter.Sql;
        filter.Parameters.Add("offset",(pageSearch.pageIndex-1)*pageSearch.pageSize);
        filter.Parameters.Add("pageSize",pageSearch.pageSize);
        await using var db=await _connectionFactory.OpenConnectionAsync();
        using var result=await db.QueryMultipleAsync($"""
            SELECT COUNT(*) FROM ({ViewSql} WHERE {where}) q;
            {ViewSql} WHERE {where} ORDER BY m.`last_update_time` DESC LIMIT @pageSize OFFSET @offset;
            """,filter.Parameters);
        var totals=await result.ReadSingleAsync<int>();
        return((await result.ReadAsync<StockmoveViewModel>()).AsList(),totals);
    }

    /// <inheritdoc />
    public async Task<List<StockmoveViewModel>> GetAllAsync(CurrentUser currentUser)
    {
        await using var db=await _connectionFactory.OpenConnectionAsync();
        return(await db.QueryAsync<StockmoveViewModel>($"{ViewSql};")).AsList();
    }

    /// <inheritdoc />
    public async Task<StockmoveViewModel?> GetAsync(int id)
    {
        await using var db=await _connectionFactory.OpenConnectionAsync();
        return await db.QuerySingleOrDefaultAsync<StockmoveViewModel>($"{ViewSql} WHERE m.`id`=@id LIMIT 1;",new { id });
    }

    /// <inheritdoc />
    public async Task<(int id,string msg)> AddAsync(StockmoveViewModel viewModel,CurrentUser currentUser)
    {
        var jobCode=await _functionHelper.GetFormNoAsync("Stockmove");
        await using var db=await _connectionFactory.OpenConnectionAsync();
        var originRouteSnapshot=await CanonicalInventorySupport.GetRouteAsync(
            db,viewModel.orig_goods_location_id);
        var destinationRouteSnapshot=await CanonicalInventorySupport.GetRouteAsync(
            db,viewModel.dest_googs_location_id);
        if(originRouteSnapshot.ErpWarehouseId!=destinationRouteSnapshot.ErpWarehouseId ||
           originRouteSnapshot.Mode!=destinationRouteSnapshot.Mode)
            return (0,"移库起点和终点不属于同一库存运行模式及ERP仓库");
        await using var tx=await db.BeginTransactionAsync(IsolationLevel.Serializable);
        try
        {
            await CanonicalInventorySupport.LockRoutesAsync(
                db,tx,[originRouteSnapshot,destinationRouteSnapshot]);
            var originRoute=originRouteSnapshot;
            var destinationRoute=destinationRouteSnapshot;
            if(originRoute.ErpWarehouseId!=destinationRoute.ErpWarehouseId || originRoute.Mode!=destinationRoute.Mode)
                return await Rollback(0,"移库起点和终点不属于同一库存运行模式及ERP仓库",tx);
            CanonicalInventorySupport.CanonicalAllocation? allocation=null;
            if(originRoute.Mode==CanonicalInventorySupport.CanonicalMode)
            {
                allocation=await CanonicalInventorySupport.ResolveAllocationAsync(
                    db,tx,viewModel.sku_id,viewModel.orig_goods_location_id,
                    viewModel.goods_owner_id,viewModel.series_number,viewModel.expiry_date,
                    viewModel.price,viewModel.putaway_date);
                var pending=await db.ExecuteScalarAsync<long>("""
                    SELECT COALESCE(SUM(`qty`),0) FROM `wms_stockmove`
                     WHERE `stock_allocation_id`=@allocationId AND `move_status`=0;
                    """,new { allocationId=allocation.AllocationId},tx);
                if(allocation.AllocatedQty-allocation.OccupiedQty-pending<viewModel.qty)
                    return await Rollback(0,_stringLocalizer["qty_not_available"],tx);
            }
            else
            {
                var stock=await db.QuerySingleOrDefaultAsync<AvailableStockRow>("""
                SELECT s.`id`,s.`is_freeze`,s.`qty`,
                    CASE WHEN s.`is_freeze`=1 THEN 0 ELSE s.`qty`
                      - COALESCE((SELECT SUM(p.`pick_qty`) FROM `wms_dispatchpicklist` p
                          JOIN `wms_dispatchlist` d ON d.`id`=p.`dispatchlist_id`
                          WHERE d.`dispatch_status`>1 AND d.`dispatch_status`<6
                            AND p.`goods_owner_id`=@ownerId AND p.`series_number`=@seriesNumber
                            AND p.`goods_location_id`=@originId AND p.`sku_id`=@skuId
                            AND p.`expiry_date`=@expiryDate AND p.`price`=@price AND p.`putaway_date`=@putawayDate),0)
                      - COALESCE((SELECT SUM(p.`qty`) FROM `wms_stockprocessdetail` p
                          WHERE p.`is_update_stock`=0 AND p.`goods_owner_id`=@ownerId
                            AND p.`series_number`=@seriesNumber AND p.`goods_location_id`=@originId
                            AND p.`sku_id`=@skuId AND p.`expiry_date`=@expiryDate
                            AND p.`price`=@price AND p.`putaway_date`=@putawayDate),0)
                      - COALESCE((SELECT SUM(sm.`qty`) FROM `wms_stockmove` sm
                          WHERE sm.`move_status`=0 AND sm.`goods_owner_id`=@ownerId
                            AND sm.`series_number`=@seriesNumber AND sm.`orig_goods_location_id`=@originId
                            AND sm.`sku_id`=@skuId AND sm.`expiry_date`=@expiryDate
                            AND sm.`price`=@price AND sm.`putaway_date`=@putawayDate),0)
                    END `qty_available`
                FROM `wms_stock` s
                WHERE s.`goods_owner_id`=@ownerId AND s.`series_number`=@seriesNumber
                  AND s.`goods_location_id`=@originId AND s.`sku_id`=@skuId
                  AND s.`expiry_date`=@expiryDate AND s.`price`=@price AND s.`putaway_date`=@putawayDate
                LIMIT 1 FOR UPDATE;
                """,StockParams(viewModel,currentUser),tx);
            if(stock==null || stock.qty_available<viewModel.qty)
                return await Rollback(0,_stringLocalizer["qty_not_available"],tx);

            var destFrozen=await db.ExecuteScalarAsync<bool>("""
                SELECT EXISTS(SELECT 1 FROM `wms_stock`
                    WHERE `goods_owner_id`=@ownerId AND `series_number`=@seriesNumber
                      AND `goods_location_id`=@destinationId AND `sku_id`=@skuId
                      AND `expiry_date`=@expiryDate AND `price`=@price AND `putaway_date`=@putawayDate
                      AND `is_freeze`=1);
                """,StockParams(viewModel,currentUser),tx);
            if(destFrozen) return await Rollback(0,_stringLocalizer["dest_stock_freeze"],tx);
            }

            var now=DateTime.Now;
            var id=await db.ExecuteScalarAsync<int>("""
                INSERT INTO `wms_stockmove`
                  (`job_code`,`move_status`,`sku_id`,`orig_goods_location_id`,`dest_googs_location_id`,`qty`,
                   `goods_owner_id`,`handler`,`handle_time`,`creator`,`create_time`,`last_update_time`,
                   `erp_stock_id`,`stock_allocation_id`,`series_number`,`expiry_date`,`price`,`putaway_date`)
                VALUES (@jobCode,0,@skuId,@originId,@destinationId,@qty,@ownerId,@handler,@handleTime,
                        @creator,@now,@now,@erpStockId,@allocationId,@seriesNumber,@expiryDate,@price,@putawayDate);
                SELECT LAST_INSERT_ID();
                """,new { jobCode,skuId=viewModel.sku_id,originId=viewModel.orig_goods_location_id,
                    destinationId=viewModel.dest_googs_location_id,viewModel.qty,ownerId=viewModel.goods_owner_id,
                    viewModel.handler,viewModel.handle_time,creator=currentUser.user_name,now,
                    erpStockId=allocation?.ErpStockId,allocationId=allocation?.AllocationId,
                    seriesNumber=viewModel.series_number,viewModel.expiry_date,viewModel.price,viewModel.putaway_date },tx);
            await tx.CommitAsync();
            return id>0 ? (id,_stringLocalizer["save_success"]) : (0,_stringLocalizer["save_failed"]);
        }
        catch { await tx.RollbackAsync(); throw; }
    }

    /// <inheritdoc />
    public async Task<(bool flag,string msg)> Confirm(int id,CurrentUser currentUser)
    {
        await using var db=await _connectionFactory.OpenConnectionAsync();
        var moveSnapshot=await db.QuerySingleOrDefaultAsync<StockmoveEntity>(
            "SELECT * FROM `wms_stockmove` WHERE `id`=@id LIMIT 1;",new{id});
        long? targetCandidateSnapshot=null;
        CanonicalInventorySupport.InventoryRoute? originRouteSnapshot=null;
        CanonicalInventorySupport.InventoryRoute? targetRouteSnapshot=null;
        if(moveSnapshot!=null)
        {
            originRouteSnapshot=await CanonicalInventorySupport.GetRouteAsync(
                db,moveSnapshot.orig_goods_location_id);
            targetRouteSnapshot=await CanonicalInventorySupport.GetRouteAsync(
                db,moveSnapshot.dest_googs_location_id);
        }
        if(moveSnapshot?.erp_stock_id is >0)
            targetCandidateSnapshot=await CanonicalInventorySupport.FindTargetAllocationIdAsync(
                db,null,moveSnapshot,moveSnapshot.erp_stock_id.Value,moveSnapshot.dest_googs_location_id);
        await using var tx=await db.BeginTransactionAsync(IsolationLevel.Serializable);
        try
        {
            var move=await db.QuerySingleOrDefaultAsync<StockmoveEntity>("""
                SELECT `id`,`job_code`,`move_status`,`sku_id`,`orig_goods_location_id`,`dest_googs_location_id`,
                       `qty`,`goods_owner_id`,`handler`,`handle_time`,`creator`,`create_time`,`last_update_time`,
                       `erp_stock_id`,`stock_allocation_id`,`series_number`,`expiry_date`,`price`,`putaway_date`
                FROM `wms_stockmove` WHERE `id`=@id LIMIT 1 FOR UPDATE;
                """,new { id },tx);
            if(move==null) return await Rollback(false,_stringLocalizer["not_exists_entity"],tx);
            if(moveSnapshot==null || !SameMoveIdentity(moveSnapshot,move))
                return await Rollback(false,"移库单在确认前已发生变化，请重试",tx);

            await CanonicalInventorySupport.LockRoutesAsync(
                db,tx,[originRouteSnapshot!,targetRouteSnapshot!]);
            var route=originRouteSnapshot!;
            var targetRoute=targetRouteSnapshot!;
            if(route.ErpWarehouseId!=targetRoute.ErpWarehouseId || route.Mode!=targetRoute.Mode)
                return await Rollback(false,"移库起点和终点不属于同一库存运行模式及ERP仓库",tx);
            var now=DateTime.Now;
            if(route.Mode==CanonicalInventorySupport.CanonicalMode)
            {
                if(!move.erp_stock_id.HasValue || !move.stock_allocation_id.HasValue)
                    return await Rollback(false,"移库单未绑定ERP库存分配，禁止确认",tx);
                var allocationIds=targetCandidateSnapshot.HasValue
                    ? new[]{move.stock_allocation_id.Value,targetCandidateSnapshot.Value}
                    : new[]{move.stock_allocation_id.Value};
                await _stockMutationService.PrelockAsync(
                    db,tx,[route.ErpWarehouseId],
                    [move.erp_stock_id.Value],allocationIds);
                var source=new CanonicalInventorySupport.CanonicalAllocation
                {
                    ErpStockId=move.erp_stock_id.Value,
                    AllocationId=move.stock_allocation_id.Value,
                    ErpWarehouseId=route.ErpWarehouseId
                };
                var targetId=await CanonicalInventorySupport.GetOrCreateTargetAllocationAsync(
                    db,tx,move,source,move.dest_googs_location_id,currentUser.user_name);
                await _stockMutationService.PrelockAsync(
                    db,tx,[route.ErpWarehouseId],[move.erp_stock_id.Value],
                    [move.stock_allocation_id.Value,targetId]);
                await _stockMutationService.MoveLocationAsync(
                    db,tx,
                    CanonicalInventorySupport.Context(
                        route.ErpWarehouseId,
                        $"MWMS:MV:{move.id}","STOCK_MOVE_LOCATION",move.id,move.id,
                        currentUser,move.creator,"库位移动"),
                    move.erp_stock_id.Value,move.stock_allocation_id.Value,targetId,move.qty);
            }
            else
            {
                var p=StockParams(move);
                var origin=await db.QuerySingleOrDefaultAsync<StockEntity>(StockSelectSql+"""
                 AND `goods_location_id`=@originId AND `sku_id`=@skuId LIMIT 1 FOR UPDATE;
                """,p,tx);
            // Keep the legacy destination predicate exactly: it selects a different SKU when present.
            var destination=await db.QuerySingleOrDefaultAsync<StockEntity>(StockSelectSql+"""
                 AND `goods_location_id`=@destinationId AND `sku_id`<>@skuId LIMIT 1 FOR UPDATE;
                """,p,tx);
            if(origin!=null)
            {
                if(origin.qty==move.qty) await db.ExecuteAsync("DELETE FROM `wms_stock` WHERE `id`=@id;",new { origin.id },tx);
                else await db.ExecuteAsync("UPDATE `wms_stock` SET `qty`=`qty`-@qty,`last_update_time`=@now WHERE `id`=@id;",new { move.qty,now,origin.id },tx);
            }
            if(destination==null)
                await db.ExecuteAsync("""
                    INSERT INTO `wms_stock` (`goods_location_id`,`sku_id`,`qty`,`goods_owner_id`,`is_freeze`,
                        `last_update_time`,`series_number`,`expiry_date`,`price`,`putaway_date`)
                    VALUES (@destinationId,@skuId,@qty,@ownerId,0,@now,@seriesNumber,@expiryDate,@price,@putawayDate);
                    """,new { destinationId=move.dest_googs_location_id,skuId=move.sku_id,move.qty,ownerId=move.goods_owner_id,
                        now,seriesNumber=move.series_number,move.expiry_date,move.price,move.putaway_date },tx);
            else await db.ExecuteAsync("UPDATE `wms_stock` SET `qty`=`qty`+@qty,`last_update_time`=@now WHERE `id`=@id;",new { move.qty,now,destination.id },tx);
            }

            var affected=await db.ExecuteAsync("""
                UPDATE `wms_stockmove` SET `handler`=@handler,`handle_time`=@now,`move_status`=1,`last_update_time`=@now
                WHERE `id`=@id;
                """,new { handler=currentUser.user_name,now,id },tx);
            await tx.CommitAsync();
            return affected>0 ? (true,_stringLocalizer["operation_success"]) : (false,_stringLocalizer["operation_failed"]);
        }
        catch { await tx.RollbackAsync(); throw; }
    }

    /// <inheritdoc />
    public async Task<(bool flag,string msg)> DeleteAsync(int id)
    {
        await using var db=await _connectionFactory.OpenConnectionAsync();
        await using var tx=await db.BeginTransactionAsync(IsolationLevel.ReadCommitted);
        try
        {
            var move=await db.QuerySingleOrDefaultAsync<StockmoveEntity>("""
                SELECT * FROM `wms_stockmove` WHERE `id`=@id FOR UPDATE;
            """,new{id},tx);
            if(move==null) return await Rollback(false,_stringLocalizer["not_exists_entity"],tx);
            await using var routeConnection=await _connectionFactory.OpenConnectionAsync();
            var routeSnapshot=await CanonicalInventorySupport.GetRouteAsync(
                routeConnection,move.orig_goods_location_id);
            _=await CanonicalInventorySupport.LockRouteAsync(db,tx,routeSnapshot);
            var count=await db.ExecuteAsync("DELETE FROM `wms_stockmove` WHERE `id`=@id AND `move_status`=0;",new { id },tx);
            await tx.CommitAsync();
            return count>0 ? (true,_stringLocalizer["delete_success"]) : (false,_stringLocalizer["delete_failed"]);
        }
        catch { await tx.RollbackAsync(); throw; }
    }

    /// <inheritdoc />
    public async Task<string> GetOrderCode(CurrentUser currentUser)
    {
        await using var db=await _connectionFactory.OpenConnectionAsync();
        var maxNo=await db.ExecuteScalarAsync<string?>("SELECT MAX(`job_code`) FROM `wms_stockmove`;");
        var date=DateTime.Now.ToString("yyyyMMdd");
        if(maxNo==null) return date+"-0001";
        var maxDate=maxNo.Substring(0,8); var maxDateNo=maxNo.Substring(9,4);
        if(date!=maxDate) return date+"-0001";
        int.TryParse(maxDateNo,out var number);
        return date+"-"+(number+1).ToString("0000");
    }

    private static object StockParams(StockmoveViewModel x,CurrentUser user)=>new { skuId=x.sku_id,originId=x.orig_goods_location_id,
        destinationId=x.dest_googs_location_id,ownerId=x.goods_owner_id,seriesNumber=x.series_number,
        expiryDate=x.expiry_date,x.price,putawayDate=x.putaway_date};
    private static object StockParams(StockmoveEntity x)=>new { skuId=x.sku_id,originId=x.orig_goods_location_id,
        destinationId=x.dest_googs_location_id,ownerId=x.goods_owner_id,seriesNumber=x.series_number,
        expiryDate=x.expiry_date,x.price,putawayDate=x.putaway_date };
    private static bool SameMoveIdentity(StockmoveEntity x,StockmoveEntity y)=>
        x.erp_stock_id==y.erp_stock_id && x.stock_allocation_id==y.stock_allocation_id
        && x.sku_id==y.sku_id && x.orig_goods_location_id==y.orig_goods_location_id
        && x.dest_googs_location_id==y.dest_googs_location_id && x.goods_owner_id==y.goods_owner_id
        && x.qty==y.qty && x.series_number==y.series_number && x.expiry_date==y.expiry_date
        && x.price==y.price && x.putaway_date==y.putaway_date && x.move_status==y.move_status;
    private static async Task<(T flag,string msg)> Rollback<T>(T flag,string msg,MySqlTransaction tx){await tx.RollbackAsync();return(flag,msg);}
    private const string StockSelectSql="""
        SELECT `id`,`sku_id`,`goods_location_id`,`qty`,`goods_owner_id`,`is_freeze`,`last_update_time`,
               `series_number`,`expiry_date`,`price`,`putaway_date`
        FROM `wms_stock` WHERE `goods_owner_id`=@ownerId AND `series_number`=@seriesNumber
          AND `expiry_date`=@expiryDate AND `price`=@price AND `putaway_date`=@putawayDate
        """;
    private sealed class AvailableStockRow { public int id{get;init;} public bool is_freeze{get;init;} public int qty{get;init;} public int qty_available{get;init;} }
}
