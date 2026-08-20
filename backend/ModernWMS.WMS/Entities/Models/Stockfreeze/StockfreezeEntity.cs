/*
 * date：2022-12-26
 * developer：NoNo
 */

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.ComponentModel.DataAnnotations;
using ModernWMS.Core.Models;
using ModernWMS.Core.Utility;

namespace ModernWMS.WMS.Entities.Models
{
    /// <summary>
    /// stockfreeze  entity
    /// </summary>
    [Table("stockfreeze")]
    public class StockfreezeEntity : BaseModel
    {
        #region Property

        /// <summary>
        /// job_code
        /// </summary>
        public string job_code { get; set; } = string.Empty;

        /// <summary>
        /// job_type
        /// </summary>
        public bool job_type { get; set; } = false;

        /// <summary>
        /// sku_id
        /// </summary>
        public int sku_id { get; set; } = 0;

        /// <summary>
        /// goods_owner_id
        /// </summary>
        public int goods_owner_id { get; set; } = 0;

        /// <summary>
        /// goods_location_id
        /// </summary>
        public int goods_location_id { get; set; } = 0;

        /// <summary>
        /// handler
        /// </summary>
        public string handler { get; set; } = string.Empty;

        /// <summary>
        /// operate_time
        /// </summary>
        public DateTime handle_time { get; set; } = UtilConvert.MinDate;

        /// <summary>
        /// last_update_time
        /// </summary>
        public DateTime last_update_time { get; set; } = UtilConvert.MinDate;

        /// <summary>
        /// tenant_id
        /// </summary>
        public long tenant_id { get; set; } = 0;

        public long? erp_stock_id { get; set; }

        public long? stock_allocation_id { get; set; }

        public long? reservation_id { get; set; }

        public long? reservation_item_id { get; set; }

        public long? source_freeze_id { get; set; }

        /// <summary>
        /// series_number
        /// </summary>
        public string series_number { get; set; } = string.Empty;

        #endregion Property
    }
}
