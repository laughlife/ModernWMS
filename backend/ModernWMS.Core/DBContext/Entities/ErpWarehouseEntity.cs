using System.ComponentModel.DataAnnotations.Schema;

namespace ModernWMS.Core.DBContext.Entities;

/// <summary>
/// Ruoyi erp_warehouse entity mapping used by ModernWMS warehouse binding.
/// </summary>
[Table("erp_warehouse")]
public class ErpWarehouseEntity
{
    public long id { get; set; }

    public string? name { get; set; }

    public string? attr { get; set; }

    public bool deleted { get; set; }
}
