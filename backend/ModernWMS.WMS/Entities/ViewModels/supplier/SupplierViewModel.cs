using System.ComponentModel.DataAnnotations;

namespace ModernWMS.WMS.Entities.ViewModels
{
    /// <summary>
    /// supplier viewModel
    /// </summary>
    public class SupplierViewModel
    {
        /// <summary>
        /// id
        /// </summary>
        [Display(Name = "id")]
        public long id { get; set; } = 0;

        /// <summary>
        /// Compatible supplier name field used by existing selectors.
        /// </summary>
        [Display(Name = "supplier_name")]
        [MaxLength(128, ErrorMessage = "MaxLength")]
        public string supplier_name { get; set; } = string.Empty;

        /// <summary>
        /// supplier name
        /// </summary>
        [Display(Name = "name")]
        [MaxLength(128, ErrorMessage = "MaxLength")]
        public string name { get; set; } = string.Empty;

        /// <summary>
        /// contact person
        /// </summary>
        [Display(Name = "linkman")]
        [MaxLength(64, ErrorMessage = "MaxLength")]
        public string linkman { get; set; } = string.Empty;

        /// <summary>
        /// telephone number
        /// </summary>
        [Display(Name = "telephone_num")]
        [MaxLength(32, ErrorMessage = "MaxLength")]
        public string telephone_num { get; set; } = string.Empty;

        /// <summary>
        /// qq
        /// </summary>
        [Display(Name = "qq")]
        [MaxLength(20, ErrorMessage = "MaxLength")]
        public string qq { get; set; } = string.Empty;

        /// <summary>
        /// email
        /// </summary>
        [Display(Name = "email")]
        [MaxLength(254, ErrorMessage = "MaxLength")]
        public string email { get; set; } = string.Empty;

        /// <summary>
        /// province name
        /// </summary>
        [Display(Name = "province_name")]
        [MaxLength(80, ErrorMessage = "MaxLength")]
        public string province_name { get; set; } = string.Empty;

        /// <summary>
        /// city name
        /// </summary>
        [Display(Name = "city_name")]
        [MaxLength(50, ErrorMessage = "MaxLength")]
        public string city_name { get; set; } = string.Empty;

        /// <summary>
        /// detailed address
        /// </summary>
        [Display(Name = "address_line")]
        [MaxLength(255, ErrorMessage = "MaxLength")]
        public string address_line { get; set; } = string.Empty;

        /// <summary>
        /// remark
        /// </summary>
        [Display(Name = "remark")]
        [MaxLength(512, ErrorMessage = "MaxLength")]
        public string remark { get; set; } = string.Empty;
    }
}
