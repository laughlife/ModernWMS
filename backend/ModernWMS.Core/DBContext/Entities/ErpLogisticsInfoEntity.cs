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
    public string? purchase_no { get; set; }
    public string? supplier_name { get; set; }
    public string? order_user_text { get; set; }
    public string shipment_batch_no { get; set; } = string.Empty;
    public string? shipment_type { get; set; }
    public long? shipment_qty { get; set; }
    public DateTime? shipment_time { get; set; }
    public string product_snapshot_json { get; set; } = "[]";
    public string? freight_forwarder_name { get; set; }
    public long? to_warehouse_id { get; set; }
    public string? to_warehouse_name { get; set; }
    public string? track_provider_code { get; set; }
    public string? carrier_code { get; set; }
    public string? carrier_name { get; set; }
    public string? tracking_no { get; set; }
    public string lifecycle_status { get; set; } = string.Empty;
    public int source_version { get; set; }
    public DateTime create_time { get; set; }
    public DateTime update_time { get; set; }
    public bool deleted { get; set; }
}
