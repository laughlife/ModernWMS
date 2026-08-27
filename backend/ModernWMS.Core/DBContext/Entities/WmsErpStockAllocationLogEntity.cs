using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ModernWMS.Core.DBContext.Entities;

/// <summary>
/// Audit trail for ERP stock location allocation changes.
/// This table is not an inventory balance ledger.
/// </summary>
[Table("wms_erp_stock_allocation_log")]
public class WmsErpStockAllocationLogEntity
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
    /// 获取或设置 event_type。
    /// </summary>
    [MaxLength(32)]
    public string event_type { get; set; } = string.Empty;

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
    /// 获取或设置 erp_stock_record_id。
    /// </summary>
    public long? erp_stock_record_id { get; set; }
    /// <summary>
    /// 获取或设置 allocated_delta。
    /// </summary>
    public long allocated_delta { get; set; }
    /// <summary>
    /// 获取或设置 occupied_delta。
    /// </summary>
    public long occupied_delta { get; set; }
    /// <summary>
    /// 获取或设置 before_allocated_qty。
    /// </summary>
    public long before_allocated_qty { get; set; }
    /// <summary>
    /// 获取或设置 after_allocated_qty。
    /// </summary>
    public long after_allocated_qty { get; set; }
    /// <summary>
    /// 获取或设置 before_occupied_qty。
    /// </summary>
    public long before_occupied_qty { get; set; }
    /// <summary>
    /// 获取或设置 after_occupied_qty。
    /// </summary>
    public long after_occupied_qty { get; set; }

    /// <summary>
    /// 获取或设置 @operator。
    /// </summary>
    [MaxLength(128)]
    public string @operator { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置 operate_time。
    /// </summary>
    public DateTime operate_time { get; set; }

    /// <summary>
    /// 获取或设置 remark。
    /// </summary>
    [MaxLength(500)]
    public string remark { get; set; } = string.Empty;
}
