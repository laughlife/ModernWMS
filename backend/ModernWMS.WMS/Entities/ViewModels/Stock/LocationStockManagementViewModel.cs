/*
 * date：2023-9-3
 * developer：NoNo
 */

using ModernWMS.Core.Utility;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace ModernWMS.WMS.Entities.ViewModels
{
    /// <summary>
    /// goods location stock management viewmodel
    /// </summary>
    public class LocationStockManagementViewModel
    {
        /// <summary>
        /// 获取或设置 erp_stock_id。
        /// </summary>
        public long? erp_stock_id { get; set; }

        /// <summary>
        /// 获取或设置 stock_allocation_id。
        /// </summary>
        public long? stock_allocation_id { get; set; }

        /// <summary>
        /// 获取或设置 inventory_mode。
        /// </summary>
        public string inventory_mode { get; set; } = "ERP_STOCK";

        /// <summary>
        /// 获取或设置 location_state。
        /// </summary>
        public string location_state { get; set; } = "LEGACY";

        /// <summary>
        /// 获取或设置 is_pending_location。
        /// </summary>
        public bool is_pending_location { get; set; }

        /// <summary>
        /// 获取或设置 allocation_consistent。
        /// </summary>
        public bool allocation_consistent { get; set; } = true;

        /// <summary>
        /// warehouse_name
        /// </summary>
        public string warehouse_name { get; set; } = string.Empty;

        /// <summary>
        /// warehouse_id
        /// </summary>
        public int warehouse_id { get; set; } = 0;

        /// <summary>
        /// warehouse_area_id
        /// </summary>
        public int? warehouse_area_id { get; set; }

        /// <summary>
        /// location_name
        /// </summary>
        public string location_name { get; set; } = string.Empty;

        /// <summary>
        /// warehouse_area_name
        /// </summary>
        public string warehouse_area_name { get; set; } = string.Empty;

        /// <summary>
        /// spu_code
        /// </summary>
        public string spu_code { get; set; } = string.Empty;

        /// <summary>
        /// spu_name
        /// </summary>
        public string spu_name { get; set; } = string.Empty;

        /// <summary>
        /// sku_id
        /// </summary>
        public int sku_id { get; set; } = 0;

        /// <summary>
        /// sku_code
        /// </summary>
        public string sku_code { get; set; } = string.Empty;

        /// <summary>
        /// sku_name
        /// </summary>
        public string sku_name { get; set; } = string.Empty;

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

        /// <summary>
        /// 获取或设置 erp_total_qty。
        /// </summary>
        public long erp_total_qty { get; set; } = 0;

        /// <summary>
        /// 获取或设置 erp_available_qty。
        /// </summary>
        public long erp_available_qty { get; set; } = 0;

        /// <summary>
        /// 获取或设置 erp_occupied_qty。
        /// </summary>
        public long erp_occupied_qty { get; set; } = 0;

        /// <summary>
        /// goods owner name
        /// </summary>
        public string goods_owner_name { get; set; } = string.Empty;

        /// <summary>
        /// product image url
        /// </summary>
        public string product_image { get; set; } = string.Empty;

        /// <summary>
        /// series_number
        /// </summary>
        [Display(Name = "series_number")]
        [MaxLength(64, ErrorMessage = "MaxLength")]
        public string series_number { get; set; } = string.Empty;

        /// <summary>
        /// goods_location_id
        /// </summary>
        public int? goods_location_id { get; set; }

        /// <summary>
        /// expiry_date
        /// </summary>
        public DateTime expiry_date { get; set; } = UtilConvert.MinDate;

        /// <summary>
        /// price
        /// </summary>
        public decimal price { get; set; } = 0;

        /// <summary>
        /// putaway_date
        /// </summary>
        public DateTime putaway_date { get; set; } = UtilConvert.MinDate;


    }
}
