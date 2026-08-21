using System.ComponentModel.DataAnnotations.Schema;

namespace ModernWMS.Core.DBContext.Entities;

/// <summary>
/// Ruoyi erp_supplier entity mapping.
/// </summary>
[Table("erp_supplier")]
public class ErpSupplierEntity
{
    /// <summary>
    /// 获取或设置 id。
    /// </summary>
    public long id { get; set; }

    /// <summary>
    /// 获取或设置 name。
    /// </summary>
    public string? name { get; set; }

    /// <summary>
    /// 获取或设置 linkman。
    /// </summary>
    public string? linkman { get; set; }

    /// <summary>
    /// 获取或设置 telephone_num。
    /// </summary>
    public string? telephone_num { get; set; }

    /// <summary>
    /// 获取或设置 qq。
    /// </summary>
    public string? qq { get; set; }

    /// <summary>
    /// 获取或设置 email。
    /// </summary>
    public string? email { get; set; }

    /// <summary>
    /// 获取或设置 province_name。
    /// </summary>
    public string? province_name { get; set; }

    /// <summary>
    /// 获取或设置 city_name。
    /// </summary>
    public string? city_name { get; set; }

    /// <summary>
    /// 获取或设置 address_line。
    /// </summary>
    public string? address_line { get; set; }

    /// <summary>
    /// 获取或设置 remark。
    /// </summary>
    public string? remark { get; set; }

    /// <summary>
    /// 获取或设置 create_time。
    /// </summary>
    public DateTime create_time { get; set; }

    /// <summary>
    /// 获取或设置 update_time。
    /// </summary>
    public DateTime update_time { get; set; }

    /// <summary>
    /// 获取或设置 deleted。
    /// </summary>
    public bool deleted { get; set; }
}
