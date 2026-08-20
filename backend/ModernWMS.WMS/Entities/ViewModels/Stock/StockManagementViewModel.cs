using ModernWMS.Core.Utility;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ModernWMS.WMS.Entities.ViewModels
{
    /// <summary>
    /// StockManagementViewModel
    /// </summary>
    public class StockManagementViewModel
    {
        #region constructor

        /// <summary>
        /// constructor
        /// </summary>
        public StockManagementViewModel()
        {
        }

        #endregion constructor

        #region Property

        /// <summary>
        /// spu_code
        /// </summary>
        public string spu_code { get; set; } = string.Empty;

        /// <summary>
        /// spu_name
        /// </summary>
        public string spu_name { get; set; } = string.Empty;

        /// <summary>
        /// sku_code
        /// </summary>
        public string sku_code { get; set; } = string.Empty;

        /// <summary>
        /// sku_id
        /// </summary>
        public int sku_id { get; set; } = 0;

        /// <summary>
        /// product image url
        /// </summary>
        public string product_image { get; set; } = string.Empty;

        /// <summary>
        /// quantity
        /// </summary>
        public long qty { get; set; } = 0;

        /// <summary>
        /// quantity available
        /// </summary>
        public long qty_available { get; set; } = 0;

        /// <summary>
        /// quantity locked
        /// </summary>
        public long qty_locked { get; set; } = 0;

        public long qty_pending_location { get; set; } = 0;

        public long erp_total_qty { get; set; } = 0;

        public long erp_available_qty { get; set; } = 0;

        public long erp_occupied_qty { get; set; } = 0;

        public bool allocation_consistent { get; set; } = true;

        /// <summary>
        /// asn qty
        /// </summary>
        public int qty_asn { get; set; } = 0;

        /// <summary>
        /// qty to be unloaded
        /// </summary>
        public int qty_to_unload { get; set; } = 0;

        /// <summary>
        ///  qty to be sorted
        /// </summary>
        public int qty_to_sort { get; set; } = 0;

        /// <summary>
        /// qty sorted
        /// </summary>
        public int qty_sorted { get; set; } = 0;

        /// <summary>
        /// shortage qty
        /// </summary>
        public int shortage_qty { get; set; } = 0;


        /// <summary>
        /// expiry_date
        /// </summary>
        public DateTime expiry_date { get; set; } = UtilConvert.MinDate;

        #endregion Property
    }
}
