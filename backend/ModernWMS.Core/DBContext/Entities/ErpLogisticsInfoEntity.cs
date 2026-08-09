using System.ComponentModel.DataAnnotations.Schema;

namespace ModernWMS.Core.DBContext.Entities;

/// <summary>
/// ERP logistics shipment fact used by the ModernWMS pending-receipt view.
/// </summary>
[Table("trk_logistics_info")]
public class ErpLogisticsInfoEntity
{
    public long id { get; set; }
    public string source_type { get; set; } = string.Empty;
    public string create_source { get; set; } = string.Empty;
    public long? source_task_id { get; set; }
    public long source_shipment_batch_id { get; set; }
    public long? source_stock_move_id { get; set; }
    public string? source_stock_move_no { get; set; }
    public string? purchase_no { get; set; }
    public string? supplier_name { get; set; }
    public string? order_user_text { get; set; }
    public long? dept_id { get; set; }
    public string shipment_batch_no { get; set; } = string.Empty;
    public string? shipment_type { get; set; }
    public long? shipment_qty { get; set; }
    public DateTime? shipment_time { get; set; }
    public string product_snapshot_json { get; set; } = "[]";
    public string? freight_forwarder_name { get; set; }
    public long? freight_forwarder_id { get; set; }
    public string? source_freight_payment_type { get; set; }
    public long? to_warehouse_id { get; set; }
    public string? to_warehouse_name { get; set; }
    public string? track_provider_code { get; set; }
    public string? carrier_code { get; set; }
    public string? carrier_name { get; set; }
    public string? tracking_no { get; set; }
    public string lifecycle_status { get; set; } = string.Empty;
    public long? actual_receipt_qty { get; set; }
    public DateTime? receipt_time { get; set; }
    public string? receipt_remark { get; set; }
    public string? receipt_attachment_list { get; set; }
    public string? receipt_freight_payment_status { get; set; }
    public decimal? receipt_freight_amount { get; set; }
    public string? receipt_freight_attachment_list { get; set; }
    public long? loss_qty { get; set; }
    public string? loss_reason { get; set; }
    public string? loss_attachment_list { get; set; }
    public DateTime? last_sync_time { get; set; }
    public int source_version { get; set; }
    public DateTime create_time { get; set; }
    public DateTime update_time { get; set; }
    public string? updater { get; set; }
    public bool deleted { get; set; }
}
