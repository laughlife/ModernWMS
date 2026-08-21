using System.ComponentModel.DataAnnotations.Schema;

namespace ModernWMS.Core.DBContext.Entities;

/// <summary>
/// ERP tracking snapshot associated with a logistics shipment.
/// </summary>
[Table("trk_track")]
public class ErpTrackEntity
{
    /// <summary>
    /// 获取或设置 id。
    /// </summary>
    public long id { get; set; }
    /// <summary>
    /// 获取或设置 track_number。
    /// </summary>
    public string track_number { get; set; } = string.Empty;
    /// <summary>
    /// 获取或设置 tracking_status。
    /// </summary>
    public string tracking_status { get; set; } = string.Empty;
    /// <summary>
    /// 获取或设置 provider_status_code。
    /// </summary>
    public string? provider_status_code { get; set; }
    /// <summary>
    /// 获取或设置 provider_status_name。
    /// </summary>
    public string? provider_status_name { get; set; }
    /// <summary>
    /// 获取或设置 provider_main_status。
    /// </summary>
    public string? provider_main_status { get; set; }
    /// <summary>
    /// 获取或设置 provider_sub_status。
    /// </summary>
    public string? provider_sub_status { get; set; }
    /// <summary>
    /// 获取或设置 business_stage。
    /// </summary>
    public string? business_stage { get; set; }
    /// <summary>
    /// 获取或设置 last_event_time。
    /// </summary>
    public DateTime? last_event_time { get; set; }
    /// <summary>
    /// 获取或设置 last_event_description。
    /// </summary>
    public string? last_event_description { get; set; }
    /// <summary>
    /// 获取或设置 last_event_location。
    /// </summary>
    public string? last_event_location { get; set; }
    /// <summary>
    /// 获取或设置 last_event_stage。
    /// </summary>
    public string? last_event_stage { get; set; }
    /// <summary>
    /// 获取或设置 estimated_delivery_time。
    /// </summary>
    public DateTime? estimated_delivery_time { get; set; }
    /// <summary>
    /// 获取或设置 actual_delivery_time。
    /// </summary>
    public DateTime? actual_delivery_time { get; set; }
    /// <summary>
    /// 获取或设置 update_time。
    /// </summary>
    public DateTime update_time { get; set; }
    /// <summary>
    /// 获取或设置 deleted。
    /// </summary>
    public bool deleted { get; set; }
}
