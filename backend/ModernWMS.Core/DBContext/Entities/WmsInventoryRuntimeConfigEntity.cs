using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ModernWMS.Core.DBContext.Entities;

/// <summary>
/// Per-warehouse inventory read mode and maintenance-window gate.
/// Rows are created explicitly; schema migration does not activate cutover.
/// </summary>
[Table("wms_inventory_runtime_config")]
public class WmsInventoryRuntimeConfigEntity
{
    [Key]
    public long id { get; set; }

    public long tenant_id { get; set; }
    public long erp_warehouse_id { get; set; }

    [MaxLength(24)]
    public string mode { get; set; } = "LEGACY_READ";

    public bool maintenance_enabled { get; set; }
    public DateTime? cutover_time { get; set; }

    [ConcurrencyCheck]
    public long row_version { get; set; }

    [MaxLength(128)]
    public string creator { get; set; } = string.Empty;

    public DateTime create_time { get; set; }

    [MaxLength(128)]
    public string updater { get; set; } = string.Empty;

    public DateTime update_time { get; set; }
}
