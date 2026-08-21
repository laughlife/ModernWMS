namespace ModernWMS.WMS.Entities.ViewModels;

/// <summary>
/// Read-only packing-task row shown in the first delivery-management tab.
/// </summary>
public class PackingTaskQueryViewModel
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
    /// 获取或设置 item_count。
    /// </summary>
    public int? item_count { get; set; }
    /// <summary>
    /// 获取或设置 shop_name。
    /// </summary>
    public string? shop_name { get; set; }
    /// <summary>
    /// 获取或设置 marketplace_name。
    /// </summary>
    public string? marketplace_name { get; set; }
    /// <summary>
    /// 获取或设置 item_list。
    /// </summary>
    public List<PackingTaskQueryItemViewModel> item_list { get; set; } = [];
}

/// <summary>
/// Read-only SellFox product snapshot belonging to one packing task.
/// Nullable quantities preserve missing source values.
/// </summary>
public class PackingTaskQueryItemViewModel
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
    /// WMS 基础 SKU 编码（去掉 -1/-2 等变体后缀后的前缀），用于「库存xxxx:数量」参考值展示。
    /// </summary>
    public string? stock_sku_code { get; set; }

    /// <summary>当前仓库中装箱任务创建人名下的非冻结库存总量。</summary>
    public int? stock_qty { get; set; }

    /// <summary>
    /// 当前仓库中创建人库存扣除有效锁定后的可用量。
    /// </summary>
    public int? stock_available_qty { get; set; }

    /// <summary>
    /// 该明细已选择锁定的库存数量（wms_packing_task_stock_selection 合计）。
    /// </summary>
    public int? locked_qty { get; set; }
}
