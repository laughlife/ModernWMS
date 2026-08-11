using System.ComponentModel.DataAnnotations;

namespace ModernWMS.WMS.Entities.ViewModels;

public class SetOutboundVolumeDivisorViewModel
{
    [Range(1, int.MaxValue)]
    public int id { get; set; }

    public int volume_divisor { get; set; }
}

public class SetOutboundCarrierViewModel
{
    [Range(1, int.MaxValue)]
    public int id { get; set; }

    [Range(1, long.MaxValue)]
    public long carrier_warehouse_id { get; set; }
}

public class OutboundCarrierOptionViewModel
{
    public long id { get; set; }

    public string name { get; set; } = string.Empty;
}
