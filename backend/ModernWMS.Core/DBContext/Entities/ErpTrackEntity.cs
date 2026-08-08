using System.ComponentModel.DataAnnotations.Schema;

namespace ModernWMS.Core.DBContext.Entities;

/// <summary>
/// ERP tracking snapshot associated with a logistics shipment.
/// </summary>
[Table("trk_track")]
public class ErpTrackEntity
{
    public long id { get; set; }
    public string track_number { get; set; } = string.Empty;
    public string tracking_status { get; set; } = string.Empty;
    public string? provider_status_code { get; set; }
    public string? provider_status_name { get; set; }
    public string? provider_main_status { get; set; }
    public string? provider_sub_status { get; set; }
    public string? business_stage { get; set; }
    public DateTime? last_event_time { get; set; }
    public string? last_event_description { get; set; }
    public string? last_event_location { get; set; }
    public string? last_event_stage { get; set; }
    public DateTime? estimated_delivery_time { get; set; }
    public DateTime? actual_delivery_time { get; set; }
    public DateTime update_time { get; set; }
    public bool deleted { get; set; }
}
