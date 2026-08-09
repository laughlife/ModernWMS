/*
 * date：2022-12-21
 * developer：NoNo
 */
using Mapster;
using Microsoft.EntityFrameworkCore;
using ModernWMS.Core.DBContext;
using ModernWMS.Core.DBContext.Entities;
using ModernWMS.Core.Services;
using ModernWMS.WMS.Entities.Models;
using ModernWMS.WMS.Entities.ViewModels;
using ModernWMS.WMS.IServices;
using ModernWMS.Core.Models;
using ModernWMS.Core.JWT;
using Microsoft.Extensions.Localization;
using ModernWMS.Core.DynamicSearch;
using System.Text;

namespace ModernWMS.WMS.Services
{
    /// <summary>
    ///  Warehouse Service
    /// </summary>
    public class WarehouseService : BaseService<WarehouseEntity>, IWarehouseService
    {
        #region Args
        /// <summary>
        /// The DBContext
        /// </summary>
        private readonly SqlDBContext _dBContext;

        /// <summary>
        /// ERP public database context.
        /// </summary>
        private readonly RuoyiDbContext _ruoyiDbContext;

        /// <summary>
        /// Localizer Service
        /// </summary>
        private readonly IStringLocalizer<ModernWMS.Core.MultiLanguage> _stringLocalizer;
        #endregion

        #region constructor
        /// <summary>
        ///Warehouse  constructor
        /// </summary>
        /// <param name="dBContext">The DBContext</param>
        /// <param name="stringLocalizer">Localizer</param>
        public WarehouseService(
            SqlDBContext dBContext
          , RuoyiDbContext ruoyiDbContext
          , IStringLocalizer<ModernWMS.Core.MultiLanguage> stringLocalizer
            )
        {
            this._dBContext = dBContext;
            this._ruoyiDbContext = ruoyiDbContext;
            this._stringLocalizer = stringLocalizer;
        }
        #endregion

        #region Api
        /// <summary>
        /// get select items
        /// </summary>
        /// <param name="currentUser">current user</param>
        /// <returns></returns>
        public async Task<List<FormSelectItem>> GetSelectItemsAsnyc(CurrentUser currentUser)
        {
            var res = new List<FormSelectItem>();
            var DBSet = _dBContext.GetDbSet<WarehouseEntity>();
            res.AddRange(await (from db in DBSet.AsNoTracking()
                                where db.is_valid == true && db.tenant_id == currentUser.tenant_id
                                select new FormSelectItem
                                {
                                    code = "warehouse_name",
                                    name = db.warehouse_name,
                                    value = db.id.ToString(),
                                    comments = "warehouse datas",
                                }).ToListAsync());
            return res;
        }

        /// <summary>
        /// Get valid ERP domestic warehouses for optional binding.
        /// </summary>
        public async Task<List<ErpWarehouseOptionViewModel>> GetErpWarehouseOptionsAsync()
        {
            return await _ruoyiDbContext.Warehouses
                .AsNoTracking()
                .Where(t => !t.deleted && t.attr == "国内仓库")
                .OrderBy(t => t.name)
                .ThenBy(t => t.id)
                .Select(t => new ErpWarehouseOptionViewModel
                {
                    id = t.id,
                    name = t.name ?? string.Empty
                })
                .ToListAsync();
        }

        /// <summary>
        /// Get enabled ERP operator groups ordered for the binding dropdown.
        /// </summary>
        public async Task<List<OperatorGroupOptionViewModel>> GetOperatorGroupOptionsAsync()
        {
            return await _ruoyiDbContext.SystemDepts
                .AsNoTracking()
                .Where(t => !t.deleted && t.status == 0 && t.dept == "operator")
                .OrderBy(t => t.sort)
                .ThenBy(t => t.id)
                .Select(t => new OperatorGroupOptionViewModel
                {
                    id = t.id,
                    name = t.name ?? string.Empty,
                    sort = t.sort
                })
                .ToListAsync();
        }

        /// <summary>
        /// page search
        /// </summary>
        /// <param name="pageSearch">args</param>
        /// <param name="currentUser">currentUser</param>
        /// <returns></returns>
        public async Task<(List<WarehouseViewModel> data, int totals)> PageAsync(PageSearch pageSearch, CurrentUser currentUser)
        {
            QueryCollection queries = new QueryCollection();
            if (pageSearch.searchObjects.Any())
            {
                pageSearch.searchObjects.ForEach(s =>
                {
                    queries.Add(s);
                });
            }
            var DbSet = _dBContext.GetDbSet<WarehouseEntity>();
            var query = DbSet.AsNoTracking()
                .Where(t => t.tenant_id.Equals(currentUser.tenant_id))
                .Where(queries.AsExpression<WarehouseEntity>());
            if (pageSearch.sqlTitle == "select")
            {
                query = query.Where(t => t.is_valid == true);
            }
            int totals = await query.CountAsync();
            var list = await query.OrderByDescending(t => t.create_time)
                       .Skip((pageSearch.pageIndex - 1) * pageSearch.pageSize)
                       .Take(pageSearch.pageSize)
                       .ToListAsync();
            var result = list.Adapt<List<WarehouseViewModel>>();
            await PopulateErpWarehouseNamesAsync(result);
            await PopulateOperatorGroupBindingsAsync(result);
            return (result, totals);
        }

        /// <summary>
        /// Get all records
        /// </summary>
        /// <returns></returns>
        public async Task<List<WarehouseViewModel>> GetAllAsync(CurrentUser currentUser)
        {
            var DbSet = _dBContext.GetDbSet<WarehouseEntity>();
            var data = await DbSet.AsNoTracking().Where(t => t.tenant_id.Equals(currentUser.tenant_id)).ToListAsync();
            var result = data.Adapt<List<WarehouseViewModel>>();
            await PopulateErpWarehouseNamesAsync(result);
            await PopulateOperatorGroupBindingsAsync(result);
            return result;
        }

        /// <summary>
        /// Get a record by id
        /// </summary>
        /// <returns></returns>
        public async Task<WarehouseViewModel> GetAsync(int id, CurrentUser currentUser)
        {
            var DbSet = _dBContext.GetDbSet<WarehouseEntity>();
            var entity = await DbSet.AsNoTracking()
                .FirstOrDefaultAsync(t => t.id == id && t.tenant_id == currentUser.tenant_id);
            if (entity == null)
            {
                return null;
            }
            var result = entity.Adapt<WarehouseViewModel>();
            await PopulateErpWarehouseNamesAsync(new List<WarehouseViewModel> { result });
            await PopulateOperatorGroupBindingsAsync(new List<WarehouseViewModel> { result });
            return result;
        }
        /// <summary>
        /// add a new record
        /// </summary>
        /// <param name="viewModel">viewmodel</param>
        /// <param name="currentUser">current user</param>
        /// <returns></returns>
        public async Task<(int id, string msg)> AddAsync(WarehouseViewModel viewModel, CurrentUser currentUser)
        {
            var DbSet = _dBContext.GetDbSet<WarehouseEntity>();
            if (!await IsValidErpWarehouseAsync(viewModel.erp_warehouse_id))
            {
                return (0, _stringLocalizer["invalid_erp_warehouse"]);
            }
            var operatorGroupIds = NormalizeOperatorGroupIds(viewModel.operator_group_ids);
            if (!await AreValidOperatorGroupsAsync(operatorGroupIds))
            {
                return (0, _stringLocalizer["invalid_operator_group"]);
            }
            if (await DbSet.AnyAsync(t => t.warehouse_name == viewModel.warehouse_name && t.tenant_id == currentUser.tenant_id))
            {
               return(0,  string.Format(_stringLocalizer["exists_entity"], _stringLocalizer["warehouse_name"], viewModel.warehouse_name));
            }
            var entity = viewModel.Adapt<WarehouseEntity>();
            entity.id = 0;
            entity.create_time = DateTime.Now;
            entity.creator = currentUser.user_name;
            entity.last_update_time = DateTime.Now;
            entity.tenant_id = currentUser.tenant_id;
            await DbSet.AddAsync(entity);
            await _dBContext.SaveChangesAsync();
            if (entity.id > 0)
            {
                await ReplaceOperatorGroupBindingsAsync(
                    entity.id,
                    currentUser.tenant_id,
                    operatorGroupIds,
                    currentUser.user_name);
                return (entity.id, _stringLocalizer["save_success"]);
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
        public async Task<(bool flag, string msg)> UpdateAsync(WarehouseViewModel viewModel, CurrentUser currentUser)
        {
            var DbSet = _dBContext.GetDbSet<WarehouseEntity>();
            if (!await IsValidErpWarehouseAsync(viewModel.erp_warehouse_id))
            {
                return (false, _stringLocalizer["invalid_erp_warehouse"]);
            }
            var operatorGroupIds = NormalizeOperatorGroupIds(viewModel.operator_group_ids);
            if (!await AreValidOperatorGroupsAsync(operatorGroupIds))
            {
                return (false, _stringLocalizer["invalid_operator_group"]);
            }
            if (await DbSet.AnyAsync(t => t.id != viewModel.id && t.warehouse_name == viewModel.warehouse_name && t.tenant_id == currentUser.tenant_id))
            {
               return(false,  string.Format(_stringLocalizer["exists_entity"], _stringLocalizer["warehouse_name"], viewModel.warehouse_name));
            }
            var entity = await DbSet.FirstOrDefaultAsync(t => t.id.Equals(viewModel.id)
                && t.tenant_id == currentUser.tenant_id);
            if (entity == null)
            {
                return (false, _stringLocalizer["not_exists_entity"]);
            }
            entity.id = viewModel.id;
            entity.warehouse_name = viewModel.warehouse_name;
            entity.erp_warehouse_id = viewModel.erp_warehouse_id;
            entity.city = viewModel.city;
            entity.address = viewModel.address;
            entity.email = viewModel.email;
            entity.manager = viewModel.manager;
            entity.contact_tel = viewModel.contact_tel;
            entity.is_valid = viewModel.is_valid;
            entity.last_update_time = DateTime.Now;
            var warehousearea_DBSet = _dBContext.GetDbSet<WarehouseareaEntity>();
            var wadatas = await warehousearea_DBSet
                .Where(t => t.warehouse_id == entity.id && t.tenant_id == currentUser.tenant_id)
                .ToListAsync();
            wadatas.ForEach(t =>
            {
                t.is_valid = entity.is_valid;
            });
            var goodslocation_DBSet = _dBContext.GetDbSet<GoodslocationEntity>();
            var gldatas = await goodslocation_DBSet
                .Where(t => t.warehouse_id == entity.id && t.tenant_id == currentUser.tenant_id)
                .ToListAsync();
            gldatas.ForEach(t =>
            {
                t.warehouse_name = entity.warehouse_name;
                t.is_valid = entity.is_valid;
            });
            await _dBContext.SaveChangesAsync();
            await ReplaceOperatorGroupBindingsAsync(
                entity.id,
                currentUser.tenant_id,
                operatorGroupIds,
                currentUser.user_name);
            return (true, _stringLocalizer["save_success"]);
        }
        /// <summary>
        /// delete a record
        /// </summary>
        /// <param name="id">id</param>
        /// <returns></returns>
        public async Task<(bool flag, string msg)> DeleteAsync(int id, CurrentUser currentUser)
        {
            if (await _dBContext.GetDbSet<WarehouseareaEntity>()
                .AnyAsync(t => t.warehouse_id == id && t.tenant_id == currentUser.tenant_id))
            {
                return (false, _stringLocalizer["exist_warehousearea_not_delete"]);
            }
            if (await _dBContext.GetDbSet<GoodslocationEntity>()
                .AnyAsync(t => t.warehouse_id == id && t.tenant_id == currentUser.tenant_id))
            {
                return (false, _stringLocalizer["exist_warehousearea_not_delete"]);
            }
            var qty = await _dBContext.GetDbSet<WarehouseEntity>()
                .Where(t => t.id == id && t.tenant_id == currentUser.tenant_id)
                .ExecuteDeleteAsync();
            if (qty > 0)
            {
                await _ruoyiDbContext.WarehouseOperatorGroups
                    .Where(t => t.warehouse_id == id && t.tenant_id == currentUser.tenant_id)
                    .ExecuteDeleteAsync();
                return (true, _stringLocalizer["delete_success"]);
            }
            else
            {
                return (false, _stringLocalizer["delete_failed"]);
            }
        }

        /// <summary>
        /// import warehouses by excel
        /// </summary>
        /// <param name="datas">excel datas</param>
        /// <param name="currentUser">current user</param>
        /// <returns></returns>
        public async Task<(bool flag, string msg)> ExcelAsync(List<WarehouseExcelImportViewModel> datas, CurrentUser currentUser)
        {
            StringBuilder sb = new StringBuilder();
            var DbSet = _dBContext.GetDbSet<WarehouseEntity>();
            var warehouse_name_repeat_excel = datas.GroupBy(t => t.warehouse_name).Select(t => new { warehouse_name = t.Key, cnt = t.Count() }).Where(t => t.cnt > 1).ToList();
            foreach (var repeat in warehouse_name_repeat_excel)
            {
                sb.AppendLine(string.Format(_stringLocalizer["exists_entity"], _stringLocalizer["warehouse_name"], repeat.warehouse_name));
            }
            if (warehouse_name_repeat_excel.Count > 0)
            {
                return (false, sb.ToString());
            }

            var warehouse_name_repeat_exists = await DbSet.Where(t=>t.tenant_id == currentUser.tenant_id).Where(t => datas.Select(t => t.warehouse_name).ToList().Contains(t.warehouse_name)).Select(t => t.warehouse_name).ToListAsync();
            foreach (var repeat in warehouse_name_repeat_exists)
            {
                sb.AppendLine(string.Format(_stringLocalizer["exists_entity"], _stringLocalizer["warehouse_name"], repeat));
            }
            if (warehouse_name_repeat_exists.Count > 0)
            {
                return (false, sb.ToString());
            }

            var entities = datas.Adapt<List<WarehouseEntity>>();
            entities.ForEach(t =>
            {
                t.creator = currentUser.user_name;
                t.tenant_id = currentUser.tenant_id;
                t.create_time = DateTime.Now;
                t.last_update_time = DateTime.Now;
                t.is_valid = true;
            });
            await DbSet.AddRangeAsync(entities);
            var res = await _dBContext.SaveChangesAsync();
            if (res > 0)
            {
                return (true, _stringLocalizer["save_success"]);
            }
            return (false, _stringLocalizer["save_failed"]);
        }

        /// <summary>
        /// Validate a logical ERP warehouse foreign key. Null means unbound.
        /// </summary>
        private async Task<bool> IsValidErpWarehouseAsync(long? erpWarehouseId)
        {
            if (!erpWarehouseId.HasValue)
            {
                return true;
            }

            return await _ruoyiDbContext.Warehouses
                .AsNoTracking()
                .AnyAsync(t => t.id == erpWarehouseId.Value
                    && !t.deleted
                    && t.attr == "国内仓库");
        }

        /// <summary>
        /// Resolve the current ERP warehouse names without persisting a stale name copy.
        /// </summary>
        private async Task PopulateErpWarehouseNamesAsync(IEnumerable<WarehouseViewModel> warehouses)
        {
            var warehouseList = warehouses.ToList();
            var erpWarehouseIds = warehouseList
                .Where(t => t.erp_warehouse_id.HasValue)
                .Select(t => t.erp_warehouse_id!.Value)
                .Distinct()
                .ToList();

            if (erpWarehouseIds.Count == 0)
            {
                return;
            }

            var names = await _ruoyiDbContext.Warehouses
                .AsNoTracking()
                .Where(t => erpWarehouseIds.Contains(t.id) && !t.deleted)
                .Select(t => new { t.id, t.name })
                .ToDictionaryAsync(t => t.id, t => t.name ?? string.Empty);

            warehouseList.ForEach(t =>
            {
                if (t.erp_warehouse_id.HasValue
                    && names.TryGetValue(t.erp_warehouse_id.Value, out var name))
                {
                    t.erp_warehouse_name = name;
                }
            });
        }

        /// <summary>
        /// Validate that every selected id is an enabled ERP operator group.
        /// </summary>
        private async Task<bool> AreValidOperatorGroupsAsync(IReadOnlyCollection<long> operatorGroupIds)
        {
            if (operatorGroupIds.Count == 0)
            {
                return true;
            }

            var validCount = await _ruoyiDbContext.SystemDepts
                .AsNoTracking()
                .CountAsync(t => operatorGroupIds.Contains(t.id)
                    && !t.deleted
                    && t.status == 0
                    && t.dept == "operator");
            return validCount == operatorGroupIds.Count;
        }

        /// <summary>
        /// Replace all bindings for one warehouse. An empty list means unbind all.
        /// </summary>
        private async Task ReplaceOperatorGroupBindingsAsync(
            int warehouseId,
            long tenantId,
            IReadOnlyCollection<long> operatorGroupIds,
            string creator)
        {
            await using var transaction = await _ruoyiDbContext.Database.BeginTransactionAsync();
            await _ruoyiDbContext.WarehouseOperatorGroups
                .Where(t => t.warehouse_id == warehouseId && t.tenant_id == tenantId)
                .ExecuteDeleteAsync();

            if (operatorGroupIds.Count > 0)
            {
                var now = DateTime.Now;
                var bindings = operatorGroupIds.Select(deptId => new ErpWarehouseOperatorGroupEntity
                {
                    tenant_id = tenantId,
                    warehouse_id = warehouseId,
                    dept_id = deptId,
                    creator = creator,
                    create_time = now
                });
                await _ruoyiDbContext.WarehouseOperatorGroups.AddRangeAsync(bindings);
                await _ruoyiDbContext.SaveChangesAsync();
            }

            await transaction.CommitAsync();
        }

        /// <summary>
        /// Populate current group ids and names for list and edit views.
        /// </summary>
        private async Task PopulateOperatorGroupBindingsAsync(IEnumerable<WarehouseViewModel> warehouses)
        {
            var warehouseList = warehouses.ToList();
            if (warehouseList.Count == 0)
            {
                return;
            }

            var warehouseIds = warehouseList.Select(t => t.id).Distinct().ToList();
            var tenantIds = warehouseList.Select(t => t.tenant_id).Distinct().ToList();
            var bindings = await (
                from binding in _ruoyiDbContext.WarehouseOperatorGroups.AsNoTracking()
                join dept in _ruoyiDbContext.SystemDepts.AsNoTracking().Where(t => !t.deleted)
                    on binding.dept_id equals dept.id
                where warehouseIds.Contains(binding.warehouse_id)
                    && tenantIds.Contains(binding.tenant_id)
                orderby dept.sort, dept.id
                select new
                {
                    binding.tenant_id,
                    binding.warehouse_id,
                    binding.dept_id,
                    name = dept.name ?? string.Empty
                }).ToListAsync();

            var bindingMap = bindings
                .GroupBy(t => (t.tenant_id, t.warehouse_id))
                .ToDictionary(t => t.Key, t => t.ToList());
            warehouseList.ForEach(warehouse =>
            {
                if (!bindingMap.TryGetValue((warehouse.tenant_id, warehouse.id), out var items))
                {
                    return;
                }

                warehouse.operator_group_ids = items.Select(t => t.dept_id).ToList();
                warehouse.operator_group_names = items.Select(t => t.name).ToList();
            });
        }

        private static List<long> NormalizeOperatorGroupIds(IEnumerable<long>? operatorGroupIds)
        {
            return operatorGroupIds?
                .Where(t => t > 0)
                .Distinct()
                .ToList() ?? new List<long>();
        }
        #endregion
    }
}

