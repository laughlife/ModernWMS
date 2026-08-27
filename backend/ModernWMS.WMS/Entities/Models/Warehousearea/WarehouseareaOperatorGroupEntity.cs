using System.ComponentModel.DataAnnotations.Schema;
using ModernWMS.Core.Models;

namespace ModernWMS.WMS.Entities.Models;

/// <summary>
/// Warehouse-area and ERP operator-group binding.
/// </summary>
[Table("wms_warehousearea_operator_group")]
public class WarehouseareaOperatorGroupEntity : BaseModel
{
    /// <summary>Warehouse area associated with the operator group.</summary>
    public int warehouse_area_id { get; set; }

    /// <summary>ERP department identifier used as the operator group.</summary>
    public long dept_id { get; set; }

    /// <summary>User who created the binding.</summary>
    public string creator { get; set; } = string.Empty;

    /// <summary>Time when the binding was created.</summary>
    public DateTime create_time { get; set; }
}
