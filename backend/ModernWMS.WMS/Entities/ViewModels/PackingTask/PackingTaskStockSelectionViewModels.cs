namespace ModernWMS.WMS.Entities.ViewModels.PackingTask;

/// <summary>Queries task-owner stock in the task's ERP warehouse.</summary>
public sealed class PackingTaskStockPageRequest
{
    /// <summary>Sellfox task identifier.</summary>
    public long sellfox_task_id { get; set; }
    /// <summary>Sellfox task-item identifier.</summary>
    public long sellfox_item_id { get; set; }
    /// <summary>One-based page number.</summary>
    public int page_index { get; set; } = 1;
    /// <summary>Page size.</summary>
    public int page_size { get; set; } = 20;
    /// <summary>Optional product/SKU keyword.</summary>
    public string keyword { get; set; } = string.Empty;
}

/// <summary>Binds one task item to an ERP stock row.</summary>
public sealed class PackingTaskStockSelectRequest
{
    /// <summary>Sellfox task identifier.</summary>
    public long sellfox_task_id { get; set; }
    /// <summary>Sellfox task-item identifier.</summary>
    public long sellfox_item_id { get; set; }
    /// <summary>Authoritative <c>trk_stock.id</c>.</summary>
    public long erp_stock_id { get; set; }
    /// <summary>Variant multiplier; locked quantity is current task quantity times this value.</summary>
    public int variant { get; set; }
}

/// <summary>A directly selectable <c>trk_stock</c> row.</summary>
public sealed class SelectableStockViewModel
{
    /// <summary>Authoritative <c>trk_stock.id</c>.</summary>
    public long erp_stock_id { get; set; }
    /// <summary>ERP commodity identifier.</summary>
    public long? commodity_id { get; set; }
    /// <summary>ERP commodity SKU snapshot.</summary>
    public string sku_code { get; set; } = string.Empty;
    /// <summary>ERP commodity name snapshot.</summary>
    public string commodity_name { get; set; } = string.Empty;
    /// <summary>Commodity image URL when available.</summary>
    public string main_image { get; set; } = string.Empty;
    /// <summary>ERP warehouse identifier.</summary>
    public long warehouse_id { get; set; }
    /// <summary>Task warehouse name snapshot.</summary>
    public string warehouse_name { get; set; } = string.Empty;
    /// <summary>Task creator's unique system user identifier.</summary>
    public long order_user_id { get; set; }
    /// <summary>Task creator name snapshot.</summary>
    public string order_user_name { get; set; } = string.Empty;
    /// <summary>ERP available quantity; inventory debt may be negative.</summary>
    public long available_qty { get; set; }
    /// <summary>ERP occupied quantity.</summary>
    public long occupied_qty { get; set; }
    /// <summary>ERP total quantity; inventory debt may be negative.</summary>
    public long total_qty { get; set; }
    /// <summary>Whether the stock commodity matches the task item.</summary>
    public bool matched { get; set; }
    /// <summary>Whether this row is the active selection.</summary>
    public bool selected { get; set; }
    /// <summary>Quantity held by the active selection.</summary>
    public long selected_qty { get; set; }
}
