using System.ComponentModel.DataAnnotations.Schema;

namespace ModernWMS.Core.DBContext.Entities;

/// <summary>
/// ERP logistics shipment fact used by the ModernWMS pending-receipt view.
/// </summary>
[Table("trk_logistics_info")]
public class ErpLogisticsInfoEntity
{
    /// <summary>
    /// 获取或设置 id。
    /// </summary>
    public long id { get; set; }
    /// <summary>
    /// 获取或设置 source_type。
    /// </summary>
    public string source_type { get; set; } = string.Empty;
    /// <summary>
    /// 获取或设置 create_source。
    /// </summary>
    public string create_source { get; set; } = string.Empty;
    /// <summary>
    /// 获取或设置 source_task_id。
    /// </summary>
    public long? source_task_id { get; set; }
    /// <summary>
    /// 获取或设置 source_shipment_batch_id。
    /// </summary>
    public long source_shipment_batch_id { get; set; }
    /// <summary>
    /// 获取或设置 source_stock_move_id。
    /// </summary>
    public long? source_stock_move_id { get; set; }
    /// <summary>
    /// 获取或设置 source_stock_move_no。
    /// </summary>
    public string? source_stock_move_no { get; set; }
    /// <summary>
    /// 获取或设置 purchase_no。
    /// </summary>
    public string? purchase_no { get; set; }
    /// <summary>
    /// 获取或设置 supplier_name。
    /// </summary>
    public string? supplier_name { get; set; }
    /// <summary>
    /// 获取或设置 order_user_text。
    /// </summary>
    public string? order_user_text { get; set; }
    /// <summary>
    /// 获取或设置 dept_id。
    /// </summary>
    public long? dept_id { get; set; }
    /// <summary>
    /// 获取或设置 shipment_batch_no。
    /// </summary>
    public string shipment_batch_no { get; set; } = string.Empty;
    /// <summary>
    /// 获取或设置 shipment_type。
    /// </summary>
    public string? shipment_type { get; set; }
    /// <summary>
    /// 获取或设置 shipment_qty。
    /// </summary>
    public long? shipment_qty { get; set; }
    /// <summary>
    /// 获取或设置 shipment_time。
    /// </summary>
    public DateTime? shipment_time { get; set; }
    /// <summary>
    /// 获取或设置 product_snapshot_json。
    /// </summary>
    public string product_snapshot_json { get; set; } = "[]";
    /// <summary>
    /// 获取或设置 freight_forwarder_name。
    /// </summary>
    public string? freight_forwarder_name { get; set; }
    /// <summary>
    /// 获取或设置 freight_forwarder_id。
    /// </summary>
    public long? freight_forwarder_id { get; set; }
    /// <summary>
    /// 获取或设置 source_freight_payment_type。
    /// </summary>
    public string? source_freight_payment_type { get; set; }
    /// <summary>
    /// 获取或设置 to_warehouse_id。
    /// </summary>
    public long? to_warehouse_id { get; set; }
    /// <summary>
    /// 获取或设置 to_warehouse_name。
    /// </summary>
    public string? to_warehouse_name { get; set; }
    /// <summary>
    /// 获取或设置 track_provider_code。
    /// </summary>
    public string? track_provider_code { get; set; }
    /// <summary>
    /// 获取或设置 carrier_code。
    /// </summary>
    public string? carrier_code { get; set; }
    /// <summary>
    /// 获取或设置 carrier_name。
    /// </summary>
    public string? carrier_name { get; set; }
    /// <summary>
    /// 获取或设置 tracking_no。
    /// </summary>
    public string? tracking_no { get; set; }
    /// <summary>
    /// 获取或设置 lifecycle_status。
    /// </summary>
    public string lifecycle_status { get; set; } = string.Empty;
    /// <summary>
    /// 获取或设置 actual_receipt_qty。
    /// </summary>
    public long? actual_receipt_qty { get; set; }
    /// <summary>
    /// 获取或设置 receipt_time。
    /// </summary>
    public DateTime? receipt_time { get; set; }
    /// <summary>
    /// 获取或设置 receipt_remark。
    /// </summary>
    public string? receipt_remark { get; set; }
    /// <summary>
    /// 获取或设置 receipt_attachment_list。
    /// </summary>
    public string? receipt_attachment_list { get; set; }
    /// <summary>
    /// 获取或设置 receipt_freight_payment_status。
    /// </summary>
    public string? receipt_freight_payment_status { get; set; }
    /// <summary>
    /// 获取或设置 receipt_freight_amount。
    /// </summary>
    public decimal? receipt_freight_amount { get; set; }
    /// <summary>
    /// 获取或设置 receipt_freight_attachment_list。
    /// </summary>
    public string? receipt_freight_attachment_list { get; set; }
    /// <summary>
    /// 获取或设置 loss_qty。
    /// </summary>
    public long? loss_qty { get; set; }
    /// <summary>
    /// 获取或设置 loss_reason。
    /// </summary>
    public string? loss_reason { get; set; }
    /// <summary>
    /// 获取或设置 loss_attachment_list。
    /// </summary>
    public string? loss_attachment_list { get; set; }
    /// <summary>
    /// 获取或设置 last_sync_time。
    /// </summary>
    public DateTime? last_sync_time { get; set; }
    /// <summary>
    /// 获取或设置 source_version。
    /// </summary>
    public int source_version { get; set; }
    /// <summary>
    /// 获取或设置 create_time。
    /// </summary>
    public DateTime create_time { get; set; }
    /// <summary>
    /// 获取或设置 update_time。
    /// </summary>
    public DateTime update_time { get; set; }
    /// <summary>
    /// 获取或设置 updater。
    /// </summary>
    public string? updater { get; set; }
    /// <summary>
    /// 获取或设置 deleted。
    /// </summary>
    public bool deleted { get; set; }
}
