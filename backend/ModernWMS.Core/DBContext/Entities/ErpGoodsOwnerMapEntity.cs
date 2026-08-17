using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using ModernWMS.Core.Models;

namespace ModernWMS.Core.DBContext.Entities;

/// <summary>
/// Maps the ERP department and order-user ownership dimensions to a WMS goods owner.
/// </summary>
[Table("wms_erp_goods_owner_map")]
public class ErpGoodsOwnerMapEntity : BaseModel
{
    public long erp_dept_id { get; set; }
    public long erp_order_user_id { get; set; }
    public int wms_goods_owner_id { get; set; }

    [MaxLength(128)]
    public string dept_name { get; set; } = string.Empty;

    [MaxLength(128)]
    public string order_user_name { get; set; } = string.Empty;

    public DateTime last_sync_time { get; set; } = DateTime.Now;
    public long tenant_id { get; set; } = 1;
}
