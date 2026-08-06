/*
 * date：2026-08-06
 * developer：Codex
 */
using System.ComponentModel.DataAnnotations;

namespace ModernWMS.WMS.Entities.ViewModels
{
    /// <summary>
    /// role menu batch update viewModel
    /// </summary>
    public class RolemenuBatchViewModel
    {
        /// <summary>
        /// userrole_id
        /// </summary>
        [Display(Name = "userrole_id")]
        [Required(ErrorMessage = "Required")]
        public int userrole_id { get; set; } = 0;

        /// <summary>
        /// final menu permission details
        /// </summary>
        public List<RolemenuBatchDetailViewModel>? detailList { get; set; }
    }

    /// <summary>
    /// role menu batch update detail viewModel
    /// </summary>
    public class RolemenuBatchDetailViewModel
    {
        /// <summary>
        /// menu_id
        /// </summary>
        [Display(Name = "menu_id")]
        [Required(ErrorMessage = "Required")]
        public int menu_id { get; set; } = 0;

        /// <summary>
        /// actions authority
        /// </summary>
        public List<string> menu_actions_authority { get; set; } = new List<string>();
    }
}
