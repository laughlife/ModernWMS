using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using ModernWMS.Core.Models;

namespace ModernWMS.Core.DBContext.Entities;

/// <summary>
/// Maps an ERP commodity to the local WMS SPU and SKU master records.
/// </summary>
[Table("wms_erp_commodity_map")]
[Index(nameof(tenant_id), nameof(erp_commodity_id), IsUnique = true)]
public class ErpCommodityMapEntity : BaseModel
{
    public long erp_commodity_id { get; set; }
    public int wms_spu_id { get; set; }
    public int wms_sku_id { get; set; }

    [MaxLength(128)]
    public string commodity_sku { get; set; } = string.Empty;

    public DateTime last_sync_time { get; set; } = DateTime.Now;
    public long tenant_id { get; set; } = 1;
}
