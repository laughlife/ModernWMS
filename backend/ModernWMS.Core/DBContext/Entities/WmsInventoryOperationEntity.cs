using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ModernWMS.Core.DBContext.Entities;

/// <summary>
/// Globally idempotent inventory mutation command header.
/// ERP stock and allocation identifiers are logical references without physical foreign keys.
/// </summary>
[Table("wms_inventory_operation")]
public class WmsInventoryOperationEntity
{
    /// <summary>
    /// 获取或设置 id。
    /// </summary>
    [Key]
    public long id { get; set; }


    /// <summary>
    /// 获取或设置 operation_key。
    /// </summary>
    [MaxLength(64)]
    public string operation_key { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置 shared_command_id。
    /// </summary>
    public long? shared_command_id { get; set; }
    /// <summary>
    /// 获取或设置 reservation_id。
    /// </summary>
    public long? reservation_id { get; set; }
    /// <summary>
    /// 获取或设置 reservation_item_id。
    /// </summary>
    public long? reservation_item_id { get; set; }

    /// <summary>
    /// 获取或设置 biz_type。
    /// </summary>
    [MaxLength(32)]
    public string biz_type { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置 biz_id。
    /// </summary>
    public long biz_id { get; set; }
    /// <summary>
    /// 获取或设置 biz_item_id。
    /// </summary>
    public long biz_item_id { get; set; }

    /// <summary>
    /// 获取或设置 mutation_type。
    /// </summary>
    [MaxLength(32)]
    public string mutation_type { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置 erp_stock_id。
    /// </summary>
    public long erp_stock_id { get; set; }
    /// <summary>
    /// 获取或设置 allocation_id。
    /// </summary>
    public long allocation_id { get; set; }
    /// <summary>
    /// 获取或设置 counterpart_allocation_id。
    /// </summary>
    public long? counterpart_allocation_id { get; set; }
    /// <summary>
    /// 获取或设置 quantity。
    /// </summary>
    public long quantity { get; set; }

    /// <summary>
    /// 获取或设置 @operator。
    /// </summary>
    [MaxLength(64)]
    public string @operator { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置 result_status。
    /// </summary>
    [MaxLength(16)]
    public string result_status { get; set; } = "PENDING";

    /// <summary>
    /// 获取或设置 erp_stock_record_id。
    /// </summary>
    public long? erp_stock_record_id { get; set; }
    /// <summary>
    /// 获取或设置 create_time。
    /// </summary>
    public DateTime create_time { get; set; }
    /// <summary>
    /// 获取或设置 update_time。
    /// </summary>
    public DateTime update_time { get; set; }
}
