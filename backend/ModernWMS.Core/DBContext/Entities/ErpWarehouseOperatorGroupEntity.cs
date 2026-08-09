using System.ComponentModel.DataAnnotations.Schema;

namespace ModernWMS.Core.DBContext.Entities;

/// <summary>
/// Warehouse and ERP operator-group binding stored in the shared application database.
/// </summary>
[Table("wms_warehouse_operator_group")]
public class ErpWarehouseOperatorGroupEntity
{
    public long id { get; set; }

    public long tenant_id { get; set; }

    public int warehouse_id { get; set; }

    public long dept_id { get; set; }

    public string creator { get; set; } = string.Empty;

    public DateTime create_time { get; set; }
}
