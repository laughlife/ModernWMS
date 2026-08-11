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
    /// <summary>Commodity cost.</summary>
    public decimal cost { get; set; }
    /// <summary>Distinct ownership pairs.</summary>
    public List<CommodityOwnershipViewModel> ownerships { get; set; } = new();
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
