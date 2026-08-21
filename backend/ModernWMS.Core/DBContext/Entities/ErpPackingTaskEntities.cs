using System.ComponentModel.DataAnnotations.Schema;

namespace ModernWMS.Core.DBContext.Entities;

/// <summary>
/// SellFox packing-task header synchronized by the ERP job executor.
/// This mapping is read-only from ModernWMS.
/// </summary>
[Table("ruiyi_sellfox_packing_task")]
public class ErpPackingTaskEntity
{
    /// <summary>
    /// 获取或设置 id。
    /// </summary>
    public long id { get; set; }
    /// <summary>
    /// 获取或设置 sellfox_task_id。
    /// </summary>
    public long sellfox_task_id { get; set; }
    /// <summary>
    /// 获取或设置 packing_task_sn。
    /// </summary>
    public string packing_task_sn { get; set; } = string.Empty;
    /// <summary>
    /// 获取或设置 warehouse_id。
    /// </summary>
    public long? warehouse_id { get; set; }
    /// <summary>
    /// 获取或设置 warehouse_name。
    /// </summary>
    public string? warehouse_name { get; set; }
    /// <summary>
    /// 获取或设置 source_status。
    /// </summary>
    public int? source_status { get; set; }
    /// <summary>
    /// 获取或设置 batch_sns_json。
    /// </summary>
    public string? batch_sns_json { get; set; }
    /// <summary>
    /// 获取或设置 order_sn。
    /// </summary>
    public string? order_sn { get; set; }
    /// <summary>
    /// 获取或设置 complete_num。
    /// </summary>
    public int? complete_num { get; set; }
    /// <summary>
    /// 获取或设置 task_num。
    /// </summary>
    public int? task_num { get; set; }
    /// <summary>
    /// 获取或设置 create_name。
    /// </summary>
    public string? create_name { get; set; }
    /// <summary>
    /// 获取或设置 source_create_time。
    /// </summary>
    public DateTime? source_create_time { get; set; }
    /// <summary>
    /// 获取或设置 source_complete_time。
    /// </summary>
    public DateTime? source_complete_time { get; set; }
    /// <summary>
    /// 获取或设置 remark。
    /// </summary>
    public string? remark { get; set; }
    /// <summary>
    /// 获取或设置 carton_num。
    /// </summary>
    public int? carton_num { get; set; }
    /// <summary>
    /// 获取或设置 carton_web_type。
    /// </summary>
    public int? carton_web_type { get; set; }
    /// <summary>
    /// 获取或设置 item_count。
    /// </summary>
    public int? item_count { get; set; }
    /// <summary>
    /// 获取或设置 shop_id。
    /// </summary>
    public long? shop_id { get; set; }
    /// <summary>
    /// 获取或设置 shop_name。
    /// </summary>
    public string? shop_name { get; set; }
    /// <summary>
    /// 获取或设置 marketplace_name。
    /// </summary>
    public string? marketplace_name { get; set; }
    /// <summary>
    /// 获取或设置 print_time。
    /// </summary>
    public string? print_time { get; set; }
    /// <summary>
    /// 获取或设置 fba_logistic_id。
    /// </summary>
    public long? fba_logistic_id { get; set; }
    /// <summary>
    /// 获取或设置 fba_logistic_name。
    /// </summary>
    public string? fba_logistic_name { get; set; }
    /// <summary>
    /// 获取或设置 expediting。
    /// </summary>
    public bool expediting { get; set; }
    /// <summary>
    /// 获取或设置 cartons_json。
    /// </summary>
    public string? cartons_json { get; set; }
    /// <summary>
    /// 获取或设置 files_json。
    /// </summary>
    public string? files_json { get; set; }
    /// <summary>
    /// 获取或设置 target_order_sn_json。
    /// </summary>
    public string? target_order_sn_json { get; set; }
    /// <summary>
    /// 获取或设置 raw_json。
    /// </summary>
    public string raw_json { get; set; } = string.Empty;
    /// <summary>
    /// 获取或设置 source_hash。
    /// </summary>
    public string source_hash { get; set; } = string.Empty;
    /// <summary>
    /// 获取或设置 source_canceled。
    /// </summary>
    public bool source_canceled { get; set; }
    /// <summary>
    /// 获取或设置 source_canceled_at。
    /// </summary>
    public DateTime? source_canceled_at { get; set; }
    /// <summary>
    /// 获取或设置 source_deleted。
    /// </summary>
    public bool source_deleted { get; set; }
    /// <summary>
    /// 获取或设置 source_deleted_at。
    /// </summary>
    public DateTime? source_deleted_at { get; set; }
    /// <summary>
    /// 获取或设置 last_seen_run_id。
    /// </summary>
    public string? last_seen_run_id { get; set; }
    /// <summary>
    /// 获取或设置 last_seen_at。
    /// </summary>
    public DateTime? last_seen_at { get; set; }
    /// <summary>
    /// 获取或设置 first_missing_at。
    /// </summary>
    public DateTime? first_missing_at { get; set; }
    /// <summary>
    /// 获取或设置 missing_count。
    /// </summary>
    public int missing_count { get; set; }
    /// <summary>
    /// 获取或设置 first_sync_time。
    /// </summary>
    public DateTime first_sync_time { get; set; }
    /// <summary>
    /// 获取或设置 last_sync_time。
    /// </summary>
    public DateTime last_sync_time { get; set; }
}

/// <summary>
/// SellFox packing-task item synchronized by the ERP job executor.
/// This mapping is read-only from ModernWMS.
/// </summary>
[Table("ruiyi_sellfox_packing_task_item")]
public class ErpPackingTaskItemEntity
{
    /// <summary>
    /// 获取或设置 id。
    /// </summary>
    public long id { get; set; }
    /// <summary>
    /// 获取或设置 sellfox_item_id。
    /// </summary>
    public long sellfox_item_id { get; set; }
    /// <summary>
    /// 获取或设置 sellfox_task_id。
    /// </summary>
    public long sellfox_task_id { get; set; }
    /// <summary>
    /// 获取或设置 commodity_id。
    /// </summary>
    public long? commodity_id { get; set; }
    /// <summary>
    /// 获取或设置 commodity_sku。
    /// </summary>
    public string? commodity_sku { get; set; }
    /// <summary>
    /// 获取或设置 commodity_name。
    /// </summary>
    public string? commodity_name { get; set; }
    /// <summary>
    /// 获取或设置 main_image。
    /// </summary>
    public string? main_image { get; set; }
    /// <summary>
    /// 获取或设置 fn_sku。
    /// </summary>
    public string? fn_sku { get; set; }
    /// <summary>
    /// 获取或设置 sku。
    /// </summary>
    public string? sku { get; set; }
    /// <summary>
    /// 获取或设置 msku。
    /// </summary>
    public string? msku { get; set; }
    /// <summary>
    /// 获取或设置 shop_id。
    /// </summary>
    public long? shop_id { get; set; }
    /// <summary>
    /// 获取或设置 shop_name。
    /// </summary>
    public string? shop_name { get; set; }
    /// <summary>
    /// 获取或设置 task_num。
    /// </summary>
    public int? task_num { get; set; }
    /// <summary>
    /// 获取或设置 quantity_shipped。
    /// </summary>
    public int? quantity_shipped { get; set; }
    /// <summary>
    /// 获取或设置 stock_available。
    /// </summary>
    public int? stock_available { get; set; }
    /// <summary>
    /// 获取或设置 stock_wait。
    /// </summary>
    public int? stock_wait { get; set; }
    /// <summary>
    /// 获取或设置 wait_up_shelf_num。
    /// </summary>
    public int? wait_up_shelf_num { get; set; }
    /// <summary>
    /// 获取或设置 stock_processing。
    /// </summary>
    public int? stock_processing { get; set; }
    /// <summary>
    /// 获取或设置 is_group。
    /// </summary>
    public bool is_group { get; set; }
    /// <summary>
    /// 获取或设置 platform_name。
    /// </summary>
    public string? platform_name { get; set; }
    /// <summary>
    /// 获取或设置 shop_auth_id。
    /// </summary>
    public long? shop_auth_id { get; set; }
    /// <summary>
    /// 获取或设置 fn_sku_list_json。
    /// </summary>
    public string? fn_sku_list_json { get; set; }
    /// <summary>
    /// 获取或设置 child_skus_json。
    /// </summary>
    public string? child_skus_json { get; set; }
    /// <summary>
    /// 获取或设置 raw_json。
    /// </summary>
    public string raw_json { get; set; } = string.Empty;
    /// <summary>
    /// 获取或设置 source_hash。
    /// </summary>
    public string source_hash { get; set; } = string.Empty;
    /// <summary>
    /// 获取或设置 source_deleted。
    /// </summary>
    public bool source_deleted { get; set; }
    /// <summary>
    /// 获取或设置 source_deleted_at。
    /// </summary>
    public DateTime? source_deleted_at { get; set; }
    /// <summary>
    /// 获取或设置 last_seen_run_id。
    /// </summary>
    public string? last_seen_run_id { get; set; }
    /// <summary>
    /// 获取或设置 first_sync_time。
    /// </summary>
    public DateTime first_sync_time { get; set; }
    /// <summary>
    /// 获取或设置 last_sync_time。
    /// </summary>
    public DateTime last_sync_time { get; set; }
}
