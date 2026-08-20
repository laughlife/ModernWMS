using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ModernWMS.Core.DBContext.Entities;

/// <summary>
/// Location, owner and batch-attribute allocation of the ERP stock balance.
/// Quantities decompose trk_stock and are not an independent inventory balance.
/// </summary>
[Table("wms_erp_stock_allocation")]
public class WmsErpStockAllocationEntity
{
    [Key]
    public long id { get; set; }

    public long tenant_id { get; set; }
    public long erp_stock_id { get; set; }
    public int? warehouse_area_id { get; set; }
    public int? goods_location_id { get; set; }
    public int goods_owner_id { get; set; }

    [MaxLength(128)]
    public string series_number { get; set; } = string.Empty;

    public DateTime expiry_date { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal price { get; set; }

    public DateTime putaway_date { get; set; }
    public long allocated_qty { get; set; }
    public long occupied_qty { get; set; }

    [MaxLength(16)]
    public string location_state { get; set; } = string.Empty;

    [ConcurrencyCheck]
    public long row_version { get; set; }

    [MaxLength(128)]
    public string creator { get; set; } = string.Empty;

    public DateTime create_time { get; set; }

    [MaxLength(128)]
    public string updater { get; set; } = string.Empty;

    public DateTime update_time { get; set; }
}
