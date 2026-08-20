namespace ModernWMS.WMS.Entities.ViewModels;

/// <summary>
/// Read-only packing-task row shown in the first delivery-management tab.
/// </summary>
public class PackingTaskQueryViewModel
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
    public List<PackingTaskQueryItemViewModel> item_list { get; set; } = [];
}

/// <summary>
/// Read-only SellFox product snapshot belonging to one packing task.
/// Nullable quantities preserve missing source values.
/// </summary>
public class PackingTaskQueryItemViewModel
{
    public long id { get; set; }
    public long sellfox_item_id { get; set; }
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
