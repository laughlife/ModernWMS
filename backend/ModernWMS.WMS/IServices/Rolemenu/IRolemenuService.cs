using ModernWMS.Core.JWT;
using ModernWMS.Core.Services;
using ModernWMS.WMS.Entities.Models;
using ModernWMS.WMS.Entities.ViewModels;

namespace ModernWMS.WMS.IServices
{
    /// <summary>
    /// Interface of RolemenuService
    /// </summary>
    public interface IRolemenuService : IBaseService<RolemenuEntity>
    {
        #region Api
        /// <summary>
        /// Get all records
        /// </summary>
        /// <param name="currentUser">currentUser</param>
        /// <returns></returns>
        Task<List<RolemenuListViewModel>> GetAllAsync(CurrentUser currentUser);

        /// <summary>
        /// Get a record by id
        /// </summary>
        /// <param name="userrole_id">userrole id</param>
        /// <returns></returns>
        Task<RolemenuBothViewModel> GetAsync(int userrole_id);

        /// <summary>
        /// add a new record
        /// </summary>
        /// <param name="viewModel">args</param>
        /// <param name="currentUser">currentUser</param>
        /// <returns></returns>
        Task<(int id, string msg)> AddAsync(RolemenuBothViewModel viewModel, CurrentUser currentUser);

        /// <summary>
        /// Get all menus
        /// </summary>
        /// <param name="currentUser">currentUser</param>
        /// <returns></returns>
        Task<List<MenuViewModel>> GetAllMenusAsync(CurrentUser currentUser);

        /// <summary>
        /// Get menu's authority by user role id
        /// </summary>
        /// <param name="userrole_id">user role id</param>
        /// <param name="currentUser">currentUser</param>
        /// <returns></returns>
        Task<List<MenuViewModel>> GetMenusByRoleId(int userrole_id, CurrentUser currentUser);

        /// <summary>
        /// update a record
        /// </summary>
        /// <param name="viewModel">args</param>
        /// <param name="currentUser">currentUser</param>
        /// <returns></returns>
        Task<(bool flag, string msg)> UpdateAsync(RolemenuBothViewModel viewModel, CurrentUser currentUser);

        /// <summary>
        /// batch update current role's full menu permission tree
        /// </summary>
        /// <param name="viewModel">final permission tree</param>
        /// <param name="currentUser">currentUser</param>
        /// <returns></returns>
        Task<(bool flag, string msg)> BatchUpdateAsync(RolemenuBatchViewModel viewModel, CurrentUser currentUser);

        /// <summary>Get the ERP warehouse IDs explicitly bound to a role.</summary>
        Task<List<long>> GetWarehouseIdsAsync(int userrole_id, CurrentUser currentUser);

        /// <summary>Atomically replace all ERP warehouse bindings for a role.</summary>
        Task<(bool flag, string msg)> ReplaceWarehousesAsync(RoleWarehouseBindingViewModel viewModel, CurrentUser currentUser);

        /// <summary>
        /// delete a record
        /// </summary>
        /// <param name="userrole_id">userrole id</param>
        /// <param name="currentUser">currentUser</param>
        /// <returns></returns>
        Task<(bool flag, string msg)> DeleteAsync(int userrole_id, CurrentUser currentUser);
        #endregion
    }
}
