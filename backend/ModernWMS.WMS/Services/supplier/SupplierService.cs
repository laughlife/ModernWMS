using Microsoft.EntityFrameworkCore;
using ModernWMS.Core.DBContext;
using ModernWMS.Core.JWT;
using ModernWMS.Core.Models;
using ModernWMS.Core.Services;
using ModernWMS.WMS.Entities.Models;
using ModernWMS.WMS.Entities.ViewModels;
using ModernWMS.WMS.IServices;

namespace ModernWMS.WMS.Services
{
    /// <summary>
    /// Supplier Service
    /// </summary>
    public class SupplierService : BaseService<SupplierEntity>, ISupplierService
    {
        private readonly RuoyiDbContext _ruoyiDbContext;

        public SupplierService(RuoyiDbContext ruoyiDbContext)
        {
            _ruoyiDbContext = ruoyiDbContext;
        }

        /// <summary>
        /// page search
        /// </summary>
        public async Task<(List<SupplierViewModel> data, int totals)> PageAsync(PageSearch pageSearch, CurrentUser currentUser)
        {
            var supplierNameKeyword = pageSearch.searchObjects
                .FirstOrDefault(t => string.Equals(t.Name, "name", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(t.Name, "supplier_name", StringComparison.OrdinalIgnoreCase))
                ?.Text
                ?.Trim();

            var query = _ruoyiDbContext.Suppliers
                .AsNoTracking()
                .Where(t => !t.deleted);

            if (!string.IsNullOrWhiteSpace(supplierNameKeyword))
            {
                query = query.Where(t => t.name != null && t.name.Contains(supplierNameKeyword));
            }

            int totals = await query.CountAsync();
            var list = await query
                .OrderByDescending(t => t.id)
                .Skip((pageSearch.pageIndex - 1) * pageSearch.pageSize)
                .Take(pageSearch.pageSize)
                .Select(t => new SupplierViewModel
                {
                    id = t.id,
                    supplier_name = t.name ?? string.Empty,
                    name = t.name ?? string.Empty,
                    linkman = t.linkman ?? string.Empty,
                    telephone_num = t.telephone_num ?? string.Empty,
                    qq = t.qq ?? string.Empty,
                    email = t.email ?? string.Empty,
                    province_name = t.province_name ?? string.Empty,
                    city_name = t.city_name ?? string.Empty,
                    address_line = t.address_line ?? string.Empty,
                    remark = t.remark ?? string.Empty
                })
                .ToListAsync();

            return (list, totals);
        }

        /// <summary>
        /// Get all records
        /// </summary>
        public async Task<List<SupplierViewModel>> GetAllAsync()
        {
            return await _ruoyiDbContext.Suppliers
                .AsNoTracking()
                .Where(t => !t.deleted)
                .OrderBy(t => t.name)
                .Select(t => new SupplierViewModel
                {
                    id = t.id,
                    supplier_name = t.name ?? string.Empty,
                    name = t.name ?? string.Empty,
                    linkman = t.linkman ?? string.Empty,
                    telephone_num = t.telephone_num ?? string.Empty,
                    qq = t.qq ?? string.Empty,
                    email = t.email ?? string.Empty,
                    province_name = t.province_name ?? string.Empty,
                    city_name = t.city_name ?? string.Empty,
                    address_line = t.address_line ?? string.Empty,
                    remark = t.remark ?? string.Empty
                })
                .ToListAsync();
        }

        /// <summary>
        /// Get a record by id
        /// </summary>
        public async Task<SupplierViewModel?> GetAsync(long id)
        {
            return await _ruoyiDbContext.Suppliers
                .AsNoTracking()
                .Where(t => !t.deleted && t.id == id)
                .Select(t => new SupplierViewModel
                {
                    id = t.id,
                    supplier_name = t.name ?? string.Empty,
                    name = t.name ?? string.Empty,
                    linkman = t.linkman ?? string.Empty,
                    telephone_num = t.telephone_num ?? string.Empty,
                    qq = t.qq ?? string.Empty,
                    email = t.email ?? string.Empty,
                    province_name = t.province_name ?? string.Empty,
                    city_name = t.city_name ?? string.Empty,
                    address_line = t.address_line ?? string.Empty,
                    remark = t.remark ?? string.Empty
                })
                .FirstOrDefaultAsync();
        }
    }
}
