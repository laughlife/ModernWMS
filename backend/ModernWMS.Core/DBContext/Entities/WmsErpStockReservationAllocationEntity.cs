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
    /// <summary>
    /// 获取或设置 id。
    /// </summary>
    [Key]
    public long id { get; set; }

    /// <summary>
    /// 获取或设置 tenant_id。
    /// </summary>
    public long tenant_id { get; set; }
    /// <summary>
    /// 获取或设置 reservation_item_id。
    /// </summary>
    public long reservation_item_id { get; set; }
    /// <summary>
    /// 获取或设置 erp_stock_id。
    /// </summary>
    public long erp_stock_id { get; set; }
    /// <summary>
    /// 获取或设置 stock_allocation_id。
    /// </summary>
    public long stock_allocation_id { get; set; }
    /// <summary>
    /// 获取或设置 reserved_qty。
    /// </summary>
    public long reserved_qty { get; set; }
    /// <summary>
    /// 获取或设置 released_qty。
    /// </summary>
    public long released_qty { get; set; }
    /// <summary>
    /// 获取或设置 consumed_qty。
    /// </summary>
    public long consumed_qty { get; set; }
    /// <summary>
    /// 获取或设置 remaining_qty。
    /// </summary>
    public long remaining_qty { get; set; }

    /// <summary>
    /// 获取或设置 status。
    /// </summary>
    [MaxLength(24)]
    public string status { get; set; } = "ACTIVE";

    /// <summary>
    /// 获取或设置 row_version。
    /// </summary>
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
    /// <summary>
    /// 获取或设置 deleted。
    /// </summary>
    public bool deleted { get; set; }
}
