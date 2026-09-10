/*
 * date：2022-12-21
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
    /// 旧商品模型，仅为历史收货和库存关联保留。
    /// </summary>
    // TODO(REQ-WMS-PRODUCT-003): SPU 不再作为业务维护概念使用，不再扩展或恢复手工维护入口。
    // 暂时保留现有模型及运行依赖；后续专项梳理关联后再评估退役，不直接删除表或字段。
    // ERP 的 spu/spu_name 字段保留；SKU、FNSKU、MSKU 继续使用。
    [Table("spu")]
    public class SpuEntity : BaseModel
    {

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
        /// spu_description
        /// </summary>
        public string spu_description { get; set; } = string.Empty;

        /// <summary>
        /// supplier_id
        /// </summary>
        public int supplier_id { get; set; } = 0;

        /// <summary>
        /// supplier_name
        /// </summary>
        public string supplier_name { get; set; } = string.Empty;

        /// <summary>
        /// brand
        /// </summary>
        public string brand { get; set; } = string.Empty;

        /// <summary>
        /// origin
        /// </summary>
        public string origin { get; set; } = string.Empty;

        /// <summary>
        /// length_unit
        /// </summary>
        public byte length_unit { get; set; } = 0;

        /// <summary>
        /// volume_unit
        /// </summary>
        public byte volume_unit { get; set; } = 0;

        /// <summary>
        /// weight_unit
        /// </summary>
        public byte weight_unit { get; set; } = 0;

        /// <summary>
        /// creator
        /// </summary>
        public string creator { get; set; } = string.Empty;

        /// <summary>
        /// create_time
        /// </summary>
        public DateTime create_time { get; set; } = DateTime.Now;

        /// <summary>
        /// last_update_time
        /// </summary>
        public DateTime last_update_time { get; set; } = DateTime.Now;

        /// <summary>
        /// is_valid
        /// </summary>
        public bool is_valid { get; set; } = true;



        #endregion

        #region details
        /// <summary>
        /// sku
        /// </summary>
        public List<SkuEntity> detailList { get; set; } = new List<SkuEntity>();
        #endregion
    }
}
