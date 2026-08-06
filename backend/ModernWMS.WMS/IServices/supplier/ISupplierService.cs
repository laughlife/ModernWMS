using ModernWMS.Core.JWT;
using ModernWMS.Core.Models;
using ModernWMS.Core.Services;
using ModernWMS.WMS.Entities.Models;
using ModernWMS.WMS.Entities.ViewModels;

namespace ModernWMS.WMS.IServices
{
    /// <summary>
    /// Interface of SupplierService
    /// </summary>
    public interface ISupplierService : IBaseService<SupplierEntity>
    {
        /// <summary>
        /// page search
        /// </summary>
        Task<(List<SupplierViewModel> data, int totals)> PageAsync(PageSearch pageSearch, CurrentUser currentUser);

        /// <summary>
        /// Get all records
        /// </summary>
        Task<List<SupplierViewModel>> GetAllAsync();

        /// <summary>
        /// Get a record by id
        /// </summary>
        Task<SupplierViewModel?> GetAsync(long id);
    }
}
