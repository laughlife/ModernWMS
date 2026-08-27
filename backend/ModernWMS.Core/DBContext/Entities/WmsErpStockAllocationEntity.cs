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
    /// <summary>
    /// 获取或设置 id。
    /// </summary>
    [Key]
    public long id { get; set; }

    /// <summary>
    /// 获取或设置 erp_stock_id。
    /// </summary>
    public long erp_stock_id { get; set; }
    /// <summary>
    /// 获取或设置 warehouse_area_id。
    /// </summary>
    public int? warehouse_area_id { get; set; }
    /// <summary>
    /// 获取或设置 goods_location_id。
    /// </summary>
    public int? goods_location_id { get; set; }
    /// <summary>
    /// 获取或设置 goods_owner_id。
    /// </summary>
    public int goods_owner_id { get; set; }

    /// <summary>
    /// 获取或设置 series_number。
    /// </summary>
    [MaxLength(128)]
    public string series_number { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置 expiry_date。
    /// </summary>
    public DateTime expiry_date { get; set; }

    /// <summary>
    /// 获取或设置 price。
    /// </summary>
    [Column(TypeName = "decimal(18,2)")]
    public decimal price { get; set; }

    /// <summary>
    /// 获取或设置 putaway_date。
    /// </summary>
    public DateTime putaway_date { get; set; }
    /// <summary>
    /// 获取或设置 allocated_qty。
    /// </summary>
    public long allocated_qty { get; set; }
    /// <summary>
    /// 获取或设置 occupied_qty。
    /// </summary>
    public long occupied_qty { get; set; }

    /// <summary>
    /// 获取或设置 location_state。
    /// </summary>
    [MaxLength(16)]
    public string location_state { get; set; } = string.Empty;

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
