using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using ModernWMS.Core.Models;

namespace ModernWMS.WMS.Entities.Models;

/// <summary>
/// Warehouse-area and ERP operator-group binding.
/// </summary>
[Table("wms_warehousearea_operator_group")]
[Index(nameof(tenant_id), nameof(warehouse_area_id), nameof(dept_id), IsUnique = true)]
public class WarehouseareaOperatorGroupEntity : BaseModel
{
    public long tenant_id { get; set; }

    public int warehouse_area_id { get; set; }

    public long dept_id { get; set; }

    public string creator { get; set; } = string.Empty;

    public DateTime create_time { get; set; }
}
