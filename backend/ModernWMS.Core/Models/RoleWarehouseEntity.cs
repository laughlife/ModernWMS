using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ModernWMS.Core.Models;

/// <summary>WMS role to ERP warehouse authorization. Tenant is compatibility metadata only.</summary>
[Table("role_warehouse")]
public class RoleWarehouseEntity : BaseModel
{
    /// <summary>
    /// 获取或设置 role。
    /// </summary>
    [ForeignKey(nameof(role_id))]
    public UserroleEntity role { get; set; } = null!;
    /// <summary>
    /// 获取或设置 role_id。
    /// </summary>
    public int role_id { get; set; }
    /// <summary>
    /// 获取或设置 warehouse_id。
    /// </summary>
    public long warehouse_id { get; set; }
    /// <summary>
    /// 获取或设置 tenant_id。
    /// </summary>
    public long tenant_id { get; set; }
    /// <summary>
    /// 获取或设置 created_by。
    /// </summary>
    public int created_by { get; set; }
    /// <summary>
    /// 获取或设置 create_time。
    /// </summary>
    public DateTime create_time { get; set; }
    /// <summary>
    /// 获取或设置 last_update_time。
    /// </summary>
    [ConcurrencyCheck] public DateTime last_update_time { get; set; }
}
