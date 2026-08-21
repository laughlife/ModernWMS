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
    /// <summary>Tenant owning the selection.</summary>
    public long tenant_id { get; set; } = 1;
    /// <summary>SellFox packing task identifier.</summary>
    public long sellfox_task_id { get; set; }
    /// <summary>SellFox task-item identifier.</summary>
    public long sellfox_item_id { get; set; }
    /// <summary>WMS SKU selected for the task item.</summary>
    public int wms_sku_id { get; set; }
    /// <summary>WMS stock row selected for allocation.</summary>
    public int stock_id { get; set; }
    /// <summary>Mapped ERP stock identifier.</summary>
    public long? erp_stock_id { get; set; }
    /// <summary>Stock-allocation identifier, when allocated.</summary>
    public long? stock_allocation_id { get; set; }
    /// <summary>Reservation identifier, when reserved.</summary>
    public long? reservation_id { get; set; }
    /// <summary>Reservation-item identifier, when reserved.</summary>
    public long? reservation_item_id { get; set; }
    /// <summary>Quantity selected from the stock row.</summary>
    public int qty { get; set; }
    /// <summary>Goods-location identifier for the selected stock.</summary>
    public int? goods_location_id { get; set; }
    /// <summary>Goods-owner identifier for the selected stock.</summary>
    public int goods_owner_id { get; set; }
    /// <summary>SKU code stored with the selection snapshot.</summary>
    [MaxLength(64)] public string sku_code { get; set; } = string.Empty;
    /// <summary>Identifier of the user who selected the stock.</summary>
    public long selected_by { get; set; }
    /// <summary>Name of the user who selected the stock.</summary>
    [MaxLength(128)] public string selected_by_name { get; set; } = string.Empty;
    /// <summary>Time when the selection was created.</summary>
    public DateTime create_time { get; set; }
    /// <summary>Time when the selection was last updated.</summary>
    public DateTime last_update_time { get; set; }
}
