namespace ModernWMS.WMS.Entities.ViewModels.PackingTask;

/// <summary>
/// 查询装箱任务明细可选择的库存列表。
/// </summary>
public class PackingTaskStockPageRequest
{
    public long sellfox_task_id { get; set; }
    public long sellfox_item_id { get; set; }
    public int page_index { get; set; } = 1;
    public int page_size { get; set; } = 20;
}

/// <summary>
/// 保存装箱任务明细对某个库存行的选择。
/// </summary>
public class PackingTaskStockSelectRequest
{
    public long sellfox_task_id { get; set; }
    public long sellfox_item_id { get; set; }
    public int stock_id { get; set; }
    public int qty { get; set; }
}

/// <summary>
/// 可选择的库存行。
/// </summary>
public class SelectableStockViewModel
{
    public int stock_id { get; set; }
    public int sku_id { get; set; }
    public string sku_code { get; set; } = string.Empty;
    public string spu_code { get; set; } = string.Empty;
    public string commodity_name { get; set; } = string.Empty;
    public string main_image { get; set; } = string.Empty;
    public int goods_location_id { get; set; }
    public string location_name { get; set; } = string.Empty;
    public int warehouse_id { get; set; }
    public string warehouse_name { get; set; } = string.Empty;
    public int goods_owner_id { get; set; }
    public string goods_owner_name { get; set; } = string.Empty;
    public int qty { get; set; }
    public int available_qty { get; set; }
    public string series_number { get; set; } = string.Empty;
    public DateTime? expiry_date { get; set; }
    public bool matched { get; set; }
    public bool selected { get; set; }
}
