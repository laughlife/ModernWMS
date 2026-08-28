namespace ModernWMS.WMS.Entities.ViewModels.PackingTask;

/// <summary>
/// 查询装箱任务明细可选择的库存列表。
/// 默认只返回创建人自己的库存；search_others=true 时按条件搜索其他人的库存。
/// </summary>
public class PackingTaskStockPageRequest
{
    /// <summary>
    /// 获取或设置 sellfox_task_id。
    /// </summary>
    public long sellfox_task_id { get; set; }
    /// <summary>
    /// 获取或设置 sellfox_item_id。
    /// </summary>
    public long sellfox_item_id { get; set; }
    /// <summary>
    /// 获取或设置 page_index。
    /// </summary>
    public int page_index { get; set; } = 1;
    /// <summary>
    /// 获取或设置 page_size。
    /// </summary>
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
    /// <summary>
    /// 获取或设置 sellfox_task_id。
    /// </summary>
    public long sellfox_task_id { get; set; }
    /// <summary>
    /// 获取或设置 sellfox_item_id。
    /// </summary>
    public long sellfox_item_id { get; set; }
    /// <summary>
    /// 获取或设置 stock_id。
    /// </summary>
    public long stock_id { get; set; }
    /// <summary>
    /// 获取或设置 erp_stock_id。
    /// </summary>
    public long? erp_stock_id { get; set; }
    /// <summary>
    /// 获取或设置 stock_allocation_id。
    /// </summary>
    public long? stock_allocation_id { get; set; }
    /// <summary>
    /// 获取或设置 qty。
    /// </summary>
    public int qty { get; set; }
    /// <summary>变体数量；服务端按赛狐当前任务量重新计算锁定数量。</summary>
    public int variant { get; set; }

    /// <summary>ERP 计划的乐观锁版本，所有写命令必填。</summary>
    public long row_version { get; set; }

    /// <summary>写命令幂等标识，所有写命令必填。</summary>
    public string request_id { get; set; } = string.Empty;

    /// <summary>货主贡献维度；不再通过物理库位或批次绑定库存。</summary>
    public int goods_owner_id { get; set; }

    /// <summary>人工确认 SKU 不匹配的事实。</summary>
    public bool sku_mismatch_confirmed { get; set; }
    /// <summary>WMS 服务端签发并等待三秒后的 SKU 不匹配确认挑战。</summary>
    public string sku_mismatch_challenge { get; set; } = string.Empty;
}

public class PackingTaskSkuMismatchChallengeRequest
{
    public long sellfox_task_id { get; set; }
    public long sellfox_item_id { get; set; }
    public long stock_id { get; set; }
    public int goods_owner_id { get; set; }
    public int qty { get; set; }
    public int variant { get; set; }
    /// <summary>绑定完整的冻结命令；任一命令参数改变时必须重新确认。</summary>
    public string request_id { get; set; } = string.Empty;
}

/// <summary>
/// 可选择的库存行。
/// </summary>
public class SelectableStockViewModel
{
    /// <summary>
    /// 获取或设置 stock_id。
    /// </summary>
    public long stock_id { get; set; }
    /// <summary>
    /// 获取或设置 erp_stock_id。
    /// </summary>
    public long? erp_stock_id { get; set; }
    /// <summary>
    /// 获取或设置 stock_allocation_id。
    /// </summary>
    public long? stock_allocation_id { get; set; }
    /// <summary>
    /// 获取或设置 sku_id。
    /// </summary>
    public int sku_id { get; set; }
    /// <summary>
    /// 获取或设置 sku_code。
    /// </summary>
    public string sku_code { get; set; } = string.Empty;
    /// <summary>
    /// 获取或设置 spu_code。
    /// </summary>
    public string spu_code { get; set; } = string.Empty;
    /// <summary>
    /// 获取或设置 commodity_name。
    /// </summary>
    public string commodity_name { get; set; } = string.Empty;
    /// <summary>
    /// 获取或设置 main_image。
    /// </summary>
    public string main_image { get; set; } = string.Empty;
    /// <summary>
    /// 获取或设置 goods_location_id。
    /// </summary>
    public int? goods_location_id { get; set; }
    /// <summary>
    /// 获取或设置 location_name。
    /// </summary>
    public string location_name { get; set; } = string.Empty;
    /// <summary>
    /// 获取或设置 warehouse_id。
    /// </summary>
    public int warehouse_id { get; set; }
    /// <summary>
    /// 获取或设置 warehouse_name。
    /// </summary>
    public string warehouse_name { get; set; } = string.Empty;
    /// <summary>
    /// 获取或设置 goods_owner_id。
    /// </summary>
    public int goods_owner_id { get; set; }
    /// <summary>
    /// 获取或设置 goods_owner_name。
    /// </summary>
    public string goods_owner_name { get; set; } = string.Empty;
    /// <summary>
    /// 获取或设置 qty。
    /// </summary>
    public int qty { get; set; }
    /// <summary>
    /// 获取或设置 available_qty。
    /// </summary>
    public int available_qty { get; set; }
    /// <summary>
    /// 获取或设置 series_number。
    /// </summary>
    public string series_number { get; set; } = string.Empty;
    /// <summary>
    /// 获取或设置 expiry_date。
    /// </summary>
    public DateTime? expiry_date { get; set; }
    /// <summary>
    /// 获取或设置 matched。
    /// </summary>
    public bool matched { get; set; }
    /// <summary>
    /// 获取或设置 selected。
    /// </summary>
    public bool selected { get; set; }

    /// <summary>
    /// 该库存行已选择的锁定数量（即已维护的变体数）。未选择时为 0。
    /// </summary>
    public int selected_qty { get; set; }

    /// <summary>
    /// 是否属于装箱任务创建人自己的库存（所属人名称包含创建人名称）。
    /// 创建人库存选择时不弹确认框；他人库存选择时前端弹确认框且后端记录日志。
    /// </summary>
    public bool is_creator_stock { get; set; }

    /// <summary>ERP 计划返回的行版本。</summary>
    public long row_version { get; set; }

    /// <summary>当前操作人是否可管理该货主贡献。</summary>
    public bool can_manage { get; set; }
}
