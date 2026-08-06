using System.ComponentModel.DataAnnotations.Schema;

namespace ModernWMS.Core.DBContext.Entities;

/// <summary>
/// Ruoyi system_users entity mapping.
/// </summary>
[Table("system_users")]
public class ErpSystemUserEntity
{
    /// <summary>
    /// primary key
    /// </summary>
    public long id { get; set; }

    /// <summary>
    /// nickname
    /// </summary>
    public string? nickname { get; set; }

    /// <summary>
    /// mobile
    /// </summary>
    public string? mobile { get; set; }

    /// <summary>
    /// deleted flag
    /// </summary>
    public bool deleted { get; set; }
}
