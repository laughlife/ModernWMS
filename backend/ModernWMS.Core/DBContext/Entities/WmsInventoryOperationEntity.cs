using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ModernWMS.Core.DBContext.Entities;

/// <summary>
/// Globally idempotent inventory mutation command header within a tenant.
/// ERP stock and allocation identifiers are logical references without physical foreign keys.
/// </summary>
[Table("wms_inventory_operation")]
public class WmsInventoryOperationEntity
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
    public string mutation_type { get; set; } = string.Empty;

    public long erp_stock_id { get; set; }
    public long allocation_id { get; set; }
    public long? counterpart_allocation_id { get; set; }
    public long quantity { get; set; }

    [MaxLength(64)]
    public string @operator { get; set; } = string.Empty;

    [MaxLength(16)]
    public string result_status { get; set; } = "PENDING";

    public long? erp_stock_record_id { get; set; }
    public DateTime create_time { get; set; }
    public DateTime update_time { get; set; }
}
