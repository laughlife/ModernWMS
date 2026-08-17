using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using ModernWMS.Core.Models;

namespace ModernWMS.Core.DBContext.Entities;

/// <summary>
/// Immutable WMS physical-stock movement ledger.
/// </summary>
[Table("wms_stock_record")]
public class WmsStockRecordEntity : BaseModel
{
    [MaxLength(64)]
    public string record_no { get; set; } = string.Empty;

    [MaxLength(32)]
    public string biz_type { get; set; } = string.Empty;

    public long biz_id { get; set; }
    public long biz_item_id { get; set; }
    public int stock_id { get; set; }
    public int sku_id { get; set; }
    public int goods_location_id { get; set; }
    public int goods_owner_id { get; set; }
    public long change_qty { get; set; }
    public long before_qty { get; set; }
    public long after_qty { get; set; }

    [MaxLength(8)]
    public string direction { get; set; } = string.Empty;

    public int operator_id { get; set; }

    [MaxLength(128)]
    public string operator_name { get; set; } = string.Empty;

    [MaxLength(500)]
    public string remark { get; set; } = string.Empty;

    public DateTime operate_time { get; set; } = DateTime.Now;
    public long tenant_id { get; set; } = 1;
}
