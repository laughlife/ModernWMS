using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using ModernWMS.Core.Models;

namespace ModernWMS.WMS.Entities.Models.PackingTask;

/// <summary>
/// WMS 自有绑定表：装箱任务明细手动选择的库存。
/// 一条记录表示「某个装箱任务明细行」选择了「某个库存行」，并锁定对应数量。
/// 表名统一加 wms_ 前缀：wms_packing_task_stock_selection。
/// </summary>
[Table("packing_task_stock_selection")]
public class PackingTaskStockSelectionEntity : BaseModel
{
    public long tenant_id { get; set; } = 1;
    public long sellfox_task_id { get; set; }
    public long sellfox_item_id { get; set; }
    public int wms_sku_id { get; set; }
    public int stock_id { get; set; }
    public long? erp_stock_id { get; set; }
    public long? stock_allocation_id { get; set; }
    public long? reservation_id { get; set; }
    public long? reservation_item_id { get; set; }
    public int qty { get; set; }
    public int goods_location_id { get; set; }
    public int goods_owner_id { get; set; }
    [MaxLength(64)] public string sku_code { get; set; } = string.Empty;
    public long selected_by { get; set; }
    [MaxLength(128)] public string selected_by_name { get; set; } = string.Empty;
    public DateTime create_time { get; set; }
    public DateTime last_update_time { get; set; }
}
