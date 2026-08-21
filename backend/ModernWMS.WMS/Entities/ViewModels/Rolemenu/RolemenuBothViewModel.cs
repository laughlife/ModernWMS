/*
 * date：2022-12-20
 * developer：AMo
 */
using System.ComponentModel.DataAnnotations;

namespace ModernWMS.WMS.Entities.ViewModels
{
    /// <summary>
    /// rolemenu viewModel
    /// </summary>
    public class RolemenuBothViewModel
    {
        #region constructor
        /// <summary>
        /// constructor
        /// </summary>
        public RolemenuBothViewModel()
        {

        }
        #endregion

        #region Property

        /// <summary>
        /// userrole_id
        /// </summary>
        [Display(Name = "userrole_id")]
        [Required(ErrorMessage = "Required")]
        public int userrole_id { get; set; } = 0;

        /// <summary>
        /// role_name
        /// </summary>
        [Display(Name = "role_name")]
        [MaxLength(32, ErrorMessage = "MaxLength")]
        public string role_name { get; set; } = string.Empty;

        /// <summary>
        /// role valid
        /// </summary>
        public bool is_valid { get; set; } = true;

        /// <summary>
        /// menu details
        /// </summary>
        public List<RolemenuViewModel> detailList { get; set; } = new List<RolemenuViewModel>();
        #endregion
    }

    /// <summary>
    /// Full replacement payload for one role's ERP warehouse bindings.
    /// </summary>
    public class RoleWarehouseBindingViewModel
    {
        /// <summary>
        /// 获取或设置 userrole_id。
        /// </summary>
        [Required(ErrorMessage = "Required")]
        public int userrole_id { get; set; }

        /// <summary>
        /// 获取或设置 warehouse_ids。
        /// </summary>
        [Required(ErrorMessage = "Required")]
        public List<long> warehouse_ids { get; set; } = [];
    }
}
