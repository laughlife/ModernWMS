using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ModernWMS.Core.DBContext.Entities;

/// <summary>
/// ERP commodity master (ruoyi-vue-pro.erp_commodity) read-only mapping.
/// </summary>
[Table("erp_commodity")]
public class ErpCommodityEntity
{
    /// <summary>
    /// ERP commodity id (API-returned id, stored as varchar).
    /// </summary>
    [Key]
    [MaxLength(64)]
    public string id { get; set; } = string.Empty;

    /// <summary>
    /// commodity sku
    /// </summary>
    [MaxLength(100)]
    public string sku { get; set; } = string.Empty;

    /// <summary>
    /// commodity name
    /// </summary>
    [MaxLength(255)]
    public string name { get; set; } = string.Empty;

    /// <summary>
    /// product image url
    /// </summary>
    public string img_url { get; set; } = string.Empty;
}
