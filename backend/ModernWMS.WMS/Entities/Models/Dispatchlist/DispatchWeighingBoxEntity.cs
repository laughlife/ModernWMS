using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using ModernWMS.Core.Models;

namespace ModernWMS.WMS.Entities.Models;

/// <summary>
/// WMS-owned physical measurement for one ERP FBA box.
/// </summary>
[Table("dispatch_weighing_box")]
public class DispatchWeighingBoxEntity : BaseModel
{
    public long tenant_id { get; set; }

    [MaxLength(32)]
    public string dispatch_no { get; set; } = string.Empty;

    public long fba_shipment_id { get; set; }

    [MaxLength(64)]
    public string fba_no { get; set; } = string.Empty;

    public long erp_box_id { get; set; }

    [MaxLength(64)]
    public string box_no { get; set; } = string.Empty;

    public int box_index { get; set; }

    [MaxLength(128)]
    public string tracking_id { get; set; } = string.Empty;

    public decimal weighing_weight { get; set; }
    public decimal weighing_length { get; set; }
    public decimal weighing_width { get; set; }
    public decimal weighing_height { get; set; }
    public decimal weighing_volume { get; set; }
    public int weighing_person_id { get; set; }

    [MaxLength(64)]
    public string weighing_person { get; set; } = string.Empty;

    public DateTime weighing_time { get; set; }
    public long? copied_from_erp_box_id { get; set; }
    public DateTime create_time { get; set; }

    [ConcurrencyCheck]
    public DateTime last_update_time { get; set; }
}
