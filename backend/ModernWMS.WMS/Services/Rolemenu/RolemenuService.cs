using System.Data;
using Dapper;
using Microsoft.Extensions.Localization;
using ModernWMS.Core.Database;
using ModernWMS.Core.JWT;
using ModernWMS.Core.Models;
using ModernWMS.Core.Services;
using ModernWMS.Core.Utility;
using ModernWMS.WMS.Entities.Models;
using ModernWMS.WMS.Entities.ViewModels;
using ModernWMS.WMS.IServices;
using MySqlConnector;

namespace ModernWMS.WMS.Services;

public class RolemenuService : BaseService<RolemenuEntity>, IRolemenuService
{
    private const int MaxMenuActionAuthorityLength = 64;
    private const string AdminRoleName = "admin";
    private const string AdminRolePermissionMessageKey = "admin_role_permission_readonly";
    private readonly IMySqlConnectionFactory _connectionFactory;
    private readonly IStringLocalizer<Core.MultiLanguage> _stringLocalizer;

    public RolemenuService(IMySqlConnectionFactory connectionFactory, IStringLocalizer<Core.MultiLanguage> stringLocalizer)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        _stringLocalizer = stringLocalizer ?? throw new ArgumentNullException(nameof(stringLocalizer));
    }

    public async Task<List<RolemenuListViewModel>> GetAllAsync(CurrentUser currentUser)
    {
        await using var db = await _connectionFactory.OpenConnectionAsync();
        return (await db.QueryAsync<RolemenuListViewModel>("""
            SELECT g.`userrole_id`, r.`role_name`, r.`is_valid`, g.`create_time`, g.`last_update_time`
            FROM (SELECT `userrole_id`, MIN(`create_time`) `create_time`, MAX(`last_update_time`) `last_update_time`
                  FROM `wms_rolemenu` WHERE `tenant_id`=@tenantId GROUP BY `userrole_id`) g
            JOIN `wms_userrole` r ON r.`id`=g.`userrole_id` AND r.`tenant_id`=@tenantId;
            """, new { tenantId = currentUser.tenant_id })).AsList();
    }

    public async Task<RolemenuBothViewModel> GetAsync(int userrole_id)
    {
        await using var db = await _connectionFactory.OpenConnectionAsync();
        var rows = (await db.QueryAsync<RolemenuDetailRow>("""
            SELECT rm.`id`,rm.`userrole_id`,r.`role_name`,r.`is_valid`,rm.`menu_id`,m.`menu_name`,
                   rm.`authority`,rm.`menu_actions_authority`
            FROM `wms_rolemenu` rm JOIN `wms_menu` m ON m.`id`=rm.`menu_id`
            JOIN `wms_userrole` r ON r.`id`=rm.`userrole_id`
            WHERE rm.`userrole_id`=@userroleId ORDER BY r.`role_name`,m.`sort`,m.`menu_name`;
            """, new { userroleId = userrole_id })).AsList();
        if (rows.Count == 0) return new RolemenuBothViewModel();
        return new RolemenuBothViewModel
        {
            userrole_id = rows[0].userrole_id, role_name = rows[0].role_name, is_valid = rows[0].is_valid,
            detailList = rows.Select(x => new RolemenuViewModel
            {
                id=x.id, menu_id=x.menu_id, menu_name=x.menu_name, authority=x.authority,
                menu_actions_authority=JsonHelper.DeserializeObject<List<string>>(x.menu_actions_authority)
            }).ToList()
        };
    }

    public async Task<List<MenuViewModel>> GetAllMenusAsync(CurrentUser currentUser)
    {
        await using var db = await _connectionFactory.OpenConnectionAsync();
        var rows = await db.QueryAsync<MenuRow>(MenuColumnsSql + " WHERE `tenant_id`=@tenantId;", new { tenantId=currentUser.tenant_id });
        return rows.Select(x => ToMenu(x, false)).ToList();
    }

    public async Task<List<MenuViewModel>> GetMenusByRoleId(int userrole_id, CurrentUser currentUser)
    {
        await using var db = await _connectionFactory.OpenConnectionAsync();
        var role = await GetRoleAsync(db, null, userrole_id, currentUser.tenant_id);
        if (role == null) return [];
        if (IsAdminRole(role.role_name))
        {
            var rows = await db.QueryAsync<MenuRow>(MenuColumnsSql + " WHERE `tenant_id`=@tenantId ORDER BY `sort`,`menu_name`;", new { tenantId=currentUser.tenant_id });
            return rows.Select(row => ToMenu(row, false)).ToList();
        }
        var menus = await db.QueryAsync<MenuRow>("""
            SELECT m.`id`,m.`menu_name`,m.`module`,m.`vue_path`,m.`vue_path_detail`,m.`vue_directory`,m.`sort`,
                   rm.`menu_actions_authority` `menu_actions`
            FROM `wms_rolemenu` rm JOIN `wms_menu` m ON m.`id`=rm.`menu_id`
            WHERE rm.`userrole_id`=@roleId AND rm.`tenant_id`=@tenantId AND m.`tenant_id`=@tenantId
            ORDER BY m.`sort`,m.`menu_name`;
            """, new { roleId=userrole_id, tenantId=currentUser.tenant_id });
        return menus.Select(row => ToMenu(row, false)).ToList();
    }

    public async Task<(int id, string msg)> AddAsync(RolemenuBothViewModel viewModel, CurrentUser currentUser)
    {
        await using var db = await _connectionFactory.OpenConnectionAsync();
        var status = await GetRoleStatusAsync(db, viewModel.userrole_id, currentUser.tenant_id);
        if (!status.exists) return (0, _stringLocalizer["not_exists_entity"]);
        if (status.admin) return (0, _stringLocalizer[AdminRolePermissionMessageKey]);
        if (await RolemenuExistsAsync(db, viewModel.userrole_id, currentUser.tenant_id))
            return (0, string.Format(_stringLocalizer["exists_entity"], _stringLocalizer["role_name"], viewModel.role_name));
        var result = await BatchUpdateAsync(CreateBatch(viewModel), currentUser);
        return result.flag ? (viewModel.userrole_id, result.msg) : (0, result.msg);
    }

    public async Task<(bool flag, string msg)> UpdateAsync(RolemenuBothViewModel viewModel, CurrentUser currentUser)
    {
        await using var db = await _connectionFactory.OpenConnectionAsync();
        var status = await GetRoleStatusAsync(db, viewModel.userrole_id, currentUser.tenant_id);
        if (!status.exists) return (false, _stringLocalizer["not_exists_entity"]);
        if (status.admin) return (false, _stringLocalizer[AdminRolePermissionMessageKey]);
        if (!await RolemenuExistsAsync(db, viewModel.userrole_id, currentUser.tenant_id))
            return (false, _stringLocalizer["not_exists_entity"]);
        return await BatchUpdateAsync(CreateBatch(viewModel), currentUser);
    }

    public async Task<(bool flag, string msg)> BatchUpdateAsync(RolemenuBatchViewModel viewModel, CurrentUser currentUser)
    {
        await using var db = await _connectionFactory.OpenConnectionAsync();
        await using var tx = await db.BeginTransactionAsync(IsolationLevel.Serializable);
        try
        {
            var result = await BatchUpdateCoreAsync(db, tx, viewModel, currentUser);
            if (result.flag) await tx.CommitAsync(); else await tx.RollbackAsync();
            return result;
        }
        catch { await tx.RollbackAsync(); throw; }
    }

    public async Task<List<long>> GetWarehouseIdsAsync(int userrole_id, CurrentUser currentUser)
    {
        await EnsureWarehouseManagementAllowedAsync(currentUser);
        await using var db = await _connectionFactory.OpenConnectionAsync();
        if (await GetRoleAsync(db, null, userrole_id, currentUser.tenant_id) == null) return [];
        return (await db.QueryAsync<long>("SELECT DISTINCT `warehouse_id` FROM `wms_role_warehouse` WHERE `role_id`=@roleId ORDER BY `warehouse_id`;", new { roleId=userrole_id })).AsList();
    }

    public async Task<(bool flag, string msg)> ReplaceWarehousesAsync(RoleWarehouseBindingViewModel viewModel, CurrentUser currentUser)
    {
        await EnsureWarehouseManagementAllowedAsync(currentUser);
        var input = viewModel.warehouse_ids ?? [];
        var ids = input.Where(x => x > 0).Distinct().OrderBy(x => x).ToList();
        if (ids.Count != input.Distinct().Count()) return (false, "invalid warehouse_id");

        await using var db = await _connectionFactory.OpenConnectionAsync();
        await using var tx = await db.BeginTransactionAsync(IsolationLevel.Serializable);
        try
        {
            var role = await GetRoleAsync(db, tx, viewModel.userrole_id, currentUser.tenant_id, true);
            if (role == null) return await Rollback(false, _stringLocalizer["not_exists_entity"], tx);
            if (IsAdminRole(role.role_name)) return await Rollback(false, _stringLocalizer[AdminRolePermissionMessageKey], tx);
            var valid = ids.Count == 0 ? [] : (await db.QueryAsync<long>("SELECT `id` FROM `erp_warehouse` WHERE `id` IN @ids AND `deleted`=0;", new { ids }, tx)).AsList();
            var invalid = ids.Except(valid).ToList();
            if (invalid.Count > 0) return await Rollback(false, $"invalid warehouse_id: {string.Join(",", invalid)}", tx);

            await db.ExecuteAsync("DELETE FROM `wms_role_warehouse` WHERE `role_id`=@roleId;", new { roleId=viewModel.userrole_id }, tx);
            var now=DateTime.Now;
            foreach (var warehouseId in ids)
                await db.ExecuteAsync("""
                    INSERT INTO `wms_role_warehouse` (`role_id`,`warehouse_id`,`tenant_id`,`created_by`,`create_time`,`last_update_time`)
                    VALUES (@roleId,@warehouseId,@tenantId,@createdBy,@now,@now);
                    """, new { roleId=viewModel.userrole_id, warehouseId, tenantId=currentUser.tenant_id, createdBy=currentUser.user_id, now }, tx);
            await tx.CommitAsync();
            return (true, _stringLocalizer["save_success"]);
        }
        catch { await tx.RollbackAsync(); throw; }
    }

    private async Task EnsureWarehouseManagementAllowedAsync(CurrentUser currentUser)
    {
        var roleName=currentUser.user_role?.Trim();
        if (string.IsNullOrEmpty(roleName)) throw new UnauthorizedAccessException("warehouse management permission required");
        await using var db=await _connectionFactory.OpenConnectionAsync();
        var roles=(await db.QueryAsync<string>("""
            SELECT `role_name` FROM `wms_userrole`
            WHERE `tenant_id`=@tenantId AND `is_valid`=1 AND UPPER(TRIM(`role_name`))=UPPER(TRIM(@roleName));
            """, new { tenantId=currentUser.tenant_id, roleName })).AsList();
        if (roles.Count==0 || !roles.Any(IsAdminRole)) throw new UnauthorizedAccessException("warehouse management permission required");
    }

    private async Task<(bool flag,string msg)> BatchUpdateCoreAsync(MySqlConnection db, MySqlTransaction tx, RolemenuBatchViewModel viewModel, CurrentUser user)
    {
        if (viewModel.detailList==null) return (false,"detailList is required");
        var role=await GetRoleAsync(db,tx,viewModel.userrole_id,user.tenant_id,true);
        if (role==null) return (false,_stringLocalizer["not_exists_entity"]);
        if (IsAdminRole(role.role_name)) return (false,_stringLocalizer[AdminRolePermissionMessageKey]);
        var details=viewModel.detailList;
        if (details.Any(x=>x.menu_id<=0)) return (false,"invalid menu_id");
        if (details.SelectMany(x=>Normalize(x.menu_actions_authority)).Any(x=>x.Length>MaxMenuActionAuthorityLength))
            return (false,$"menu_actions_authority length must be less than or equal to {MaxMenuActionAuthorityLength}");
        var duplicates=details.GroupBy(x=>x.menu_id).Where(x=>x.Count()>1).Select(x=>x.Key).ToList();
        if (duplicates.Count>0) return (false,$"duplicate menu_id: {string.Join(",",duplicates)}");

        var menuIds=details.Select(x=>x.menu_id).ToList();
        var menus=menuIds.Count==0 ? [] : (await db.QueryAsync<MenuPermissionRow>("SELECT `id`,`menu_actions` FROM `wms_menu` WHERE `tenant_id`=@tenantId AND `id` IN @menuIds;",new { tenantId=user.tenant_id,menuIds },tx)).AsList();
        var invalidIds=menuIds.Except(menus.Select(x=>x.id)).ToList();
        if (invalidIds.Count>0) return (false,$"invalid menu_id: {string.Join(",",invalidIds)}");
        var whiteLists=menus.ToDictionary(x=>x.id,x=>Normalize(JsonHelper.DeserializeObject<List<string>>(x.menu_actions)));
        foreach(var detail in details)
        {
            var allowed=whiteLists[detail.menu_id];
            if (allowed.Count==0) continue;
            var set=allowed.ToHashSet(StringComparer.Ordinal);
            var invalid=Normalize(detail.menu_actions_authority).Where(x=>!set.Contains(x)).ToList();
            if(invalid.Count>0) return(false,$"invalid menu_actions_authority: {string.Join(",",invalid)}");
        }

        var existing=(await db.QueryAsync<RolemenuEntity>("""
            SELECT `id`,`userrole_id`,`menu_id`,`authority`,`create_time`,`last_update_time`,`tenant_id`,`menu_actions_authority`
            FROM `wms_rolemenu` WHERE `userrole_id`=@roleId AND `tenant_id`=@tenantId FOR UPDATE;
            """,new { roleId=viewModel.userrole_id,tenantId=user.tenant_id },tx)).AsList();
        var groups=existing.GroupBy(x=>x.menu_id).ToDictionary(x=>x.Key,x=>x.OrderBy(y=>y.id).ToList());
        var payloadIds=menuIds.ToHashSet(); var deleteIds=new List<int>(); var now=DateTime.Now;
        foreach(var detail in details)
        {
            var authority=Serialize(detail.menu_actions_authority);
            if(groups.TryGetValue(detail.menu_id,out var current))
            {
                var entity=current[0];
                if(entity.authority!=1 || entity.menu_actions_authority!=authority)
                    await db.ExecuteAsync("UPDATE `wms_rolemenu` SET `authority`=1,`menu_actions_authority`=@authority,`last_update_time`=@now WHERE `id`=@id;",new { authority,now,entity.id },tx);
                deleteIds.AddRange(current.Skip(1).Select(x=>x.id));
            }
            else await db.ExecuteAsync("""
                INSERT INTO `wms_rolemenu` (`userrole_id`,`menu_id`,`authority`,`menu_actions_authority`,`create_time`,`last_update_time`,`tenant_id`)
                VALUES (@roleId,@menuId,1,@authority,@now,@now,@tenantId);
                """,new { roleId=viewModel.userrole_id,menuId=detail.menu_id,authority,now,tenantId=user.tenant_id },tx);
        }
        deleteIds.AddRange(existing.Where(x=>!payloadIds.Contains(x.menu_id)).Select(x=>x.id));
        if(deleteIds.Count>0) await db.ExecuteAsync("DELETE FROM `wms_rolemenu` WHERE `id` IN @ids;",new { ids=deleteIds.Distinct().ToArray() },tx);
        return(true,_stringLocalizer["save_success"]);
    }

    public async Task<(bool flag,string msg)> DeleteAsync(int userrole_id,CurrentUser currentUser)
    {
        await using var db=await _connectionFactory.OpenConnectionAsync();
        var status=await GetRoleStatusAsync(db,userrole_id,currentUser.tenant_id);
        if(!status.exists) return(false,_stringLocalizer["not_exists_entity"]);
        if(status.admin) return(false,_stringLocalizer[AdminRolePermissionMessageKey]);
        var count=await db.ExecuteAsync("DELETE FROM `wms_rolemenu` WHERE `userrole_id`=@roleId AND `tenant_id`=@tenantId;",new { roleId=userrole_id,tenantId=currentUser.tenant_id });
        return count>0 ? (true,_stringLocalizer["delete_success"]) : (false,_stringLocalizer["delete_failed"]);
    }

    private async Task<(bool exists,bool admin)> GetRoleStatusAsync(MySqlConnection db,int id,long tenantId)
    { var role=await GetRoleAsync(db,null,id,tenantId); return(role!=null,role!=null&&IsAdminRole(role.role_name)); }
    private static Task<bool> RolemenuExistsAsync(MySqlConnection db,int roleId,long tenantId)=>db.ExecuteScalarAsync<bool>("SELECT EXISTS(SELECT 1 FROM `wms_rolemenu` WHERE `userrole_id`=@roleId AND `tenant_id`=@tenantId);",new { roleId,tenantId });
    private static Task<RoleRow?> GetRoleAsync(MySqlConnection db,MySqlTransaction? tx,int roleId,long tenantId,bool forUpdate=false)=>db.QuerySingleOrDefaultAsync<RoleRow>($"SELECT `id`,`role_name` FROM `wms_userrole` WHERE `id`=@roleId AND `tenant_id`=@tenantId LIMIT 1{(forUpdate?" FOR UPDATE":"")};",new { roleId,tenantId },tx);
    private static async Task<(bool flag,string msg)> Rollback(bool flag,string msg,MySqlTransaction tx){await tx.RollbackAsync();return(flag,msg);}
    private static MenuViewModel ToMenu(MenuRow x,bool normalize=true)=>new(){id=x.id,menu_name=x.menu_name,module=x.module,vue_path=x.vue_path,vue_path_detail=x.vue_path_detail,vue_directory=x.vue_directory,sort=x.sort,menu_actions=normalize?Normalize(JsonHelper.DeserializeObject<List<string>>(x.menu_actions)):JsonHelper.DeserializeObject<List<string>>(x.menu_actions)};
    private static string Serialize(List<string> actions)=>JsonHelper.SerializeObject(Normalize(actions));
    private static RolemenuBatchViewModel CreateBatch(RolemenuBothViewModel x)=>new(){userrole_id=x.userrole_id,detailList=x.detailList?.Where(y=>y.id>=0).Select(y=>new RolemenuBatchDetailViewModel{menu_id=y.menu_id,menu_actions_authority=y.menu_actions_authority}).ToList()};
    private static List<string> Normalize(List<string> actions)=>(actions??[]).Select(x=>x?.Trim()).Where(x=>!string.IsNullOrWhiteSpace(x)).Select(x=>x!).Distinct(StringComparer.Ordinal).OrderBy(x=>x,StringComparer.Ordinal).ToList();
    private static bool IsAdminRole(string roleName)=>string.Equals(roleName?.Trim(),AdminRoleName,StringComparison.OrdinalIgnoreCase);
    private const string MenuColumnsSql="SELECT `id`,`menu_name`,`module`,`vue_path`,`vue_path_detail`,`vue_directory`,`sort`,`menu_actions` FROM `wms_menu`";

    private sealed class RoleRow { public int id {get;init;} public string role_name {get;init;}=string.Empty; }
    private sealed class RolemenuDetailRow { public int id{get;init;} public int userrole_id{get;init;} public string role_name{get;init;}=string.Empty; public bool is_valid{get;init;} public int menu_id{get;init;} public string menu_name{get;init;}=string.Empty; public byte authority{get;init;} public string menu_actions_authority{get;init;}="[]"; }
    private sealed class MenuRow { public int id{get;init;} public string menu_name{get;init;}=string.Empty; public string module{get;init;}=string.Empty; public string vue_path{get;init;}=string.Empty; public string vue_path_detail{get;init;}=string.Empty; public string vue_directory{get;init;}=string.Empty; public int sort{get;init;} public string menu_actions{get;init;}="[]"; }
    private sealed class MenuPermissionRow { public int id{get;init;} public string menu_actions{get;init;}="[]"; }
}
