using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ModernWMS.Core.DBContext.Entities;

/// <summary>
/// Decomposes an ERP reservation item across WMS stock allocations.
/// It is an occupied-quantity ownership projection, never an inventory balance.
/// </summary>
[Table("wms_erp_stock_reservation_allocation")]
public class WmsErpStockReservationAllocationEntity
{
    [Key]
    public long id { get; set; }

    public long tenant_id { get; set; }
    public long reservation_item_id { get; set; }
    public long erp_stock_id { get; set; }
    public long stock_allocation_id { get; set; }
    public long reserved_qty { get; set; }
    public long released_qty { get; set; }
    public long consumed_qty { get; set; }
    public long remaining_qty { get; set; }

    [MaxLength(24)]
    public string status { get; set; } = "ACTIVE";

    public long row_version { get; set; }

    [MaxLength(128)]
    public string creator { get; set; } = string.Empty;

    public DateTime create_time { get; set; }

    [MaxLength(128)]
    public string updater { get; set; } = string.Empty;

    public DateTime update_time { get; set; }
    public bool deleted { get; set; }
}

