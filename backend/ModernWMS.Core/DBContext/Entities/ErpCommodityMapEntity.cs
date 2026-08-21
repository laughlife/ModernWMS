using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using ModernWMS.Core.Models;

namespace ModernWMS.Core.DBContext.Entities;

/// <summary>
/// Maps an ERP commodity to the local WMS SPU and SKU master records.
/// </summary>
[Table("wms_erp_commodity_map")]
public class ErpCommodityMapEntity : BaseModel
{
    /// <summary>
    /// 获取或设置 erp_commodity_id。
    /// </summary>
    public long erp_commodity_id { get; set; }
    /// <summary>
    /// 获取或设置 wms_spu_id。
    /// </summary>
    public int wms_spu_id { get; set; }
    /// <summary>
    /// 获取或设置 wms_sku_id。
    /// </summary>
    public int wms_sku_id { get; set; }

    /// <summary>
    /// 获取或设置 commodity_sku。
    /// </summary>
    [MaxLength(128)]
    public string commodity_sku { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置 last_sync_time。
    /// </summary>
    public DateTime last_sync_time { get; set; } = DateTime.Now;
    /// <summary>
    /// 获取或设置 tenant_id。
    /// </summary>
    public long tenant_id { get; set; } = 1;
}
