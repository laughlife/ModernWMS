using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ModernWMS.Core.DBContext.Entities;

/// <summary>
/// Audit trail for ERP stock location allocation changes.
/// This table is not an inventory balance ledger.
/// </summary>
[Table("wms_erp_stock_allocation_log")]
public class WmsErpStockAllocationLogEntity
{
    [Key]
    public long id { get; set; }

    public long tenant_id { get; set; }

    [MaxLength(64)]
    public string operation_key { get; set; } = string.Empty;

    [MaxLength(32)]
    public string biz_type { get; set; } = string.Empty;

    public long biz_id { get; set; }
    public long biz_item_id { get; set; }

    [MaxLength(32)]
    public string event_type { get; set; } = string.Empty;

    public long erp_stock_id { get; set; }
    public long allocation_id { get; set; }
    public long? counterpart_allocation_id { get; set; }
    public long? erp_stock_record_id { get; set; }
    public long allocated_delta { get; set; }
    public long occupied_delta { get; set; }
    public long before_allocated_qty { get; set; }
    public long after_allocated_qty { get; set; }
    public long before_occupied_qty { get; set; }
    public long after_occupied_qty { get; set; }

    [MaxLength(128)]
    public string @operator { get; set; } = string.Empty;

    public DateTime operate_time { get; set; }

    [MaxLength(500)]
    public string remark { get; set; } = string.Empty;
}
