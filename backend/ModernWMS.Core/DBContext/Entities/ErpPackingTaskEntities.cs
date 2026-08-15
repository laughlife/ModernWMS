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
    public int? source_status { get; set; }
    public string? batch_sns_json { get; set; }
    public string? order_sn { get; set; }
    public int? complete_num { get; set; }
    public int? task_num { get; set; }
    public string? create_name { get; set; }
    public DateTime? source_create_time { get; set; }
    public DateTime? source_complete_time { get; set; }
    public string? remark { get; set; }
    public int? carton_num { get; set; }
    public int? carton_web_type { get; set; }
    public int? item_count { get; set; }
    public long? shop_id { get; set; }
    public string? shop_name { get; set; }
    public string? marketplace_name { get; set; }
    public string? print_time { get; set; }
    public long? fba_logistic_id { get; set; }
    public string? fba_logistic_name { get; set; }
    public bool expediting { get; set; }
    public string? cartons_json { get; set; }
    public string? files_json { get; set; }
    public string? target_order_sn_json { get; set; }
    public string raw_json { get; set; } = string.Empty;
    public string source_hash { get; set; } = string.Empty;
    public bool source_canceled { get; set; }
    public DateTime? source_canceled_at { get; set; }
    public bool source_deleted { get; set; }
    public DateTime? source_deleted_at { get; set; }
    public string? last_seen_run_id { get; set; }
    public DateTime? last_seen_at { get; set; }
    public DateTime? first_missing_at { get; set; }
    public int missing_count { get; set; }
    public DateTime first_sync_time { get; set; }
    public DateTime last_sync_time { get; set; }
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
    public long? shop_id { get; set; }
    public string? shop_name { get; set; }
    public int? task_num { get; set; }
    public int? quantity_shipped { get; set; }
    public int? stock_available { get; set; }
    public int? stock_wait { get; set; }
    public int? wait_up_shelf_num { get; set; }
    public int? stock_processing { get; set; }
    public bool is_group { get; set; }
    public string? platform_name { get; set; }
    public long? shop_auth_id { get; set; }
    public string? fn_sku_list_json { get; set; }
    public string? child_skus_json { get; set; }
    public string raw_json { get; set; } = string.Empty;
    public string source_hash { get; set; } = string.Empty;
    public bool source_deleted { get; set; }
    public DateTime? source_deleted_at { get; set; }
    public string? last_seen_run_id { get; set; }
    public DateTime first_sync_time { get; set; }
    public DateTime last_sync_time { get; set; }
}
