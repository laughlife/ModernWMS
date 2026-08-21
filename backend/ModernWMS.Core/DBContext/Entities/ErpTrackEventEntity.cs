using System.ComponentModel.DataAnnotations.Schema;

namespace ModernWMS.Core.DBContext.Entities;

/// <summary>
/// ERP logistics tracking event used by the WMS track timeline.
/// </summary>
[Table("trk_track_event")]
public class ErpTrackEventEntity
{
    /// <summary>
    /// 获取或设置 id。
    /// </summary>
    public long id { get; set; }
    /// <summary>
    /// 获取或设置 track_id。
    /// </summary>
    public long track_id { get; set; }
    /// <summary>
    /// 获取或设置 track_number。
    /// </summary>
    public string track_number { get; set; } = string.Empty;
    /// <summary>
    /// 获取或设置 provider_code。
    /// </summary>
    public string provider_code { get; set; } = string.Empty;
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
    /// 获取或设置 event_time。
    /// </summary>
    public DateTime? event_time { get; set; }
    /// <summary>
    /// 获取或设置 description。
    /// </summary>
    public string? description { get; set; }
    /// <summary>
    /// 获取或设置 location。
    /// </summary>
    public string? location { get; set; }
    /// <summary>
    /// 获取或设置 stage。
    /// </summary>
    public string? stage { get; set; }
    /// <summary>
    /// 获取或设置 sort。
    /// </summary>
    public int sort { get; set; }
    /// <summary>
    /// 获取或设置 deleted。
    /// </summary>
    public bool deleted { get; set; }
}
