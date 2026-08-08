using System.ComponentModel.DataAnnotations.Schema;

namespace ModernWMS.Core.DBContext.Entities;

/// <summary>
/// ERP 文件存储配置。ModernWMS 仅读取当前主配置，不维护该表。
/// </summary>
[Table("infra_file_config")]
public class ErpFileConfigEntity
{
    public long id { get; set; }

    public string name { get; set; } = string.Empty;

    public int storage { get; set; }

    public bool master { get; set; }

    public string config { get; set; } = string.Empty;

    public bool deleted { get; set; }
}
