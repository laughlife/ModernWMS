using System.ComponentModel.DataAnnotations.Schema;

namespace ModernWMS.Core.DBContext.Entities;

/// <summary>
/// ERP 文件存储配置。ModernWMS 仅读取当前主配置，不维护该表。
/// </summary>
[Table("infra_file_config")]
public class ErpFileConfigEntity
{
    /// <summary>
    /// 获取或设置 id。
    /// </summary>
    public long id { get; set; }

    /// <summary>
    /// 获取或设置 name。
    /// </summary>
    public string name { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置 storage。
    /// </summary>
    public int storage { get; set; }

    /// <summary>
    /// 获取或设置 master。
    /// </summary>
    public bool master { get; set; }

    /// <summary>
    /// 获取或设置 config。
    /// </summary>
    public string config { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置 deleted。
    /// </summary>
    public bool deleted { get; set; }
}
