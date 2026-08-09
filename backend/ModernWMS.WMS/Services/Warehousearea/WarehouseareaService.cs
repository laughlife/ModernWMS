/*
 * date：2022-12-21
 * developer：NoNo
 */
using Mapster;
using Microsoft.EntityFrameworkCore;
using ModernWMS.Core.DBContext;
using ModernWMS.Core.Services;
using ModernWMS.WMS.Entities.Models;
using ModernWMS.WMS.Entities.ViewModels;
using ModernWMS.WMS.IServices;
using ModernWMS.Core.Models;
using ModernWMS.Core.JWT;
using Microsoft.Extensions.Localization;
using ModernWMS.Core.DynamicSearch;

namespace ModernWMS.WMS.Services
{
    /// <summary>
    ///  Warehousearea Service
    /// </summary>
    public class WarehouseareaService : BaseService<WarehouseareaEntity>, IWarehouseareaService
    {
        #region Args
        /// <summary>
        /// The DBContext
        /// </summary>
        private readonly SqlDBContext _dBContext;

        /// <summary>
        /// Ruoyi business tables in the shared database.
        /// </summary>
        private readonly RuoyiDbContext _ruoyiDbContext;

        /// <summary>
        /// Localizer Service
        /// </summary>
        private readonly IStringLocalizer<ModernWMS.Core.MultiLanguage> _stringLocalizer;
        #endregion

        #region constructor
        /// <summary>
        ///Warehousearea  constructor
        /// </summary>
        /// <param name="dBContext">The DBContext</param>
        /// <param name="stringLocalizer">Localizer</param>
        public WarehouseareaService(
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
        public async Task<(List<WarehouseareaViewModel> data, int totals)> PageAsync(PageSearch pageSearch, CurrentUser currentUser)
        {
            QueryCollection queries = new QueryCollection();
            if (pageSearch.searchObjects.Any())
            {
                pageSearch.searchObjects.ForEach(s =>
                {
                    queries.Add(s);
                });
            }
            var DbSet = _dBContext.GetDbSet<WarehouseareaEntity>();
            var warehouse_DBSet = _dBContext.GetDbSet<WarehouseEntity>();

            var query = from wa in DbSet.AsNoTracking()
                        join w in warehouse_DBSet.AsNoTracking() on wa.warehouse_id equals w.id
                        select new WarehouseareaViewModel
                        {
                            id = wa.id,
                            warehouse_id = wa.warehouse_id,
                            warehouse_name = w.warehouse_name,
                            area_name = wa.area_name,
                            parent_id = wa.parent_id,
                            create_time = wa.create_time,
                            last_update_time = wa.last_update_time,
                            is_valid = wa.is_valid,
                            tenant_id = wa.tenant_id,
                            area_property = wa.area_property,
                            sort = wa.sort,
                        };
            if (pageSearch.sqlTitle == "select")
            {
                query = query.Where(t => t.is_valid == true);
            }
            query = query.Where(t => t.tenant_id.Equals(currentUser.tenant_id)).Where(queries.AsExpression<WarehouseareaViewModel>());
            int totals = await query.CountAsync();
            var list = await query.OrderBy(t => t.sort).ThenBy(t => t.id)
                       .Skip((pageSearch.pageIndex - 1) * pageSearch.pageSize)
                       .Take(pageSearch.pageSize)
                       .ToListAsync();
            await PopulateOperatorGroupBindingsAsync(list);
            return (list, totals);
        }
        /// <summary>
        /// get warehouseareas of the warehouse by warehouse_id
        /// </summary>
        /// <param name="warehouse_id">warehouse's id</param>
        /// <param name="currentUser">current user</param>
        /// <returns></returns>
        public async Task<List<FormSelectItem>> GetWarehouseareaByWarehouse_id(int warehouse_id, CurrentUser currentUser)
        {
            var res = new List<FormSelectItem>();
            var DbSet = _dBContext.GetDbSet<WarehouseareaEntity>();
            res = await (from wa in DbSet.AsNoTracking()
                         where wa.is_valid == true && wa.tenant_id == currentUser.tenant_id && wa.warehouse_id == warehouse_id
                         orderby wa.sort, wa.id
                         select new FormSelectItem
                         {
                             code = "warehousearea",
                             comments = "warehouseareas of the warehouse",
                             name = wa.area_name,
                             value = wa.id.ToString(),
                         }).ToListAsync();
            return res;
        }

        /// <summary>
        /// Get all records
        /// </summary>
        /// <returns></returns>
        public async Task<List<WarehouseareaViewModel>> GetAllAsync(int warehouse_id, CurrentUser currentUser)
        {
            var DbSet = _dBContext.GetDbSet<WarehouseareaEntity>().AsNoTracking();
            if (warehouse_id > 0)
            {
                DbSet = DbSet.Where(t=>t.warehouse_id == warehouse_id);
            }
            var data = await DbSet.Where(t =>t.is_valid == true && t.tenant_id.Equals(currentUser.tenant_id))
                .OrderBy(t => t.sort)
                .ThenBy(t => t.id)
                .ToListAsync();
            var result = data.Adapt<List<WarehouseareaViewModel>>();
            await PopulateOperatorGroupBindingsAsync(result);
            return result;
        }

        /// <summary>
        /// Get a record by id
        /// </summary>
        /// <returns></returns>
        public async Task<WarehouseareaViewModel> GetAsync(int id, CurrentUser currentUser)
        {
            var DbSet = _dBContext.GetDbSet<WarehouseareaEntity>();
            var entity = await DbSet.AsNoTracking()
                .FirstOrDefaultAsync(t => t.id == id && t.tenant_id == currentUser.tenant_id);
            if (entity == null)
            {
                return null;
            }
            var result = entity.Adapt<WarehouseareaViewModel>();
            await PopulateOperatorGroupBindingsAsync(new List<WarehouseareaViewModel> { result });
            return result;
        }
        /// <summary>
        /// add a new record
        /// </summary>
        /// <param name="viewModel">viewmodel</param>
        /// <param name="currentUser">current user</param>
        /// <returns></returns>
        public async Task<(int id, string msg)> AddAsync(WarehouseareaViewModel viewModel, CurrentUser currentUser)
        {
            var DbSet = _dBContext.GetDbSet<WarehouseareaEntity>();
            var operatorGroupIds = NormalizeOperatorGroupIds(viewModel.operator_group_ids);
            if (!await AreValidOperatorGroupsAsync(operatorGroupIds))
            {
                return (0, _stringLocalizer["invalid_operator_group"]);
            }
            if (await HasOperatorGroupBindingConflictAsync(operatorGroupIds, currentUser.tenant_id, null))
            {
                return (0, _stringLocalizer["operator_group_already_bound"]);
            }
            if (!await _dBContext.GetDbSet<WarehouseEntity>()
                .AnyAsync(t => t.id == viewModel.warehouse_id && t.tenant_id == currentUser.tenant_id))
            {
                return (0, _stringLocalizer["not_exists_entity"]);
            }
            if (await DbSet.AnyAsync(t => t.warehouse_id == viewModel.warehouse_id && t.area_name == viewModel.area_name && t.tenant_id == currentUser.tenant_id))
            {
                return (0, string.Format(_stringLocalizer["exists_entity"], _stringLocalizer["area_name"], viewModel.area_name));
            }
            var entity = viewModel.Adapt<WarehouseareaEntity>();
            entity.id = 0;
            entity.create_time = DateTime.Now;
            entity.last_update_time = DateTime.Now;
            entity.tenant_id = currentUser.tenant_id;
            await using var transaction = await _dBContext.Database.BeginTransactionAsync();
            await DbSet.AddAsync(entity);
            await _dBContext.SaveChangesAsync();
            if (entity.id > 0)
            {
                await AddOperatorGroupBindingsAsync(
                    entity.id,
                    currentUser.tenant_id,
                    operatorGroupIds,
                    currentUser.user_name);
                await _dBContext.SaveChangesAsync();
                await transaction.CommitAsync();
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
        public async Task<(bool flag, string msg)> UpdateAsync(WarehouseareaViewModel viewModel, CurrentUser currentUser)
        {
            var DbSet = _dBContext.GetDbSet<WarehouseareaEntity>();
            var operatorGroupIds = NormalizeOperatorGroupIds(viewModel.operator_group_ids);
            if (!await AreValidOperatorGroupsAsync(operatorGroupIds))
            {
                return (false, _stringLocalizer["invalid_operator_group"]);
            }
            if (await HasOperatorGroupBindingConflictAsync(operatorGroupIds, currentUser.tenant_id, viewModel.id))
            {
                return (false, _stringLocalizer["operator_group_already_bound"]);
            }
            if (!await _dBContext.GetDbSet<WarehouseEntity>()
                .AnyAsync(t => t.id == viewModel.warehouse_id && t.tenant_id == currentUser.tenant_id))
            {
                return (false, _stringLocalizer["not_exists_entity"]);
            }
            var entity = await DbSet.FirstOrDefaultAsync(t => t.id == viewModel.id
                && t.tenant_id == currentUser.tenant_id);
            if (await DbSet.AnyAsync(t => t.id != viewModel.id && t.warehouse_id == viewModel.warehouse_id && t.area_name == viewModel.area_name && t.tenant_id == currentUser.tenant_id))
            {
                return (false, string.Format(_stringLocalizer["exists_entity"], _stringLocalizer["area_name"], viewModel.area_name));
            }
            if (entity == null)
            {
                return (false, _stringLocalizer["not_exists_entity"]);
            }
            entity.id = viewModel.id;
            entity.warehouse_id = viewModel.warehouse_id;
            entity.area_name = viewModel.area_name;
            entity.parent_id = viewModel.parent_id;
            entity.is_valid = viewModel.is_valid;
            entity.area_property = viewModel.area_property;
            entity.sort = viewModel.sort;
            entity.last_update_time = DateTime.Now;
            var goodslocation_DBSet = _dBContext.GetDbSet<GoodslocationEntity>();
            var gldatas = await goodslocation_DBSet
                .Where(t => t.warehouse_area_id == entity.id && t.tenant_id == currentUser.tenant_id)
                .ToListAsync();
            gldatas.ForEach(t =>
            {
                t.warehouse_area_name = entity.area_name;
                t.warehouse_area_property = entity.area_property;
                t.is_valid = entity.is_valid;
            });
            var bindingDbSet = _dBContext.GetDbSet<WarehouseareaOperatorGroupEntity>();
            var oldBindings = await bindingDbSet
                .Where(t => t.warehouse_area_id == entity.id && t.tenant_id == currentUser.tenant_id)
                .ToListAsync();
            var selectedGroupIds = operatorGroupIds.ToHashSet();
            bindingDbSet.RemoveRange(oldBindings.Where(t => !selectedGroupIds.Contains(t.dept_id)));
            var newGroupIds = operatorGroupIds
                .Except(oldBindings.Select(t => t.dept_id))
                .ToList();
            await AddOperatorGroupBindingsAsync(
                entity.id,
                currentUser.tenant_id,
                newGroupIds,
                currentUser.user_name);
            await _dBContext.SaveChangesAsync();
            return (true, _stringLocalizer["save_success"]);
        }
        /// <summary>
        /// delete a record
        /// </summary>
        /// <param name="id">id</param>
        /// <returns></returns>
        public async Task<(bool flag, string msg)> DeleteAsync(int id, CurrentUser currentUser)
        {
            if (await _dBContext.GetDbSet<GoodslocationEntity>()
                .AnyAsync(t => t.warehouse_area_id == id && t.tenant_id == currentUser.tenant_id))
            {
                return (false, _stringLocalizer["exist_location_not_delete"]);
            }
            var entity = await _dBContext.GetDbSet<WarehouseareaEntity>()
                .FirstOrDefaultAsync(t => t.id == id && t.tenant_id == currentUser.tenant_id);
            if (entity != null)
            {
                var bindingDbSet = _dBContext.GetDbSet<WarehouseareaOperatorGroupEntity>();
                var bindings = await bindingDbSet
                    .Where(t => t.warehouse_area_id == id && t.tenant_id == currentUser.tenant_id)
                    .ToListAsync();
                bindingDbSet.RemoveRange(bindings);
                _dBContext.GetDbSet<WarehouseareaEntity>().Remove(entity);
                await _dBContext.SaveChangesAsync();
                return (true, _stringLocalizer["delete_success"]);
            }
            else
            {
                return (false, _stringLocalizer["delete_failed"]);
            }
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

        private async Task<bool> HasOperatorGroupBindingConflictAsync(
            IReadOnlyCollection<long> operatorGroupIds,
            long tenantId,
            int? currentAreaId)
        {
            if (operatorGroupIds.Count == 0)
            {
                return false;
            }

            return await _dBContext.GetDbSet<WarehouseareaOperatorGroupEntity>()
                .AsNoTracking()
                .AnyAsync(t => t.tenant_id == tenantId
                    && operatorGroupIds.Contains(t.dept_id)
                    && (!currentAreaId.HasValue || t.warehouse_area_id != currentAreaId.Value));
        }

        /// <summary>
        /// Add normalized bindings to the current WMS unit of work.
        /// </summary>
        private async Task AddOperatorGroupBindingsAsync(
            int warehouseAreaId,
            long tenantId,
            IReadOnlyCollection<long> operatorGroupIds,
            string creator)
        {
            if (operatorGroupIds.Count == 0)
            {
                return;
            }

            var now = DateTime.Now;
            var bindings = operatorGroupIds.Select(deptId => new WarehouseareaOperatorGroupEntity
            {
                tenant_id = tenantId,
                warehouse_area_id = warehouseAreaId,
                dept_id = deptId,
                creator = creator,
                create_time = now
            });
            await _dBContext.GetDbSet<WarehouseareaOperatorGroupEntity>().AddRangeAsync(bindings);
        }

        /// <summary>
        /// Populate current group ids and names for list and edit views.
        /// </summary>
        private async Task PopulateOperatorGroupBindingsAsync(IEnumerable<WarehouseareaViewModel> warehouseAreas)
        {
            var areaList = warehouseAreas.ToList();
            if (areaList.Count == 0)
            {
                return;
            }

            var areaIds = areaList.Select(t => t.id).Distinct().ToList();
            var tenantIds = areaList.Select(t => t.tenant_id).Distinct().ToList();
            var bindings = await _dBContext.GetDbSet<WarehouseareaOperatorGroupEntity>()
                .AsNoTracking()
                .Where(t => areaIds.Contains(t.warehouse_area_id)
                    && tenantIds.Contains(t.tenant_id))
                .ToListAsync();
            if (bindings.Count == 0)
            {
                return;
            }

            var deptIds = bindings.Select(t => t.dept_id).Distinct().ToList();
            var departments = await _ruoyiDbContext.SystemDepts
                .AsNoTracking()
                .Where(t => deptIds.Contains(t.id) && !t.deleted)
                .Select(t => new { t.id, t.name, t.sort })
                .ToListAsync();
            var departmentMap = departments.ToDictionary(t => t.id);

            areaList.ForEach(area =>
            {
                var items = bindings
                    .Where(t => t.tenant_id == area.tenant_id && t.warehouse_area_id == area.id)
                    .Where(t => departmentMap.ContainsKey(t.dept_id))
                    .OrderBy(t => departmentMap[t.dept_id].sort)
                    .ThenBy(t => t.dept_id)
                    .ToList();
                area.operator_group_ids = items.Select(t => t.dept_id).ToList();
                area.operator_group_names = items
                    .Select(t => departmentMap[t.dept_id].name ?? string.Empty)
                    .ToList();
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

