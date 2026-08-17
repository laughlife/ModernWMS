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
    public long shipment_id { get; set; }

    public int source_version { get; set; }

    public long actual_receipt_qty { get; set; }

    public long loss_qty { get; set; }

    public long inbound_qty { get; set; }

    [MaxLength(16)]
    public string receipt_freight_payment_status { get; set; } = string.Empty;

    [Column(TypeName = "decimal(18,2)")]
    public decimal? receipt_freight_amount { get; set; }

    [Column(TypeName = "longtext")]
    public string receipt_freight_files_json { get; set; } = "[]";

    [Column(TypeName = "longtext")]
    public string receipt_files_json { get; set; } = "[]";

    [MaxLength(500)]
    public string loss_reason { get; set; } = string.Empty;

    [Column(TypeName = "longtext")]
    public string loss_files_json { get; set; } = "[]";

    [MaxLength(500)]
    public string receipt_remark { get; set; } = string.Empty;

    [MaxLength(64)]
    public string creator { get; set; } = string.Empty;

    public DateTime create_time { get; set; } = DateTime.Now;

    public DateTime last_update_time { get; set; } = DateTime.Now;

    public long tenant_id { get; set; } = 1;
}
