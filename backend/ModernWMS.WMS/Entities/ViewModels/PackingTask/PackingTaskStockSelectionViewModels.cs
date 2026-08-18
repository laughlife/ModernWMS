namespace ModernWMS.WMS.Entities.ViewModels.PackingTask;

/// <summary>
/// 查询装箱任务明细可选择的库存列表。
/// 默认只返回创建人自己的库存；search_others=true 时按条件搜索其他人的库存。
/// </summary>
public class PackingTaskStockPageRequest
{
    public long sellfox_task_id { get; set; }
    public long sellfox_item_id { get; set; }
    public int page_index { get; set; } = 1;
    public int page_size { get; set; } = 20;

    /// <summary>
    /// 是否搜索其他人的库存；false（默认）只返回创建人自己的库存。
    /// </summary>
    public bool search_others { get; set; }

    /// <summary>搜索条件：SKU/商品名称（模糊匹配）。</summary>
    public string keyword { get; set; } = string.Empty;

    /// <summary>搜索条件：库位（模糊匹配）。</summary>
    public string location { get; set; } = string.Empty;

    /// <summary>搜索条件：所属人（模糊匹配）。</summary>
    public string owner { get; set; } = string.Empty;
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

    /// <summary>
    /// 是否属于装箱任务创建人自己的库存（所属人名称包含创建人名称）。
    /// 创建人库存选择时不弹确认框；他人库存选择时前端弹确认框且后端记录日志。
    /// </summary>
    public bool is_creator_stock { get; set; }
}
