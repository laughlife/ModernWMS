using System.ComponentModel.DataAnnotations.Schema;

namespace ModernWMS.Core.DBContext.Entities;

/// <summary>
/// Warehouse and ERP operator-group binding stored in the shared application database.
/// </summary>
[Table("wms_warehouse_operator_group")]
public class ErpWarehouseOperatorGroupEntity
{
    /// <summary>
    /// 获取或设置 id。
    /// </summary>
    public long id { get; set; }

    /// <summary>
    /// 获取或设置 tenant_id。
    /// </summary>
    public long tenant_id { get; set; }

    /// <summary>
    /// 获取或设置 warehouse_id。
    /// </summary>
    public int warehouse_id { get; set; }

    /// <summary>
    /// 获取或设置 dept_id。
    /// </summary>
    public long dept_id { get; set; }

    /// <summary>
    /// 获取或设置 creator。
    /// </summary>
    public string creator { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置 create_time。
    /// </summary>
    public DateTime create_time { get; set; }
}
