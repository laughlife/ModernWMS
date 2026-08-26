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
    /// <summary>Selection still owns the current stock binding.</summary>
    public const string ActiveStatus = "ACTIVE";
    /// <summary>Selection was cancelled and its reservation was released.</summary>
    public const string CancelledStatus = "CANCELLED";
    /// <summary>Selection was transferred into a completed picking allocation.</summary>
    public const string TransferredStatus = "TRANSFERRED";

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
    /// <summary>Lifecycle status of the binding.</summary>
    [MaxLength(16)] public string status { get; set; } = ActiveStatus;
    /// <summary>Identifier of the user who cancelled the binding.</summary>
    public long? cancelled_by { get; set; }
    /// <summary>Name of the user who cancelled the binding.</summary>
    [MaxLength(128)] public string? cancelled_by_name { get; set; }
    /// <summary>Time when the binding was cancelled.</summary>
    public DateTime? cancelled_at { get; set; }
    /// <summary>Reason why the binding was cancelled.</summary>
    [MaxLength(255)] public string? cancel_reason { get; set; }
    /// <summary>Optimistic lifecycle version.</summary>
    public long row_version { get; set; }
    /// <summary>System operation that last changed the lifecycle.</summary>
    [MaxLength(32)] public string operation_source { get; set; } = "MODERN_WMS";

    /// <summary>Whether this row currently owns a stock binding.</summary>
    [NotMapped]
    public bool IsActive => string.Equals(status, ActiveStatus, StringComparison.Ordinal);

    /// <summary>Marks an active binding as cancelled while retaining its audit trail.</summary>
    public void Cancel(long actorId, string actorName, string reason, string operationSource, DateTime cancelledAt)
    {
        if (!IsActive) throw new InvalidOperationException("Only an active stock selection can be cancelled.");
        status = CancelledStatus;
        cancelled_by = actorId;
        cancelled_by_name = actorName;
        cancelled_at = cancelledAt;
        cancel_reason = reason;
        operation_source = operationSource;
        last_update_time = cancelledAt;
        row_version = checked(row_version + 1);
    }

    /// <summary>Marks an active binding as transferred into picking.</summary>
    public void Transfer(string operationSource, DateTime transferredAt)
    {
        if (!IsActive) throw new InvalidOperationException("Only an active stock selection can be transferred.");
        status = TransferredStatus;
        operation_source = operationSource;
        last_update_time = transferredAt;
        row_version = checked(row_version + 1);
    }
}
