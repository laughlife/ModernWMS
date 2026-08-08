using System.ComponentModel.DataAnnotations.Schema;

namespace ModernWMS.Core.DBContext.Entities;

/// <summary>
/// ERP logistics tracking event used by the WMS track timeline.
/// </summary>
[Table("trk_track_event")]
public class ErpTrackEventEntity
{
    public long id { get; set; }
    public long track_id { get; set; }
    public string track_number { get; set; } = string.Empty;
    public string provider_code { get; set; } = string.Empty;
    public string? provider_status_code { get; set; }
    public string? provider_status_name { get; set; }
    public string? provider_main_status { get; set; }
    public string? provider_sub_status { get; set; }
    public DateTime? event_time { get; set; }
    public string? description { get; set; }
    public string? location { get; set; }
    public string? stage { get; set; }
    public int sort { get; set; }
    public bool deleted { get; set; }
}
