/*
 * date：2022-12-20
 * developer：AMo
 */
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using ModernWMS.Core.DBContext;
using ModernWMS.Core.JWT;
using ModernWMS.Core.Models;
using ModernWMS.Core.Services;
using ModernWMS.Core.Utility;
using ModernWMS.WMS.Entities.Models;
using ModernWMS.WMS.Entities.ViewModels;
using ModernWMS.WMS.IServices;
using System.Data;

namespace ModernWMS.WMS.Services
{
    /// <summary>
    ///  Rolemenu Service
    /// </summary>
    public class RolemenuService : BaseService<RolemenuEntity>, IRolemenuService
    {
        #region Args
        /// <summary>
        /// The DBContext
        /// </summary>
        private readonly SqlDBContext _dBContext;

        /// <summary>
        /// Localizer Service
        /// </summary>
        private readonly IStringLocalizer<Core.MultiLanguage> _stringLocalizer;

        private const int MaxMenuActionAuthorityLength = 64;
        #endregion

        #region constructor
        /// <summary>
        ///Rolemenu  constructor
        /// </summary>
        /// <param name="dBContext">The DBContext</param>
        /// <param name="stringLocalizer">Localizer</param>
        public RolemenuService(
            SqlDBContext dBContext
          , IStringLocalizer<ModernWMS.Core.MultiLanguage> stringLocalizer
            )
        {
            this._dBContext = dBContext;
            this._stringLocalizer = stringLocalizer;
        }
        #endregion

        #region Api
        /// <summary>
        /// Get all records
        /// </summary>
        /// <param name="currentUser">currentUser</param>
        /// <returns></returns>
        public async Task<List<RolemenuListViewModel>> GetAllAsync(CurrentUser currentUser)
        {
            var Rolemenus = _dBContext.GetDbSet<RolemenuEntity>();
            var Userroles = _dBContext.GetDbSet<UserroleEntity>();
            var queryMenusGroup = Rolemenus.AsNoTracking()
               .Where(t => t.tenant_id == currentUser.tenant_id)
               .GroupBy(g => new { g.userrole_id })
               .Select(g => new
               {
                   userrole_id = g.Key.userrole_id,
                   create_time = g.Min(t => t.create_time),
                   last_update_time = g.Max(t => t.last_update_time)
               });
            var data = await (from g in queryMenusGroup
                              join r in Userroles.AsNoTracking().Where(t => t.tenant_id == currentUser.tenant_id)
                              on g.userrole_id equals r.id
                              select new RolemenuListViewModel
                              {
                                  userrole_id = g.userrole_id,
                                  role_name = r.role_name,
                                  is_valid = r.is_valid,
                                  create_time = g.create_time,
                                  last_update_time = g.last_update_time
                              }).ToListAsync();
            return data;
        }

        /// <summary>
        /// Get a record by id
        /// </summary>
        /// <param name="userrole_id">userrole id</param>
        /// <returns></returns>
        public async Task<RolemenuBothViewModel> GetAsync(int userrole_id)
        {
            var Rolemenus = _dBContext.GetDbSet<RolemenuEntity>();
            var Userroles = _dBContext.GetDbSet<UserroleEntity>();
            var Menus = _dBContext.GetDbSet<MenuEntity>();
            var entities = await (from rm in Rolemenus.AsNoTracking()
                                  join m in Menus.AsNoTracking() on rm.menu_id equals m.id
                                  join r in Userroles.AsNoTracking() on rm.userrole_id equals r.id
                                  where rm.userrole_id == userrole_id
                                  orderby r.role_name, m.sort, m.menu_name
                                  select new
                                  {
                                      rm.id,
                                      rm.userrole_id,
                                      r.role_name,
                                      r.is_valid,
                                      rm.menu_id,
                                      m.menu_name,
                                      rm.authority,
                                      rm.menu_actions_authority,
                                      rm.create_time,
                                      rm.last_update_time
                                  }).ToListAsync();
            if (entities.Any())
            {
                var data = new RolemenuBothViewModel
                {
                    userrole_id = entities.First().userrole_id,
                    role_name = entities.First().role_name,
                    is_valid = entities.First().is_valid,
                    detailList = entities.Select(t => new RolemenuViewModel
                    {
                        id = t.id,
                        menu_id = t.menu_id,
                        menu_name = t.menu_name,
                        authority = t.authority,
                        menu_actions_authority = JsonHelper.DeserializeObject<List<string>>(t.menu_actions_authority)
                    }).ToList()
                };
                return data;
            }
            else
            {
                return new RolemenuBothViewModel();
            }
        }
        /// <summary>
        /// Get all menus
        /// </summary>
        /// <param name="currentUser">currentUser</param>
        /// <returns></returns>
        public async Task<List<MenuViewModel>> GetAllMenusAsync(CurrentUser currentUser)
        {
            var Menus = _dBContext.GetDbSet<MenuEntity>();
            var data = await Menus.AsNoTracking()
                .Where(t => t.tenant_id == currentUser.tenant_id)
                .Select(m => new 
                {
                    id = m.id,
                    menu_name = m.menu_name,
                    module = m.module,
                    vue_path = m.vue_path,
                    vue_path_detail = m.vue_path_detail,
                    vue_directory = m.vue_directory,
                    sort = m.sort,
                    menu_actions = m.menu_actions
                }).ToListAsync();

            var result = data.Select(m => new MenuViewModel
            {
                id = m.id,
                menu_name = m.menu_name,
                module = m.module,
                vue_path = m.vue_path,
                vue_path_detail = m.vue_path_detail,
                vue_directory = m.vue_directory,
                sort = m.sort,
                menu_actions = JsonHelper.DeserializeObject<List<string>>(m.menu_actions)
            }).ToList();
            return result;
        }
        /// <summary>
        /// Get menu's authority by user role id
        /// </summary>
        /// <param name="userrole_id">user role id</param>
        /// <returns></returns>
        public async Task<List<MenuViewModel>> GetMenusByRoleId(int userrole_id)
        {
            var Rolemenus = _dBContext.GetDbSet<RolemenuEntity>(); 
            var Menus = _dBContext.GetDbSet<MenuEntity>();
            var data = await (from rm in Rolemenus.AsNoTracking()
                              join m in Menus.AsNoTracking() on rm.menu_id equals m.id
                              where rm.userrole_id == userrole_id
                              orderby m.sort, m.menu_name
                              select new 
                              {
                                  id = m.id,
                                  menu_name = m.menu_name,
                                  module = m.module,
                                  vue_path = m.vue_path,
                                  vue_path_detail = m.vue_path_detail,
                                  vue_directory = m.vue_directory,
                                  sort = m.sort,
                                  rm.menu_actions_authority
                              }).ToListAsync();
            if (data.Any())
            {
                var result = data.Select(m => new MenuViewModel
                {
                    id = m.id,
                    menu_name = m.menu_name,
                    module = m.module,
                    vue_path = m.vue_path,
                    vue_path_detail = m.vue_path_detail,
                    vue_directory = m.vue_directory,
                    sort = m.sort,
                    menu_actions = JsonHelper.DeserializeObject<List<string>>(m.menu_actions_authority)
                }).ToList();
                return result;
            }
            return new List<MenuViewModel>();
        }
        /// <summary>
        /// add a new record
        /// </summary>
        /// <param name="viewModel">viewmodel</param>
        /// <param name="currentUser">currentUser</param>
        /// <returns></returns>
        public async Task<(int id, string msg)> AddAsync(RolemenuBothViewModel viewModel, CurrentUser currentUser)
        {
            var Rolemenus = _dBContext.GetDbSet<RolemenuEntity>();
            if (await Rolemenus.AnyAsync(t => t.userrole_id.Equals(viewModel.userrole_id)))
            {
                return (0, string.Format(_stringLocalizer["exists_entity"], _stringLocalizer["role_name"], viewModel.role_name));
            }
            var entities = viewModel.detailList.Select(t => new RolemenuEntity
            {
                id = 0,
                userrole_id = viewModel.userrole_id,
                menu_id = t.menu_id,
                authority = t.authority,
                menu_actions_authority = JsonHelper.SerializeObject(t.menu_actions_authority),
                create_time = DateTime.Now,
                last_update_time = DateTime.Now,
                tenant_id = currentUser.tenant_id
            }).ToList();

            await Rolemenus.AddRangeAsync(entities);
            var qty = await _dBContext.SaveChangesAsync();
            if (qty > 0)
            {
                return (viewModel.userrole_id, _stringLocalizer["save_success"]);
            }
            else
            {
                return (0, _stringLocalizer["save_failed"]);
            }
        }
        /// <summary>
        /// update a record
        /// </summary>
        /// <param name="viewModel">args</param>
        /// <param name="currentUser">currentUser</param>
        /// <returns></returns>
        public async Task<(bool flag, string msg)> UpdateAsync(RolemenuBothViewModel viewModel, CurrentUser currentUser)
        {
            var Rolemenus = _dBContext.GetDbSet<RolemenuEntity>();
            if (!(await Rolemenus.AnyAsync(t => t.userrole_id.Equals(viewModel.userrole_id))))
            {
                return (false, _stringLocalizer["not_exists_entity"]);
            }
            var dbEntities = await Rolemenus.AsNoTracking().Where(t => t.userrole_id == viewModel.userrole_id).ToListAsync();

            var entities = (from vm in viewModel.detailList
                            join db in dbEntities on new { id = Math.Abs(vm.id), vm.menu_id } equals new { db.id, db.menu_id } into dbJoin
                            from db in dbJoin.DefaultIfEmpty()
                            select new RolemenuEntity
                            {
                                id = vm.id,
                                userrole_id = viewModel.userrole_id,
                                menu_id = vm.menu_id,
                                authority = vm.authority,
                                menu_actions_authority = JsonHelper.SerializeObject(vm.menu_actions_authority),
                                create_time = db == null ? DateTime.Now : db.create_time,
                                last_update_time = DateTime.Now,
                                tenant_id = currentUser.tenant_id
                            }).ToList();
            
            if (entities.Any(t => t.id > 0))
            {
                Rolemenus.UpdateRange(entities.Where(t => t.id > 0).ToList());
            }
            if (entities.Any(t => t.id == 0))
            {
                Rolemenus.AddRange(entities.Where(t => t.id == 0).ToList());
            }
            if (entities.Any(t => t.id < 0))
            {
                var dels = entities.Where(t => t.id < 0).ToList();
                dels.ForEach(t => t.id *= -1);
                Rolemenus.RemoveRange(dels);
            }
            var qty = await _dBContext.SaveChangesAsync();
            if (qty > 0)
            {
                return (true, _stringLocalizer["save_success"]);
            }
            else
            {
                return (false, _stringLocalizer["save_failed"]);
            }
        }

        /// <summary>
        /// batch update current role's full menu permission tree
        /// </summary>
        /// <param name="viewModel">final permission tree</param>
        /// <param name="currentUser">currentUser</param>
        /// <returns></returns>
        public async Task<(bool flag, string msg)> BatchUpdateAsync(RolemenuBatchViewModel viewModel, CurrentUser currentUser)
        {
            if (IsInMemoryDatabase())
            {
                return await BatchUpdateCoreAsync(viewModel, currentUser);
            }

            await using var transaction = await _dBContext.GetDatabase().BeginTransactionAsync(IsolationLevel.Serializable);
            try
            {
                var result = await BatchUpdateCoreAsync(viewModel, currentUser);
                if (result.flag)
                {
                    await transaction.CommitAsync();
                }
                else
                {
                    await transaction.RollbackAsync();
                }
                return result;
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }

        private async Task<(bool flag, string msg)> BatchUpdateCoreAsync(RolemenuBatchViewModel viewModel, CurrentUser currentUser)
        {
            var Rolemenus = _dBContext.GetDbSet<RolemenuEntity>();
            var Userroles = _dBContext.GetDbSet<UserroleEntity>();
            var Menus = _dBContext.GetDbSet<MenuEntity>();

            if (viewModel.detailList == null)
            {
                return (false, "detailList is required");
            }

            if (!(await Userroles.AsNoTracking().AnyAsync(t => t.id == viewModel.userrole_id && t.tenant_id == currentUser.tenant_id)))
            {
                return (false, _stringLocalizer["not_exists_entity"]);
            }

            var details = viewModel.detailList;
            if (details.Any(t => t.menu_id <= 0))
            {
                return (false, "invalid menu_id");
            }
            if (details.SelectMany(t => NormalizeActionAuthority(t.menu_actions_authority))
                .Any(t => t.Length > MaxMenuActionAuthorityLength))
            {
                return (false, $"menu_actions_authority length must be less than or equal to {MaxMenuActionAuthorityLength}");
            }

            var duplicateMenuIds = details.GroupBy(t => t.menu_id)
                .Where(t => t.Count() > 1)
                .Select(t => t.Key)
                .ToList();
            if (duplicateMenuIds.Any())
            {
                return (false, $"duplicate menu_id: {string.Join(",", duplicateMenuIds)}");
            }

            var menuIds = details.Select(t => t.menu_id).ToList();
            var validMenus = await Menus.AsNoTracking()
                .Where(t => t.tenant_id == currentUser.tenant_id && menuIds.Contains(t.id))
                .Select(t => new
                {
                    t.id,
                    t.menu_actions
                })
                .ToListAsync();
            var validMenuIds = validMenus.Select(t => t.id).ToList();
            var invalidMenuIds = menuIds.Except(validMenuIds).ToList();
            if (invalidMenuIds.Any())
            {
                return (false, $"invalid menu_id: {string.Join(",", invalidMenuIds)}");
            }
            var menuActionWhiteList = validMenus.ToDictionary(
                t => t.id,
                t => NormalizeActionAuthority(JsonHelper.DeserializeObject<List<string>>(t.menu_actions)));
            foreach (var detail in details)
            {
                var allowedActions = menuActionWhiteList[detail.menu_id];
                if (!allowedActions.Any())
                {
                    continue;
                }
                var allowedActionSet = allowedActions.ToHashSet(StringComparer.Ordinal);
                var invalidActions = NormalizeActionAuthority(detail.menu_actions_authority)
                    .Where(t => !allowedActionSet.Contains(t))
                    .ToList();
                if (invalidActions.Any())
                {
                    return (false, $"invalid menu_actions_authority: {string.Join(",", invalidActions)}");
                }
            }

            var dbEntities = await Rolemenus
                .Where(t => t.userrole_id == viewModel.userrole_id && t.tenant_id == currentUser.tenant_id)
                .ToListAsync();
            var dbEntityGroups = dbEntities.GroupBy(t => t.menu_id).ToDictionary(t => t.Key, t => t.ToList());
            var payloadMenuIds = menuIds.ToHashSet();
            var now = DateTime.Now;

            foreach (var detail in details)
            {
                var actionAuthority = SerializeActionAuthority(detail.menu_actions_authority);
                if (dbEntityGroups.TryGetValue(detail.menu_id, out var currentEntities))
                {
                    var entity = currentEntities.OrderBy(t => t.id).First();
                    if (entity.authority != 1 || entity.menu_actions_authority != actionAuthority)
                    {
                        entity.authority = 1;
                        entity.menu_actions_authority = actionAuthority;
                        entity.last_update_time = now;
                    }

                    var duplicateDbEntities = currentEntities.OrderBy(t => t.id).Skip(1).ToList();
                    if (duplicateDbEntities.Any())
                    {
                        Rolemenus.RemoveRange(duplicateDbEntities);
                    }
                }
                else
                {
                    Rolemenus.Add(new RolemenuEntity
                    {
                        id = 0,
                        userrole_id = viewModel.userrole_id,
                        menu_id = detail.menu_id,
                        authority = 1,
                        menu_actions_authority = actionAuthority,
                        create_time = now,
                        last_update_time = now,
                        tenant_id = currentUser.tenant_id
                    });
                }
            }

            var deleteEntities = dbEntities.Where(t => !payloadMenuIds.Contains(t.menu_id)).ToList();
            if (deleteEntities.Any())
            {
                Rolemenus.RemoveRange(deleteEntities);
            }

            await _dBContext.SaveChangesAsync();
            return (true, _stringLocalizer["save_success"]);
        }

        private static string SerializeActionAuthority(List<string> menuActionsAuthority)
        {
            var normalizedActions = NormalizeActionAuthority(menuActionsAuthority);
            return JsonHelper.SerializeObject(normalizedActions);
        }

        private static List<string> NormalizeActionAuthority(List<string> menuActionsAuthority)
        {
            return (menuActionsAuthority ?? new List<string>())
                .Select(t => t?.Trim())
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(t => t, StringComparer.Ordinal)
                .ToList();
        }

        private bool IsInMemoryDatabase()
        {
            return string.Equals(_dBContext.GetDatabase().ProviderName, "Microsoft.EntityFrameworkCore.InMemory", StringComparison.Ordinal);
        }

        /// <summary>
        /// delete a record
        /// </summary>
        /// <param name="userrole_id">userrole id</param>
        /// <returns></returns>
        public async Task<(bool flag, string msg)> DeleteAsync(int userrole_id)
        {
            var qty = await _dBContext.GetDbSet<RolemenuEntity>().Where(t => t.userrole_id.Equals(userrole_id)).ExecuteDeleteAsync();
            if (qty > 0)
            {
                return (true, _stringLocalizer["delete_success"]);
            }
            else
            {
                return (false, _stringLocalizer["delete_failed"]);
            }
        }
        #endregion
    }
 }
 
