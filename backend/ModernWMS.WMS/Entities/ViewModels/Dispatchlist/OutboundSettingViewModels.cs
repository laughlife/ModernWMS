using System.ComponentModel.DataAnnotations;

namespace ModernWMS.WMS.Entities.ViewModels;

/// <summary>
/// Outbound volume divisor settings.
/// </summary>
public class SetOutboundVolumeDivisorViewModel
{
    /// <summary>
    /// 获取或设置 id。
    /// </summary>
    [Range(1, int.MaxValue)]
    public int id { get; set; }

    /// <summary>
    /// 获取或设置 volume_divisor。
    /// </summary>
    public int volume_divisor { get; set; }
}

/// <summary>
/// Outbound carrier settings.
/// </summary>
public class SetOutboundCarrierViewModel
{
    /// <summary>
    /// 获取或设置 id。
    /// </summary>
    [Range(1, int.MaxValue)]
    public int id { get; set; }

    /// <summary>
    /// 获取或设置 carrier_warehouse_id。
    /// </summary>
    [Range(1, long.MaxValue)]
    public long carrier_warehouse_id { get; set; }
}

/// <summary>
/// Outbound carrier option.
/// </summary>
public class OutboundCarrierOptionViewModel
{
    /// <summary>
    /// 获取或设置 id。
    /// </summary>
    public long id { get; set; }

    /// <summary>
    /// 获取或设置 name。
    /// </summary>
    public string name { get; set; } = string.Empty;
}
