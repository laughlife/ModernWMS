using System.ComponentModel.DataAnnotations.Schema;

namespace ModernWMS.Core.DBContext.Entities;

/// <summary>
/// Ruoyi system_dept entity mapping.
/// </summary>
[Table("system_dept")]
public class ErpSystemDeptEntity
{
    /// <summary>
    /// primary key
    /// </summary>
    public long id { get; set; }

    /// <summary>
    /// department name
    /// </summary>
    public string? name { get; set; }

    /// <summary>
    /// department business code
    /// </summary>
    public string? dept { get; set; }

    /// <summary>
    /// sort
    /// </summary>
    public int sort { get; set; }

    /// <summary>
    /// leader user id
    /// </summary>
    public long? leader_user_id { get; set; }

    /// <summary>
    /// deleted flag
    /// </summary>
    public bool deleted { get; set; }
}
