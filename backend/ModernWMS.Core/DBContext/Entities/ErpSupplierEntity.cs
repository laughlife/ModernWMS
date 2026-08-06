using System.ComponentModel.DataAnnotations.Schema;

namespace ModernWMS.Core.DBContext.Entities;

/// <summary>
/// Ruoyi erp_supplier entity mapping.
/// </summary>
[Table("erp_supplier")]
public class ErpSupplierEntity
{
    public long id { get; set; }

    public string? name { get; set; }

    public string? linkman { get; set; }

    public string? telephone_num { get; set; }

    public string? qq { get; set; }

    public string? email { get; set; }

    public string? province_name { get; set; }

    public string? city_name { get; set; }

    public string? address_line { get; set; }

    public string? remark { get; set; }

    public DateTime create_time { get; set; }

    public DateTime update_time { get; set; }

    public bool deleted { get; set; }
}
