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
    /// <summary>Dispatch number associated with the box.</summary>
    [MaxLength(32)]
    public string dispatch_no { get; set; } = string.Empty;

    /// <summary>FBA shipment identifier.</summary>
    public long fba_shipment_id { get; set; }

    /// <summary>FBA shipment number.</summary>
    [MaxLength(64)]
    public string fba_no { get; set; } = string.Empty;

    /// <summary>ERP box identifier.</summary>
    public long erp_box_id { get; set; }

    /// <summary>Box number.</summary>
    [MaxLength(64)]
    public string box_no { get; set; } = string.Empty;

    /// <summary>Zero-based box position in the shipment.</summary>
    public int box_index { get; set; }

    /// <summary>Carrier tracking identifier.</summary>
    [MaxLength(128)]
    public string tracking_id { get; set; } = string.Empty;

    /// <summary>Measured box weight.</summary>
    public decimal weighing_weight { get; set; }
    /// <summary>Measured box length.</summary>
    public decimal weighing_length { get; set; }
    /// <summary>Measured box width.</summary>
    public decimal weighing_width { get; set; }
    /// <summary>Measured box height.</summary>
    public decimal weighing_height { get; set; }
    /// <summary>Measured box volume.</summary>
    public decimal weighing_volume { get; set; }
    /// <summary>Identifier of the weighing operator.</summary>
    public int weighing_person_id { get; set; }

    /// <summary>Name of the weighing operator.</summary>
    [MaxLength(64)]
    public string weighing_person { get; set; } = string.Empty;

    /// <summary>Time when the box was weighed.</summary>
    public DateTime weighing_time { get; set; }
    /// <summary>Source ERP box identifier when copied.</summary>
    public long? copied_from_erp_box_id { get; set; }
    /// <summary>Time when the record was created.</summary>
    public DateTime create_time { get; set; }

    /// <summary>Time when the record was last updated.</summary>
    [ConcurrencyCheck]
    public DateTime last_update_time { get; set; }
}
