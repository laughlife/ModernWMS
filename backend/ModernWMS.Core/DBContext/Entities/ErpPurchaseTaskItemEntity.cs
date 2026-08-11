using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ModernWMS.Core.DBContext.Entities;

/// <summary>
/// Minimal read-only projection of an ERP purchase-task item used for batch purchase prices.
/// </summary>
[Table("erp_purchase_task_item")]
public class ErpPurchaseTaskItemEntity
{
    [Key]
    public long id { get; set; }

    /// <summary>Purchase task header id.</summary>
    public long task_id { get; set; }

    /// <summary>Purchase unit price in RMB.</summary>
    public decimal? per_purchase { get; set; }

    /// <summary>Ruoyi logical deletion flag.</summary>
    public bool deleted { get; set; }
}
