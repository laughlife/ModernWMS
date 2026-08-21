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
    /// <summary>
    /// 获取或设置 record_no。
    /// </summary>
    [MaxLength(64)]
    public string record_no { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置 biz_type。
    /// </summary>
    [MaxLength(32)]
    public string biz_type { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置 biz_id。
    /// </summary>
    public long biz_id { get; set; }
    /// <summary>
    /// 获取或设置 biz_item_id。
    /// </summary>
    public long biz_item_id { get; set; }
    /// <summary>
    /// 获取或设置 stock_id。
    /// </summary>
    public int stock_id { get; set; }
    /// <summary>
    /// 获取或设置 sku_id。
    /// </summary>
    public int sku_id { get; set; }
    /// <summary>
    /// 获取或设置 goods_location_id。
    /// </summary>
    public int goods_location_id { get; set; }
    /// <summary>
    /// 获取或设置 goods_owner_id。
    /// </summary>
    public int goods_owner_id { get; set; }
    /// <summary>
    /// 获取或设置 change_qty。
    /// </summary>
    public long change_qty { get; set; }
    /// <summary>
    /// 获取或设置 before_qty。
    /// </summary>
    public long before_qty { get; set; }
    /// <summary>
    /// 获取或设置 after_qty。
    /// </summary>
    public long after_qty { get; set; }

    /// <summary>
    /// 获取或设置 direction。
    /// </summary>
    [MaxLength(8)]
    public string direction { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置 operator_id。
    /// </summary>
    public int operator_id { get; set; }

    /// <summary>
    /// 获取或设置 operator_name。
    /// </summary>
    [MaxLength(128)]
    public string operator_name { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置 remark。
    /// </summary>
    [MaxLength(500)]
    public string remark { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置 operate_time。
    /// </summary>
    public DateTime operate_time { get; set; } = DateTime.Now;
    /// <summary>
    /// 获取或设置 tenant_id。
    /// </summary>
    public long tenant_id { get; set; } = 1;
}
