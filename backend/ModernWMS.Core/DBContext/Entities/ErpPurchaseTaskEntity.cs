using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ModernWMS.Core.DBContext.Entities;

/// <summary>
/// Minimal read-only projection of an ERP purchase task used for purchaser names.
/// </summary>
[Table("erp_purchase_task")]
public class ErpPurchaseTaskEntity
{
    /// <summary>
    /// 获取或设置 id。
    /// </summary>
    [Key]
    public long id { get; set; }

    /// <summary>Actual purchaser name from Sellfox.</summary>
    public string? purchaser_name { get; set; }

    /// <summary>Ruoyi logical deletion flag.</summary>
    public bool deleted { get; set; }
}
