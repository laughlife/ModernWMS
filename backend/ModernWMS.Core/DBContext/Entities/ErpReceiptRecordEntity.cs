using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using ModernWMS.Core.Models;

namespace ModernWMS.Core.DBContext.Entities;

/// <summary>
/// ModernWMS receipt record created from an ERP logistics shipment.
/// </summary>
[Table("wms_erp_receipt")]
public class ErpReceiptRecordEntity : BaseModel
{
    /// <summary>
    /// 获取或设置 shipment_id。
    /// </summary>
    public long shipment_id { get; set; }

    /// <summary>
    /// 获取或设置 source_version。
    /// </summary>
    public int source_version { get; set; }

    /// <summary>
    /// 获取或设置 actual_receipt_qty。
    /// </summary>
    public long actual_receipt_qty { get; set; }

    /// <summary>
    /// 获取或设置 loss_qty。
    /// </summary>
    public long loss_qty { get; set; }

    /// <summary>
    /// 获取或设置 inbound_qty。
    /// </summary>
    public long inbound_qty { get; set; }

    /// <summary>
    /// 获取或设置 receipt_freight_payment_status。
    /// </summary>
    [MaxLength(16)]
    public string receipt_freight_payment_status { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置 receipt_freight_amount。
    /// </summary>
    [Column(TypeName = "decimal(18,2)")]
    public decimal? receipt_freight_amount { get; set; }

    /// <summary>
    /// 获取或设置 receipt_freight_files_json。
    /// </summary>
    [Column(TypeName = "longtext")]
    public string receipt_freight_files_json { get; set; } = "[]";

    /// <summary>
    /// 获取或设置 receipt_files_json。
    /// </summary>
    [Column(TypeName = "longtext")]
    public string receipt_files_json { get; set; } = "[]";

    /// <summary>
    /// 获取或设置 loss_reason。
    /// </summary>
    [MaxLength(500)]
    public string loss_reason { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置 loss_files_json。
    /// </summary>
    [Column(TypeName = "longtext")]
    public string loss_files_json { get; set; } = "[]";

    /// <summary>
    /// 获取或设置 receipt_remark。
    /// </summary>
    [MaxLength(500)]
    public string receipt_remark { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置 creator。
    /// </summary>
    [MaxLength(64)]
    public string creator { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置 create_time。
    /// </summary>
    public DateTime create_time { get; set; } = DateTime.Now;

    /// <summary>
    /// 获取或设置 last_update_time。
    /// </summary>
    public DateTime last_update_time { get; set; } = DateTime.Now;

    /// <summary>
    /// 获取或设置 tenant_id。
    /// </summary>
    public long tenant_id { get; set; } = 1;
}
