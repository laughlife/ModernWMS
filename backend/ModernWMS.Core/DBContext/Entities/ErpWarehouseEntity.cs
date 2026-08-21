using System.ComponentModel.DataAnnotations.Schema;

namespace ModernWMS.Core.DBContext.Entities;

/// <summary>
/// Ruoyi erp_warehouse entity mapping used by ModernWMS warehouse binding.
/// </summary>
[Table("erp_warehouse")]
public class ErpWarehouseEntity
{
    /// <summary>
    /// 获取或设置 id。
    /// </summary>
    public long id { get; set; }

    /// <summary>
    /// 获取或设置 name。
    /// </summary>
    public string? name { get; set; }

    /// <summary>
    /// 获取或设置 attr。
    /// </summary>
    public string? attr { get; set; }

    /// <summary>
    /// 获取或设置 deleted。
    /// </summary>
    public bool deleted { get; set; }
}
