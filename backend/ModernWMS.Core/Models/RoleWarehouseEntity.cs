using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace ModernWMS.Core.Models;

/// <summary>WMS role to ERP warehouse authorization. Tenant is compatibility metadata only.</summary>
[Table("role_warehouse")]
[Index(nameof(role_id), nameof(warehouse_id), IsUnique = true)]
[Index(nameof(warehouse_id))]
public class RoleWarehouseEntity : BaseModel
{
    [ForeignKey(nameof(role_id))]
    public UserroleEntity role { get; set; } = null!;
    public int role_id { get; set; }
    public long warehouse_id { get; set; }
    public long tenant_id { get; set; }
    public int created_by { get; set; }
    public DateTime create_time { get; set; }
    [ConcurrencyCheck] public DateTime last_update_time { get; set; }
}
