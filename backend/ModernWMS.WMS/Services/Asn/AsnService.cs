using System.Data;
using Dapper;
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

namespace ModernWMS.WMS.Services;

/// <summary>ASN 服务。</summary>
public class AsnService : BaseService<AsnEntity>, IAsnService
{
    private readonly IMySqlConnectionFactory _connectionFactory;
    private readonly IStringLocalizer<Core.MultiLanguage> _stringLocalizer;
    private readonly FunctionHelper _functionHelper;

    /// <summary>
    /// 初始化 AsnService 的新实例。
    /// </summary>
    public AsnService(IMySqlConnectionFactory connectionFactory,
        IStringLocalizer<Core.MultiLanguage> stringLocalizer, FunctionHelper functionHelper)
    {
        _connectionFactory = connectionFactory;
        _stringLocalizer = stringLocalizer;
        _functionHelper = functionHelper;
    }

    private const string DetailSelect = """
        SELECT a.`id`,a.`asnmaster_id`,a.`asn_no`,m.`asn_batch`,m.`estimated_arrival_time`,a.`asn_status`,
          a.`spu_id`,p.`spu_code`,p.`spu_name`,a.`sku_id`,k.`sku_code`,k.`sku_name`,p.`origin`,
          p.`length_unit`,p.`volume_unit`,p.`weight_unit`,a.`price`,a.`asn_qty`,a.`actual_qty`,
          a.`arrival_time`,a.`unload_person`,a.`unload_person_id`,a.`unload_time`,a.`sorted_qty`,
          a.`shortage_qty`,a.`more_qty`,a.`damage_qty`,k.`weight`*a.`asn_qty` AS `weight`,
          k.`volume`*a.`asn_qty` AS `volume`,a.`supplier_id`,a.`supplier_name`,a.`goods_owner_id`,
          a.`goods_owner_name`,a.`creator`,a.`create_time`,a.`last_update_time`,a.`is_valid`,a.`expiry_date`
        FROM `wms_asn` a JOIN `wms_asnmaster` m ON m.`id`=a.`asnmaster_id`
          JOIN `wms_spu` p ON p.`id`=a.`spu_id` JOIN `wms_sku` k ON k.`id`=a.`sku_id`
        """;

    private static readonly IReadOnlyDictionary<string,string> DetailSearch = new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase)
    {
        ["id"]="a.`id`",["asnmaster_id"]="a.`asnmaster_id`",["asn_no"]="a.`asn_no`",["asn_batch"]="m.`asn_batch`",
        ["asn_status"]="a.`asn_status`",["spu_code"]="p.`spu_code`",["spu_name"]="p.`spu_name`",
        ["sku_code"]="k.`sku_code`",["sku_name"]="k.`sku_name`",["supplier_name"]="a.`supplier_name`",
        ["goods_owner_name"]="a.`goods_owner_name`",["creator"]="a.`creator`",["is_valid"]="a.`is_valid`",
        ["estimated_arrival_time"]="m.`estimated_arrival_time`",["spu_id"]="a.`spu_id`",["sku_id"]="a.`sku_id`",
        ["origin"]="p.`origin`",["price"]="a.`price`",["asn_qty"]="a.`asn_qty`",["actual_qty"]="a.`actual_qty`",
        ["arrival_time"]="a.`arrival_time`",["unload_person"]="a.`unload_person`",["unload_person_id"]="a.`unload_person_id`",
        ["unload_time"]="a.`unload_time`",["sorted_qty"]="a.`sorted_qty`",["shortage_qty"]="a.`shortage_qty`",
        ["more_qty"]="a.`more_qty`",["damage_qty"]="a.`damage_qty`",["supplier_id"]="a.`supplier_id`",
        ["goods_owner_id"]="a.`goods_owner_id`",["create_time"]="a.`create_time`",["last_update_time"]="a.`last_update_time`",
        ["expiry_date"]="a.`expiry_date`"
    };

    /// <summary>
    /// 执行 PageAsync 操作。
    /// </summary>
    public async Task<(List<AsnViewModel> data, int totals)> PageAsync(PageSearch pageSearch, CurrentUser currentUser)
    {
        var filter = DapperSearchBuilder.Build(pageSearch.searchObjects, DetailSearch);
        var clauses = new List<string>{"a.`tenant_id`=@tenantId"};
        var title = pageSearch.sqlTitle.ToLowerInvariant();
        if (title.Contains("asn_status:alltodo")) clauses.Add("a.`asn_status`<=3");
        else if (title.Contains("asn_status"))
        {
            var status=Convert.ToByte(title.Trim().Replace("asn_status","").Replace("：","").Replace(":","").Replace("=",""));
            clauses.Add("a.`asn_status`=@status"); filter.Parameters.Add("status",status);
        }
        if (!string.IsNullOrWhiteSpace(filter.Sql)) clauses.Add(filter.Sql);
        filter.Parameters.Add("tenantId",currentUser.tenant_id);
        filter.Parameters.Add("offset",(pageSearch.pageIndex-1)*pageSearch.pageSize);
        filter.Parameters.Add("pageSize",pageSearch.pageSize);
        var where=string.Join(" AND ",clauses);
        await using var connection=await _connectionFactory.OpenConnectionAsync();
        using var grid=await connection.QueryMultipleAsync($"""
            SELECT COUNT(*) FROM `wms_asn` a JOIN `wms_asnmaster` m ON m.`id`=a.`asnmaster_id`
              JOIN `wms_spu` p ON p.`id`=a.`spu_id` JOIN `wms_sku` k ON k.`id`=a.`sku_id` WHERE {where};
            {DetailSelect} WHERE {where} ORDER BY a.`create_time` DESC LIMIT @pageSize OFFSET @offset;
            """,filter.Parameters);
        var total=await grid.ReadSingleAsync<int>();
        return ((await grid.ReadAsync<AsnViewModel>()).AsList(),total);
    }

    /// <summary>获取 ASN。</summary>
    public async Task<AsnViewModel> GetAsync(int id)
    {
        await using var connection=await _connectionFactory.OpenConnectionAsync();
        return await connection.QuerySingleOrDefaultAsync<AsnViewModel>($"{DetailSelect} WHERE a.`id`=@id LIMIT 1;",new{id}) ?? new();
    }

    /// <summary>
    /// 执行 AddAsync 操作。
    /// </summary>
    public async Task<(int id,string msg)> AddAsync(AsnViewModel vm,CurrentUser user)
    {
        var no=await _functionHelper.GetFormNoAsync("Asn"); var now=DateTime.Now;
        await using var c=await _connectionFactory.OpenConnectionAsync();
        var id=await c.ExecuteScalarAsync<int>("""
          INSERT INTO `wms_asn` (`asnmaster_id`,`asn_no`,`asn_status`,`spu_id`,`sku_id`,`asn_qty`,`actual_qty`,
          `arrival_time`,`unload_time`,`unload_person_id`,`unload_person`,`sorted_qty`,`shortage_qty`,`more_qty`,`damage_qty`,
          `weight`,`volume`,`supplier_id`,`supplier_name`,`goods_owner_id`,`goods_owner_name`,`creator`,`create_time`,
          `last_update_time`,`is_valid`,`tenant_id`,`expiry_date`,`price`)
          VALUES (@asnmaster_id,@no,@asn_status,@spu_id,@sku_id,@asn_qty,@actual_qty,@arrival_time,@unload_time,
          @unload_person_id,@unload_person,@sorted_qty,@shortage_qty,@more_qty,@damage_qty,@weight,@volume,@supplier_id,
          @supplier_name,@goods_owner_id,@goods_owner_name,@creator,@now,@now,@is_valid,@tenant_id,@expiry_date,@price);
          SELECT LAST_INSERT_ID();
          """,new{vm.asnmaster_id,no,vm.asn_status,vm.spu_id,vm.sku_id,vm.asn_qty,vm.actual_qty,vm.arrival_time,vm.unload_time,
              vm.unload_person_id,vm.unload_person,vm.sorted_qty,vm.shortage_qty,vm.more_qty,vm.damage_qty,vm.weight,vm.volume,
              vm.supplier_id,vm.supplier_name,vm.goods_owner_id,vm.goods_owner_name,creator=user.user_name,now,vm.is_valid,
              tenant_id=user.tenant_id,vm.expiry_date,vm.price});
        return id>0?(id,_stringLocalizer["save_success"]):(0,_stringLocalizer["save_failed"]);
    }

    /// <summary>生成 ASN 单号。</summary>
    public async Task<string> GetOrderCode(CurrentUser user)
    {
        await using var c=await _connectionFactory.OpenConnectionAsync();
        var maxNo=await c.ExecuteScalarAsync<string?>("SELECT MAX(`asn_no`) FROM `wms_asn` WHERE `tenant_id`=@tenantId;",new{tenantId=user.tenant_id});
        var date=DateTime.Now.ToString("yyyyMMdd");
        if(string.IsNullOrEmpty(maxNo)) return date+"-0001";
        try { return date==maxNo[..8]?date+"-"+(int.Parse(maxNo[9..])+1).ToString("0000"):date+"-0001"; }
        catch{return date+"-0001";}
    }

    /// <summary>
    /// 执行 UpdateAsync 操作。
    /// </summary>
    public async Task<(bool flag,string msg)> UpdateAsync(AsnViewModel vm)
    {
        await using var c=await _connectionFactory.OpenConnectionAsync();
        if(!await ExistsAsync(c,vm.id)) return(false,_stringLocalizer["not_exists_entity"]);
        var qty=await c.ExecuteAsync("""
          UPDATE `wms_asn` SET `asn_no`=@asn_no,`spu_id`=@spu_id,`sku_id`=@sku_id,`price`=@price,
          `asn_qty`=@asn_qty,`weight`=@weight,`volume`=@volume,`supplier_id`=@supplier_id,`supplier_name`=@supplier_name,
          `goods_owner_id`=@goods_owner_id,`goods_owner_name`=@goods_owner_name,`is_valid`=@is_valid,`last_update_time`=@now
          WHERE `id`=@id;
          """,new{vm.id,vm.asn_no,vm.spu_id,vm.sku_id,vm.price,vm.asn_qty,vm.weight,vm.volume,vm.supplier_id,vm.supplier_name,
              vm.goods_owner_id,vm.goods_owner_name,vm.is_valid,now=DateTime.Now});
        return WriteResult(qty,"save");
    }

    /// <summary>
    /// 执行 DeleteAsync 操作。
    /// </summary>
    public async Task<(bool flag,string msg)> DeleteAsync(int id)
    {
        await using var c=await _connectionFactory.OpenConnectionAsync();
        var entity=await c.QuerySingleOrDefaultAsync<AsnEntity>("SELECT * FROM `wms_asn` WHERE `id`=@id LIMIT 1;",new{id});
        if(entity==null)return(false,_stringLocalizer["not_exists_entity"]);
        if(entity.asn_status==8)return(false,_stringLocalizer["asn_had_putaway"]);
        var qty=entity.asn_status==0
            ?await c.ExecuteAsync("DELETE FROM `wms_asn` WHERE `id`=@id;",new{id})
            :await c.ExecuteAsync("UPDATE `wms_asn` SET `asn_status`=`asn_status`-1 WHERE `id`=@id;",new{id});
        return qty>0?(true,_stringLocalizer["delete_success"]):(false,_stringLocalizer["delete_failed"]);
    }

    /// <summary>
    /// 执行 BulkModifyGoodsownerAsync 操作。
    /// </summary>
    public async Task<(bool flag,string msg)> BulkModifyGoodsownerAsync(AsnBulkModifyGoodsOwnerViewModel vm)
    {
        await using var c=await _connectionFactory.OpenConnectionAsync();
        var qty=await c.ExecuteAsync("""
          UPDATE `wms_asn` SET `goods_owner_id`=@goods_owner_id,`goods_owner_name`=@goods_owner_name,`last_update_time`=@now WHERE `id` IN @ids;
          """,new{vm.goods_owner_id,vm.goods_owner_name,now=DateTime.Now,ids=vm.idList});
        return WriteResult(qty,"save");
    }

    /// <summary>
    /// 执行 ConfirmAsync 操作。
    /// </summary>
    public Task<(bool flag,string msg)> ConfirmAsync(List<AsnConfirmInputViewModel> rows) =>
        ChangeRowsAsync(rows.Select(x=>x.id).Where(x=>x>0).ToList(),0,1,"ASN_Status_Is_Not_Pre_Delivery","confirm",
            rows.GroupBy(x=>x.id).ToDictionary(x=>x.Key,x=>(object?)x.First().arrival_time));
    /// <summary>
    /// 执行 ConfirmCancelAsync 操作。
    /// </summary>
    public Task<(bool flag,string msg)> ConfirmCancelAsync(List<int> ids) =>
        ChangeRowsAsync(ids,1,0,"ASN_Status_Is_Not_Pre_Delivery","save",null,true);

    /// <summary>
    /// 执行 UnloadAsync 操作。
    /// </summary>
    public async Task<(bool flag,string msg)> UnloadAsync(List<AsnUnloadInputViewModel> rows,CurrentUser user)
    {
        var ids=rows.Select(x=>x.id).Where(x=>x>0).ToList();
        await using var c=await _connectionFactory.OpenConnectionAsync(); await using var tx=await c.BeginTransactionAsync();
        var entities=(await c.QueryAsync<AsnEntity>("SELECT * FROM `wms_asn` WHERE `id` IN @ids FOR UPDATE;",new{ids},tx)).AsList();
        if(entities.Count==0)return(false,"[202]"+_stringLocalizer["not_exists_entity"]);
        if(entities.Any(x=>x.asn_status>1))return(false,"[202]"+_stringLocalizer["ASN_Status_Is_Not_Pre_Load"]);
        var now=DateTime.Now;
        foreach(var e in entities){var vm=rows.FirstOrDefault(x=>x.id==e.id);if(vm!=null)await c.ExecuteAsync("""
          UPDATE `wms_asn` SET `asn_status`=2,`last_update_time`=@now,`unload_time`=@unload_time,
          `unload_person_id`=@personId,`unload_person`=@person WHERE `id`=@id;
          """,new{e.id,now,vm.unload_time,personId=vm.unload_person_id==0?user.user_id:vm.unload_person_id,
              person=vm.unload_person_id==0?user.user_name:vm.unload_person},tx);}
        await tx.CommitAsync();return(true,_stringLocalizer["confirm_success"]);
    }

    /// <summary>
    /// 执行 UnloadCancelAsync 操作。
    /// </summary>
    public Task<(bool flag,string msg)> UnloadCancelAsync(List<int> ids)=>ResetUnloadAsync(ids);

    /// <summary>
    /// 执行 SortingAsync 操作。
    /// </summary>
    public async Task<(bool flag,string msg)> SortingAsync(List<AsnsortInputViewModel> rows,CurrentUser user)
    {
        var ids=rows.Select(x=>x.asn_id).Distinct().ToList();
        await using var c=await _connectionFactory.OpenConnectionAsync();await using var tx=await c.BeginTransactionAsync();
        var asns=(await c.QueryAsync<AsnEntity>("SELECT * FROM `wms_asn` WHERE `id` IN @ids FOR UPDATE;",new{ids},tx)).AsList();
        if(asns.Count==0)return(false,"[202]"+_stringLocalizer["not_exists_entity"]);
        if(asns.Any(x=>x.asn_status!=2))return(false,"[202]"+_stringLocalizer["ASN_Status_Is_Not_Pre_Sort"]);
        foreach(var v in rows.Where(v=>asns.Any(e=>e.id==v.asn_id)))
        {
            var quantities=v.sorted_qty>1&&v.is_auto_num?Enumerable.Repeat(1,v.sorted_qty).ToList():[v.sorted_qty];
            var sns=v.sorted_qty>1&&v.is_auto_num?await _functionHelper.GetFormNoListAsync("Asnsort",v.sorted_qty,user.tenant_id,"sn")
                :[await _functionHelper.GetFormNoAsync("Asnsort","sn")];
            for(var i=0;i<quantities.Count;i++)await c.ExecuteAsync("""
              INSERT INTO `wms_asnsort` (`asn_id`,`sorted_qty`,`series_number`,`putaway_qty`,`creator`,`create_time`,`last_update_time`,`is_valid`,`tenant_id`)
              VALUES (@asnId,@qty,@sn,0,@creator,@now,@now,1,@tenantId);
              """,new{asnId=v.asn_id,qty=quantities[i],sn=sns[i],creator=user.user_name,now=DateTime.Now,tenantId=user.tenant_id},tx);
        }
        foreach(var e in asns){var qty=rows.Where(x=>x.asn_id==e.id).Sum(x=>x.sorted_qty);var expiry=rows.First(x=>x.asn_id==e.id).expiry_date;
            await c.ExecuteAsync("UPDATE `wms_asn` SET `sorted_qty`=`sorted_qty`+@qty,`expiry_date`=@expiry,`last_update_time`=@now WHERE `id`=@id;",
                new{e.id,qty,expiry,now=DateTime.Now},tx);}
        await tx.CommitAsync();return(true,_stringLocalizer["save_success"]);
    }

    /// <summary>获取 ASN 明细排序信息。</summary>
    public async Task<List<AsnsortViewModel>> GetAsnsortsAsync(int asn_id)
    {
        await using var c=await _connectionFactory.OpenConnectionAsync();
        return (await c.QueryAsync<AsnsortViewModel>("""
          SELECT s.`id`,s.`asn_id`,s.`sorted_qty`,s.`series_number`,s.`putaway_qty`,a.`expiry_date`,s.`creator`,
          s.`create_time`,s.`last_update_time`,s.`is_valid`,s.`tenant_id` FROM `wms_asn` a JOIN `wms_asnsort` s ON s.`asn_id`=a.`id`
          WHERE a.`id`=@asn_id;
          """,new{asn_id})).AsList();
    }

    /// <summary>
    /// 执行 ModifyAsnsortsAsync 操作。
    /// </summary>
    public async Task<(bool flag,string msg)> ModifyAsnsortsAsync(List<AsnsortEntity> rows,CurrentUser user)
    {
        await using var c=await _connectionFactory.OpenConnectionAsync();await using var tx=await c.BeginTransactionAsync();
        var del=rows.Where(x=>x.id<0).Select(x=>-x.id).ToList();if(del.Count>0)await c.ExecuteAsync("DELETE FROM `wms_asnsort` WHERE `id` IN @del;",new{del},tx);
        foreach(var r in rows.Where(x=>x.id>0&&x.sorted_qty>0))await c.ExecuteAsync("""
          UPDATE `wms_asnsort` SET `asn_id`=@asn_id,`sorted_qty`=@sorted_qty,`series_number`=@series_number,
          `putaway_qty`=@putaway_qty,`creator`=@creator,`create_time`=@create_time,`last_update_time`=@now,`is_valid`=1,`tenant_id`=@tenant_id WHERE `id`=@id;
          """,new{r.id,r.asn_id,r.sorted_qty,r.series_number,r.putaway_qty,r.creator,r.create_time,now=DateTime.Now,r.tenant_id},tx);
        var ids=rows.Select(x=>x.asn_id).Distinct().ToList();
        await c.ExecuteAsync("""
          UPDATE `wms_asn` a SET a.`sorted_qty`=(SELECT COALESCE(SUM(s.`sorted_qty`),0) FROM `wms_asnsort` s WHERE s.`asn_id`=a.`id`)
          WHERE a.`id` IN @ids;
          """,new{ids},tx);await tx.CommitAsync();return(true,_stringLocalizer["sorted_success"]);
    }

    /// <summary>
    /// 执行 SortedAsync 操作。
    /// </summary>
    public async Task<(bool flag,string msg)> SortedAsync(List<int> ids)
    {
        await using var c=await _connectionFactory.OpenConnectionAsync();var rows=(await c.QueryAsync<AsnEntity>("SELECT * FROM `wms_asn` WHERE `id` IN @ids;",new{ids})).AsList();
        if(rows.Count==0)return(false,"[202]"+_stringLocalizer["not_exists_entity"]);if(rows.Any(x=>x.sorted_qty<1))return(false,"[202]"+_stringLocalizer["ASN_Status_Is_Not_Sorting"]);
        var qty=await c.ExecuteAsync("""
          UPDATE `wms_asn` SET `asn_status`=3,`more_qty`=GREATEST(`sorted_qty`-`asn_qty`,0),
          `shortage_qty`=GREATEST(`asn_qty`-`sorted_qty`,0),`last_update_time`=@now WHERE `id` IN @ids;
          """,new{ids,now=DateTime.Now});return qty>0?(true,_stringLocalizer["sorted_success"]):(false,_stringLocalizer["sorted_failed"]);
    }

    /// <summary>
    /// 执行 SortedCancelAsync 操作。
    /// </summary>
    public async Task<(bool flag,string msg)> SortedCancelAsync(List<int> ids)
    {
        await using var c=await _connectionFactory.OpenConnectionAsync();await using var tx=await c.BeginTransactionAsync();
        var rows=(await c.QueryAsync<AsnEntity>("SELECT * FROM `wms_asn` WHERE `id` IN @ids FOR UPDATE;",new{ids},tx)).AsList();
        if(rows.Count==0)return(false,"[202]"+_stringLocalizer["not_exists_entity"]);if(rows.Any(x=>x.actual_qty>0))return(false,"[202]"+_stringLocalizer["ASN_Status_Is_Putaway"]);if(rows.Any(x=>x.sorted_qty<1))return(false,"[202]"+_stringLocalizer["ASN_Status_Is_Not_Sorting"]);
        var qty=await c.ExecuteAsync("UPDATE `wms_asn` SET `asn_status`=2,`sorted_qty`=0,`more_qty`=0,`shortage_qty`=0,`last_update_time`=@now WHERE `id` IN @ids;",new{ids,now=DateTime.Now},tx);
        if(qty>0)await c.ExecuteAsync("DELETE FROM `wms_asnsort` WHERE `asn_id` IN @ids;",new{ids},tx);await tx.CommitAsync();return WriteResult(qty,"save");
    }

    /// <summary>获取待上架数据。</summary>
    public async Task<List<AsnPendingPutawayViewModel>> GetPendingPutawayDataAsync(int id)
    {
        await using var c=await _connectionFactory.OpenConnectionAsync();return(await c.QueryAsync<AsnPendingPutawayViewModel>("""
          SELECT a.`id` AS `asn_id`,a.`goods_owner_id`,a.`goods_owner_name`,s.`series_number`,SUM(s.`sorted_qty`-s.`putaway_qty`) AS `sorted_qty`
          FROM `wms_asn` a JOIN `wms_asnsort` s ON s.`asn_id`=a.`id` WHERE a.`id`=@id AND s.`putaway_qty`<s.`sorted_qty`
          GROUP BY a.`id`,a.`goods_owner_id`,a.`goods_owner_name`,s.`series_number`;
          """,new{id})).AsList();
    }

    /// <summary>
    /// 执行 PutAwayAsync 操作。
    /// </summary>
    public async Task<(bool flag,string msg)> PutAwayAsync(List<AsnPutAwayInputViewModel> rows,CurrentUser user)
    {
        rows.RemoveAll(x=>x.putaway_qty<1);
        if(rows.Any(x=>x.goods_location_id==0))return(false,"[202]"+string.Format(_stringLocalizer["Required"],_stringLocalizer["location_name"]));
        var locationIds=rows.Select(x=>x.goods_location_id).Distinct().ToList();
        await using var c=await _connectionFactory.OpenConnectionAsync();
        var locations=(await c.QueryAsync<GoodslocationEntity>("SELECT * FROM `wms_goodslocation` WHERE `id` IN @locationIds;",new{locationIds})).AsList();
        if(locations.Count!=locationIds.Count)return(false,"[202]"+string.Format(_stringLocalizer["Required"],_stringLocalizer["location_name"]));
        var routeSnapshots=new List<CanonicalInventorySupport.InventoryRoute>();
        foreach(var locationId in locationIds)
        {
            var route=await CanonicalInventorySupport.GetRouteAsync(c,user.tenant_id,locationId);
            routeSnapshots.Add(route);
            if(route.Mode==CanonicalInventorySupport.CanonicalMode)
                return(false,"普通ASN缺少可唯一关联的ERP采购物流库存维度，统一库存模式下禁止上架；请使用ERP签收入库流程");
        }
        await using var tx=await c.BeginTransactionAsync(IsolationLevel.Serializable);
        await CanonicalInventorySupport.LockRoutesAsync(c,tx,user.tenant_id,routeSnapshots);
        var asn=await c.QuerySingleOrDefaultAsync<AsnEntity>("SELECT * FROM `wms_asn` WHERE `id`=@id FOR UPDATE;",new{id=rows[0].asn_id},tx);
        if(asn==null)return(false,"[202]"+_stringLocalizer["not_exists_entity"]);
        var sum=rows.Sum(x=>x.putaway_qty);
        if(asn.asn_status!=3)return(false,"[202]"+$"{asn.asn_no}{_stringLocalizer["ASN_Status_Is_Not_Sorted"]}");
        if(asn.actual_qty+sum>asn.sorted_qty)return(false,"[202]"+$"{asn.asn_no}{_stringLocalizer["ASN_Total_PutAway_Qty_Greater_Than_Sorted_Qty"]}");
        var damage=rows.Where(x=>locations.First(l=>l.id==x.goods_location_id).warehouse_area_property==5).Sum(x=>x.putaway_qty);
        await c.ExecuteAsync("""
          UPDATE `wms_asn` SET `actual_qty`=`actual_qty`+@sum,`damage_qty`=`damage_qty`+@damage,
          `asn_status`=IF(`actual_qty`+@sum=`sorted_qty`,4,`asn_status`),`last_update_time`=@now WHERE `id`=@id;
          """,new{id=asn.id,sum,damage,now=DateTime.Now},tx);
        var sorts=(await c.QueryAsync<AsnsortEntity>("SELECT * FROM `wms_asnsort` WHERE `asn_id`=@id AND `sorted_qty`>`putaway_qty` ORDER BY `id` FOR UPDATE;",new{id=asn.id},tx)).AsList();
        foreach(var vm in rows)
        {
            var left=vm.putaway_qty;
            foreach(var s in sorts.Where(x=>x.series_number==vm.series_number))
            { if(left<=0)break;var used=Math.Min(left,s.sorted_qty-s.putaway_qty);s.putaway_qty+=used;left-=used;
              await c.ExecuteAsync("UPDATE `wms_asnsort` SET `putaway_qty`=@putaway_qty WHERE `id`=@id;",new{s.id,s.putaway_qty},tx); }
            var putawayDate=DateTime.Now.ToString("yyyy-MM-dd").ObjToDate();
            var stockId=await c.ExecuteScalarAsync<int?>("""
              SELECT `id` FROM `wms_stock` WHERE `sku_id`=@skuId AND `goods_location_id`=@locationId AND `goods_owner_id`=@ownerId
              AND `series_number`=@sn AND `expiry_date`=@expiry AND `price`=@price AND `putaway_date`=@putawayDate LIMIT 1 FOR UPDATE;
              """,new{skuId=asn.sku_id,locationId=vm.goods_location_id,ownerId=vm.goods_owner_id,sn=vm.series_number,expiry=asn.expiry_date,asn.price,putawayDate},tx);
            if(stockId.HasValue)await c.ExecuteAsync("UPDATE `wms_stock` SET `qty`=`qty`+@qty,`last_update_time`=@now WHERE `id`=@stockId;",new{qty=vm.putaway_qty,now=DateTime.Now,stockId},tx);
            else await c.ExecuteAsync("""
              INSERT INTO `wms_stock` (`sku_id`,`goods_location_id`,`qty`,`goods_owner_id`,`is_freeze`,`last_update_time`,`tenant_id`,`series_number`,`expiry_date`,`price`,`putaway_date`)
              VALUES (@skuId,@locationId,@qty,@ownerId,0,@now,@tenantId,@sn,@expiry,@price,@putawayDate);
              """,new{skuId=asn.sku_id,locationId=vm.goods_location_id,qty=vm.putaway_qty,ownerId=asn.goods_owner_id,now=DateTime.Now,tenantId=user.tenant_id,sn=vm.series_number,expiry=asn.expiry_date,asn.price,putawayDate},tx);
        }
        await tx.CommitAsync();return(true,_stringLocalizer["putaway_success"]);
    }

    private const string MasterSelect="""
      SELECT `id`,`asn_no`,`asn_batch`,`estimated_arrival_time`,`asn_status`,`weight`,`volume`,`goods_owner_id`,
      `goods_owner_name`,`creator`,`create_time`,`last_update_time`,`tenant_id` FROM `wms_asnmaster`
      """;
    private const string MasterDetailSelect="""
      SELECT a.`id`,a.`asnmaster_id`,a.`asn_status`,a.`spu_id`,p.`spu_code`,p.`spu_name`,a.`sku_id`,k.`sku_code`,k.`sku_name`,
      p.`origin`,p.`length_unit`,p.`volume_unit`,p.`weight_unit`,a.`asn_qty`,a.`actual_qty`,a.`weight`,a.`volume`,
      a.`supplier_id`,a.`supplier_name`,a.`is_valid`,a.`expiry_date`,a.`price`,a.`sorted_qty`
      FROM `wms_asn` a JOIN `wms_spu` p ON p.`id`=a.`spu_id` JOIN `wms_sku` k ON k.`id`=a.`sku_id`
      """;
    private static readonly IReadOnlyDictionary<string,string> MasterSearch=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase)
    { ["id"]="m.`id`",["asn_no"]="m.`asn_no`",["asn_batch"]="m.`asn_batch`",["asn_status"]="m.`asn_status`",
      ["estimated_arrival_time"]="m.`estimated_arrival_time`",["weight"]="m.`weight`",["volume"]="m.`volume`",
      ["goods_owner_id"]="m.`goods_owner_id`",["goods_owner_name"]="m.`goods_owner_name`",["creator"]="m.`creator`",
      ["create_time"]="m.`create_time`",["last_update_time"]="m.`last_update_time`",["tenant_id"]="m.`tenant_id`" };

    /// <summary>
    /// 执行 PageAsnmasterAsync 操作。
    /// </summary>
    public async Task<(List<AsnmasterBothViewModel> data,int totals)> PageAsnmasterAsync(PageSearch pageSearch,CurrentUser user)
    {
        var filter=DapperSearchBuilder.Build(pageSearch.searchObjects,MasterSearch);var clauses=new List<string>{"m.`tenant_id`=@tenantId"};
        var title=pageSearch.sqlTitle.ToLowerInvariant();if(title.Contains("asn_status")){var status=Convert.ToByte(title.Trim().Replace("asn_status","").Replace("：","").Replace(":","").Replace("=",""));if(status!=4){clauses.Add("m.`asn_status`=@status");filter.Parameters.Add("status",status);}}
        if(!string.IsNullOrWhiteSpace(filter.Sql))clauses.Add(filter.Sql);filter.Parameters.Add("tenantId",user.tenant_id);filter.Parameters.Add("offset",(pageSearch.pageIndex-1)*pageSearch.pageSize);filter.Parameters.Add("pageSize",pageSearch.pageSize);
        var where=string.Join(" AND ",clauses);await using var c=await _connectionFactory.OpenConnectionAsync();using var grid=await c.QueryMultipleAsync($"""
          SELECT COUNT(*) FROM `wms_asnmaster` m WHERE {where};
          SELECT m.* FROM `wms_asnmaster` m WHERE {where} ORDER BY m.`last_update_time` DESC LIMIT @pageSize OFFSET @offset;
          """,filter.Parameters);var total=await grid.ReadSingleAsync<int>();var masters=(await grid.ReadAsync<AsnmasterBothViewModel>()).AsList();await FillMasterDetailsAsync(c,masters);return(masters,total);
    }

    /// <summary>获取 ASN 主数据及关联信息。</summary>
    public async Task<AsnmasterBothViewModel> GetAsnmasterAsync(int id,CurrentUser user)
    {
        await using var c=await _connectionFactory.OpenConnectionAsync();var master=await c.QuerySingleOrDefaultAsync<AsnmasterBothViewModel>($"{MasterSelect} WHERE `id`=@id AND `tenant_id`=@tenantId LIMIT 1;",new{id,tenantId=user.tenant_id})??new();
        if(master.id>0)await FillMasterDetailsAsync(c,[master]);return master;
    }

    /// <summary>
    /// 执行 AddAsnmasterAsync 操作。
    /// </summary>
    public async Task<(int id,string msg)> AddAsnmasterAsync(AsnmasterBothViewModel vm,CurrentUser user)
    {
        var no=await _functionHelper.GetFormNoAsync("Asnmaster");var now=DateTime.Now;await using var c=await _connectionFactory.OpenConnectionAsync();await using var tx=await c.BeginTransactionAsync();
        var id=await c.ExecuteScalarAsync<int>("""
          INSERT INTO `wms_asnmaster` (`asn_no`,`asn_batch`,`estimated_arrival_time`,`asn_status`,`weight`,`volume`,`goods_owner_id`,`goods_owner_name`,`creator`,`create_time`,`last_update_time`,`tenant_id`)
          VALUES (@no,@asn_batch,@estimated_arrival_time,0,@weight,@volume,@goods_owner_id,@goods_owner_name,@creator,@now,@now,@tenantId);SELECT LAST_INSERT_ID();
          """,new{no,vm.asn_batch,vm.estimated_arrival_time,vm.weight,vm.volume,vm.goods_owner_id,vm.goods_owner_name,creator=user.user_name,now,tenantId=user.tenant_id},tx);
        foreach(var d in vm.detailList)await InsertDetailAsync(c,tx,id,no,d,vm.goods_owner_id,vm.goods_owner_name,user,now);
        await tx.CommitAsync();return id>0?(id,_stringLocalizer["save_success"]):(0,_stringLocalizer["save_failed"]);
    }

    /// <summary>
    /// 执行 UpdateAsnmasterAsync 操作。
    /// </summary>
    public async Task<(bool flag,string msg)> UpdateAsnmasterAsync(AsnmasterBothViewModel vm,CurrentUser user)
    {
        await using var c=await _connectionFactory.OpenConnectionAsync();await using var tx=await c.BeginTransactionAsync();
        var exists=await c.ExecuteScalarAsync<bool>("SELECT EXISTS(SELECT 1 FROM `wms_asnmaster` WHERE `id`=@id);",new{vm.id},tx);if(!exists)return(false,_stringLocalizer["not_exists_entity"]);
        var now=DateTime.Now;await c.ExecuteAsync("""
          UPDATE `wms_asnmaster` SET `asn_batch`=@asn_batch,`estimated_arrival_time`=@estimated_arrival_time,`weight`=@weight,`volume`=@volume,
          `goods_owner_id`=@goods_owner_id,`goods_owner_name`=@goods_owner_name,`last_update_time`=@now WHERE `id`=@id;
          """,new{vm.id,vm.asn_batch,vm.estimated_arrival_time,vm.weight,vm.volume,vm.goods_owner_id,vm.goods_owner_name,now},tx);
        foreach(var d in vm.detailList.Where(x=>x.id>0))await c.ExecuteAsync("""
          UPDATE `wms_asn` SET `spu_id`=@spu_id,`sku_id`=@sku_id,`asn_qty`=@asn_qty,`actual_qty`=@actual_qty,`weight`=@weight,
          `volume`=@volume,`supplier_id`=@supplier_id,`supplier_name`=@supplier_name,`goods_owner_id`=@ownerId,`goods_owner_name`=@ownerName,
          `last_update_time`=@now,`price`=@price WHERE `id`=@id;
          """,new{d.id,d.spu_id,d.sku_id,d.asn_qty,d.actual_qty,d.weight,d.volume,d.supplier_id,d.supplier_name,ownerId=vm.goods_owner_id,ownerName=vm.goods_owner_name,now,d.price},tx);
        foreach(var d in vm.detailList.Where(x=>x.id==0))await InsertDetailAsync(c,tx,vm.id,vm.asn_no,d,vm.goods_owner_id,vm.goods_owner_name,user,now);
        var del=vm.detailList.Where(x=>x.id<0).Select(x=>-x.id).ToList();if(del.Count>0)await c.ExecuteAsync("DELETE FROM `wms_asn` WHERE `id` IN @del;",new{del},tx);
        await tx.CommitAsync();return(true,_stringLocalizer["save_success"]);
    }

    /// <summary>
    /// 执行 DeleteAsnmasterAsync 操作。
    /// </summary>
    public async Task<(bool flag,string msg)> DeleteAsnmasterAsync(int id)
    {
        await using var c=await _connectionFactory.OpenConnectionAsync();await using var tx=await c.BeginTransactionAsync();var qty=await c.ExecuteAsync("DELETE FROM `wms_asn` WHERE `asnmaster_id`=@id;",new{id},tx);qty+=await c.ExecuteAsync("DELETE FROM `wms_asnmaster` WHERE `id`=@id;",new{id},tx);await tx.CommitAsync();return qty>0?(true,_stringLocalizer["delete_success"]):(false,_stringLocalizer["delete_failed"]);
    }

    /// <summary>获取 ASN 打印序列号。</summary>
    public async Task<List<AsnPrintSeriesNumberViewModel>> GetAsnPrintSeriesNumberAsync(List<int> input)
    {
        await using var c=await _connectionFactory.OpenConnectionAsync();return(await c.QueryAsync<AsnPrintSeriesNumberViewModel>("""
          SELECT a.`id` AS `asn_id`,m.`id` AS `asnmaster_id`,m.`asn_no`,a.`sku_id`,k.`sku_code`,k.`sku_name`,p.`spu_code`,p.`spu_name`,s.`series_number`
          FROM `wms_asnmaster` m JOIN `wms_asn` a ON a.`asnmaster_id`=m.`id` JOIN `wms_spu` p ON p.`id`=a.`spu_id`
          JOIN `wms_sku` k ON k.`id`=a.`sku_id` JOIN `wms_asnsort` s ON s.`asn_id`=a.`id` WHERE a.`id` IN @input ORDER BY a.`id`;
          """,new{input})).AsList();
    }

    private async Task FillMasterDetailsAsync(MySqlConnector.MySqlConnection c,List<AsnmasterBothViewModel> masters)
    { if(masters.Count==0)return;var ids=masters.Select(x=>x.id).ToList();var rows=(await c.QueryAsync<AsnmasterDetailViewModel>($"{MasterDetailSelect} WHERE a.`asnmaster_id` IN @ids;",new{ids})).AsList();foreach(var m in masters)m.detailList=rows.Where(x=>x.asnmaster_id==m.id).ToList(); }

    private static Task<int> InsertDetailAsync(MySqlConnector.MySqlConnection c,MySqlConnector.MySqlTransaction tx,int masterId,string no,AsnmasterDetailViewModel d,int ownerId,string ownerName,CurrentUser user,DateTime now)=>c.ExecuteAsync("""
      INSERT INTO `wms_asn` (`asnmaster_id`,`asn_no`,`asn_status`,`spu_id`,`sku_id`,`asn_qty`,`actual_qty`,`arrival_time`,
      `unload_time`,`unload_person_id`,`unload_person`,`sorted_qty`,`shortage_qty`,`more_qty`,`damage_qty`,`weight`,`volume`,
      `supplier_id`,`supplier_name`,`goods_owner_id`,`goods_owner_name`,`creator`,`create_time`,`last_update_time`,`is_valid`,`tenant_id`,`expiry_date`,`price`)
      VALUES (@masterId,@no,0,@spu_id,@sku_id,@asn_qty,@actual_qty,@min,@min,0,'',0,0,0,0,@weight,@volume,@supplier_id,
      @supplier_name,@ownerId,@ownerName,@creator,@now,@now,1,@tenantId,@min,@price);
      """,new{masterId,no,d.spu_id,d.sku_id,d.asn_qty,d.actual_qty,d.weight,d.volume,d.supplier_id,d.supplier_name,ownerId,ownerName,creator=user.user_name,now,tenantId=user.tenant_id,d.price,min=UtilConvert.MinDate},tx);

    private async Task<(bool flag,string msg)> ChangeRowsAsync(List<int> ids,byte expected,byte next,string errorKey,string successKind,Dictionary<int,object?>? arrival,bool resetArrival=false)
    {
        await using var c=await _connectionFactory.OpenConnectionAsync();await using var tx=await c.BeginTransactionAsync();var rows=(await c.QueryAsync<AsnEntity>("SELECT * FROM `wms_asn` WHERE `id` IN @ids FOR UPDATE;",new{ids},tx)).AsList();
        if(rows.Count==0)return(false,"[202]"+_stringLocalizer["not_exists_entity"]);if(rows.Any(x=>x.asn_status!=expected))return(false,"[202]"+_stringLocalizer[errorKey]);var now=DateTime.Now;
        foreach(var r in rows)await c.ExecuteAsync("UPDATE `wms_asn` SET `asn_status`=@next,`arrival_time`=@arrivalTime,`last_update_time`=@now WHERE `id`=@id;",new{r.id,next,arrivalTime=resetArrival?UtilConvert.MinDate:arrival?.GetValueOrDefault(r.id)??r.arrival_time,now},tx);
        var master=rows[0].asnmaster_id;if(!await c.ExecuteScalarAsync<bool>("SELECT EXISTS(SELECT 1 FROM `wms_asnmaster` WHERE `id`=@master);",new{master},tx))return(false,"[202]"+_stringLocalizer["not_exists_entity"]);await c.ExecuteAsync("UPDATE `wms_asnmaster` SET `last_update_time`=@now WHERE `id`=@master;",new{master,now},tx);await tx.CommitAsync();return(true,_stringLocalizer[successKind=="confirm"?"confirm_success":"save_success"]);
    }

    private async Task<(bool flag,string msg)> ResetUnloadAsync(List<int> ids)
    { await using var c=await _connectionFactory.OpenConnectionAsync();var rows=(await c.QueryAsync<AsnEntity>("SELECT * FROM `wms_asn` WHERE `id` IN @ids;",new{ids})).AsList();if(rows.Count==0)return(false,"[202]"+_stringLocalizer["not_exists_entity"]);if(rows.Any(x=>x.asn_status!=2))return(false,"[202]"+_stringLocalizer["ASN_Status_Is_Not_Pre_Load"]);var qty=await c.ExecuteAsync("UPDATE `wms_asn` SET `asn_status`=1,`unload_time`=@min,`unload_person_id`=0,`unload_person`='',`last_update_time`=@now WHERE `id` IN @ids;",new{ids,min=UtilConvert.MinDate,now=DateTime.Now});return WriteResult(qty,"save"); }
    private static Task<bool> ExistsAsync(MySqlConnector.MySqlConnection c,int id)=>c.ExecuteScalarAsync<bool>("SELECT EXISTS(SELECT 1 FROM `wms_asn` WHERE `id`=@id);",new{id});
    private (bool flag,string msg) WriteResult(int qty,string kind)=>qty>0?(true,_stringLocalizer[kind+"_success"]):(false,_stringLocalizer[kind+"_failed"]);
}
