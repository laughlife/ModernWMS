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
    /// <summary>
    /// 获取或设置 id。
    /// </summary>
    [Key]
    public long id { get; set; }

    /// <summary>
    /// 获取或设置 erp_warehouse_id。
    /// </summary>
    public long erp_warehouse_id { get; set; }

    /// <summary>
    /// 获取或设置 mode。
    /// </summary>
    [MaxLength(24)]
    public string mode { get; set; } = "LEGACY_READ";

    /// <summary>
    /// 获取或设置 maintenance_enabled。
    /// </summary>
    public bool maintenance_enabled { get; set; }
    /// <summary>
    /// 获取或设置 cutover_time。
    /// </summary>
    public DateTime? cutover_time { get; set; }

    /// <summary>
    /// 获取或设置 row_version。
    /// </summary>
    [ConcurrencyCheck]
    public long row_version { get; set; }

    /// <summary>
    /// 获取或设置 creator。
    /// </summary>
    [MaxLength(128)]
    public string creator { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置 create_time。
    /// </summary>
    public DateTime create_time { get; set; }

    /// <summary>
    /// 获取或设置 updater。
    /// </summary>
    [MaxLength(128)]
    public string updater { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置 update_time。
    /// </summary>
    public DateTime update_time { get; set; }
}
