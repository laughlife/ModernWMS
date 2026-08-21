using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using ModernWMS.Core.Models;

namespace ModernWMS.Core.DBContext.Entities;

/// <summary>
/// Product-level receipt result shared by ERP and the WMS physical ledger.
/// </summary>
[Table("wms_erp_receipt_item")]
public class ErpReceiptItemEntity : BaseModel
{
    /// <summary>
    /// 获取或设置 receipt_id。
    /// </summary>
    public int receipt_id { get; set; }
    /// <summary>
    /// 获取或设置 shipment_id。
    /// </summary>
    public long shipment_id { get; set; }

    /// <summary>
    /// 获取或设置 source_item_key。
    /// </summary>
    [MaxLength(160)]
    public string source_item_key { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置 task_item_id。
    /// </summary>
    public long? task_item_id { get; set; }
    /// <summary>
    /// 获取或设置 allocation_id。
    /// </summary>
    public long? allocation_id { get; set; }
    /// <summary>
    /// 获取或设置 commodity_id。
    /// </summary>
    public long? commodity_id { get; set; }

    /// <summary>
    /// 获取或设置 commodity_sku。
    /// </summary>
    [MaxLength(128)]
    public string commodity_sku { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置 commodity_name。
    /// </summary>
    [MaxLength(255)]
    public string commodity_name { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置 dept_id。
    /// </summary>
    public long? dept_id { get; set; }
    /// <summary>
    /// 获取或设置 order_user_id。
    /// </summary>
    public long? order_user_id { get; set; }

    /// <summary>
    /// 获取或设置 dept_name。
    /// </summary>
    [MaxLength(128)]
    public string dept_name { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置 order_user_name。
    /// </summary>
    [MaxLength(128)]
    public string order_user_name { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置 warehouse_area_id。
    /// </summary>
    public int warehouse_area_id { get; set; }

    /// <summary>
    /// 获取或设置 warehouse_area_name。
    /// </summary>
    [MaxLength(128)]
    public string warehouse_area_name { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置 shipment_qty。
    /// </summary>
    public long shipment_qty { get; set; }
    /// <summary>
    /// 获取或设置 actual_receipt_qty。
    /// </summary>
    public long actual_receipt_qty { get; set; }
    /// <summary>
    /// 获取或设置 loss_qty。
    /// </summary>
    public long loss_qty { get; set; }
    /// <summary>
    /// 获取或设置 inbound_qty。
    /// </summary>
    public long inbound_qty { get; set; }
    /// <summary>
    /// 获取或设置 erp_stock_id。
    /// </summary>
    public long? erp_stock_id { get; set; }
    /// <summary>
    /// 获取或设置 wms_sku_id。
    /// </summary>
    public int wms_sku_id { get; set; }
    /// <summary>
    /// 获取或设置 wms_stock_id。
    /// </summary>
    public int? wms_stock_id { get; set; }
    /// <summary>
    /// 获取或设置 primary_stock_allocation_id。
    /// </summary>
    public long? primary_stock_allocation_id { get; set; }
    /// <summary>
    /// 获取或设置 receipt_time。
    /// </summary>
    public DateTime receipt_time { get; set; }
    /// <summary>
    /// 获取或设置 total_weight。
    /// </summary>
    public decimal? total_weight { get; set; }
    /// <summary>
    /// 获取或设置 total_volume。
    /// </summary>
    public decimal? total_volume { get; set; }
    /// <summary>
    /// 获取或设置 create_time。
    /// </summary>
    public DateTime create_time { get; set; } = DateTime.Now;
    /// <summary>
    /// 获取或设置 tenant_id。
    /// </summary>
    public long tenant_id { get; set; } = 1;
}
