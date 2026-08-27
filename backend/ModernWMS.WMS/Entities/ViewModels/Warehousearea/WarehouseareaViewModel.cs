/*
 * date：2022-12-21
 * developer：NoNo
 */
using System;
using System.ComponentModel.DataAnnotations;
using ModernWMS.Core.Utility;

namespace ModernWMS.WMS.Entities.ViewModels
{
    /// <summary>
    /// warehousearea viewModel
    /// </summary>
    public class WarehouseareaViewModel
    {

        #region constructor
        /// <summary>
        /// constructor
        /// </summary>
        public WarehouseareaViewModel()
        {

        }
        #endregion
        #region Property

        /// <summary>
        /// id
        /// </summary>
        [Display(Name = "id")]
        public int id { get; set; } = 0;

        /// <summary>
        /// warehouse_id
        /// </summary>
        [Display(Name = "warehouse_id")]
        public int warehouse_id { get; set; } = 0;


        /// <summary>
        /// warehouse_name
        /// </summary>
        [Display(Name = "warehouse_name")]
        [MaxLength(32, ErrorMessage = "MaxLength")]
        [Required(ErrorMessage = "Required")]
        public string warehouse_name { get; set; } = string.Empty;

        /// <summary>
        /// area_name
        /// </summary>
        [Display(Name = "area_name")]
        [MaxLength(32, ErrorMessage = "MaxLength")]
        [Required(ErrorMessage = "Required")]
        public string area_name { get; set; } = string.Empty;

        /// <summary>
        /// parent_id
        /// </summary>
        [Display(Name = "parent_id")]
        public int parent_id { get; set; } = 0;

        /// <summary>
        /// create_time
        /// </summary>
        [Display(Name = "create_time")]
        [DataType(DataType.DateTime, ErrorMessage = "DataType_DateTime")]
        public DateTime create_time { get; set; } = UtilConvert.MinDate;

        /// <summary>
        /// last_update_time
        /// </summary>
        [Display(Name = "last_update_time")]
        [DataType(DataType.DateTime, ErrorMessage = "DataType_DateTime")]
        public DateTime last_update_time { get; set; } = UtilConvert.MinDate;

        /// <summary>
        /// is_valid
        /// </summary>
        [Display(Name = "is_valid")]
        public bool is_valid { get; set; } = true;


        /// <summary>
        /// area_property
        /// </summary>
        [Display(Name = "area_property")]
        public byte area_property { get; set; } = 0;

        /// <summary>
        /// sort
        /// </summary>
        [Display(Name = "sort")]
        [Range(0, int.MaxValue, ErrorMessage = "Range")]
        public int sort { get; set; } = 0;

        /// <summary>
        /// Bound ERP operator-group ids. Binding is optional and supports multiple groups.
        /// </summary>
        [Display(Name = "operator_group_ids")]
        public List<long> operator_group_ids { get; set; } = new();

        /// <summary>
        /// Current ERP operator-group names ordered by system_dept.sort.
        /// </summary>
        [Display(Name = "operator_group_names")]
        public List<string> operator_group_names { get; set; } = new();


        #endregion

    }
}
