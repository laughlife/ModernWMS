using System.ComponentModel.DataAnnotations.Schema;

namespace ModernWMS.Core.DBContext.Entities;

/// <summary>
/// SellFox packing-task header synchronized by the ERP job executor.
/// This mapping is read-only from ModernWMS.
/// </summary>
[Table("ruiyi_sellfox_packing_task")]
public class ErpPackingTaskEntity
{
    public long id { get; set; }
    public long sellfox_task_id { get; set; }
    public string packing_task_sn { get; set; } = string.Empty;
    public long? warehouse_id { get; set; }
    public string? warehouse_name { get; set; }
    public int? complete_num { get; set; }
    public int? task_num { get; set; }
    public string? create_name { get; set; }
    public DateTime? source_create_time { get; set; }
    public int? item_count { get; set; }
    public string? shop_name { get; set; }
    public string? marketplace_name { get; set; }
    public bool source_canceled { get; set; }
    public bool source_deleted { get; set; }
}

/// <summary>
/// SellFox packing-task item synchronized by the ERP job executor.
/// This mapping is read-only from ModernWMS.
/// </summary>
[Table("ruiyi_sellfox_packing_task_item")]
public class ErpPackingTaskItemEntity
{
    public long id { get; set; }
    public long sellfox_item_id { get; set; }
    public long sellfox_task_id { get; set; }
    public long? commodity_id { get; set; }
    public string? commodity_sku { get; set; }
    public string? commodity_name { get; set; }
    public string? main_image { get; set; }
    public string? fn_sku { get; set; }
    public string? sku { get; set; }
    public string? msku { get; set; }
    public int? task_num { get; set; }
    public int? quantity_shipped { get; set; }
    public int? stock_available { get; set; }
    public bool source_deleted { get; set; }
}
