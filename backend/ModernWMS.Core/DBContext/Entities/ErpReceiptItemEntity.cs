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
    public int receipt_id { get; set; }
    public long shipment_id { get; set; }

    [MaxLength(160)]
    public string source_item_key { get; set; } = string.Empty;

    public long? task_item_id { get; set; }
    public long? allocation_id { get; set; }
    public long? commodity_id { get; set; }

    [MaxLength(128)]
    public string commodity_sku { get; set; } = string.Empty;

    [MaxLength(255)]
    public string commodity_name { get; set; } = string.Empty;

    public long? dept_id { get; set; }
    public long? order_user_id { get; set; }

    [MaxLength(128)]
    public string dept_name { get; set; } = string.Empty;

    [MaxLength(128)]
    public string order_user_name { get; set; } = string.Empty;

    public int warehouse_area_id { get; set; }

    [MaxLength(128)]
    public string warehouse_area_name { get; set; } = string.Empty;

    public long shipment_qty { get; set; }
    public long actual_receipt_qty { get; set; }
    public long loss_qty { get; set; }
    public long inbound_qty { get; set; }
    public long? erp_stock_id { get; set; }
    public int wms_sku_id { get; set; }
    public int? wms_stock_id { get; set; }
    public long? primary_stock_allocation_id { get; set; }
    public DateTime receipt_time { get; set; }
    public decimal? total_weight { get; set; }
    public decimal? total_volume { get; set; }
    public DateTime create_time { get; set; } = DateTime.Now;
    public long tenant_id { get; set; } = 1;
}
