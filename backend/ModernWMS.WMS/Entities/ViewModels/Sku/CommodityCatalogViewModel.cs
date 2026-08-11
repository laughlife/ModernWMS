namespace ModernWMS.WMS.Entities.ViewModels;

/// <summary>
/// Read-only commodity catalog row.
/// </summary>
public class CommodityCatalogViewModel
{
    /// <summary>WMS SKU id.</summary>
    public int sku_id { get; set; }
    /// <summary>SKU code.</summary>
    public string sku_code { get; set; } = string.Empty;
    /// <summary>Commodity name.</summary>
    public string sku_name { get; set; } = string.Empty;
    /// <summary>ERP product image URL.</summary>
    public string product_image { get; set; } = string.Empty;
    /// <summary>Commodity volume normalized to cubic centimeters.</summary>
    public decimal volume_cm3 { get; set; }
    /// <summary>Total received quantity across purchase batches.</summary>
    public long total_qty { get; set; }
    /// <summary>Received purchase batches.</summary>
    public List<CommodityCostBatchViewModel> cost_batches { get; set; } = new();
    /// <summary>Total purchase value across received batches.</summary>
    public decimal total_value { get; set; }
    /// <summary>Distinct ownership pairs.</summary>
    public List<CommodityOwnershipViewModel> ownerships { get; set; } = new();
}

/// <summary>
/// One received purchase-price batch for a commodity.
/// </summary>
public class CommodityCostBatchViewModel
{
    /// <summary>Receipt date of this batch.</summary>
    public DateTime batch_date { get; set; }
    /// <summary>Actual purchaser name.</summary>
    public string purchaser_name { get; set; } = string.Empty;
    /// <summary>Purchase unit price in RMB.</summary>
    public decimal unit_cost { get; set; }
    /// <summary>Received quantity in this batch.</summary>
    public long quantity { get; set; }
}

/// <summary>
/// One distinct operator-group and owner pair associated with a commodity.
/// </summary>
public class CommodityOwnershipViewModel
{
    /// <summary>Operator group name.</summary>
    public string dept_name { get; set; } = string.Empty;
    /// <summary>Owner name.</summary>
    public string order_user_name { get; set; } = string.Empty;
}
