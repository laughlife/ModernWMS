namespace ModernWMS.WMS.Entities.ViewModels;

/// <summary>
/// One Shenzhen self-operated warehouse FBA shipment prepared by ERP.
/// </summary>
public class FbaShipmentViewModel
{
    public long stock_move_id { get; set; }
    public string stock_move_no { get; set; } = string.Empty;
    public long fba_shipment_id { get; set; }
    public string fba_no { get; set; } = string.Empty;
    public string shipment_name { get; set; } = string.Empty;
    public string fba_status { get; set; } = string.Empty;
    public string fulfillment_center_id { get; set; } = string.Empty;
    public string shop_name { get; set; } = string.Empty;
    public string marketplace_name { get; set; } = string.Empty;
    public string shipping_mode { get; set; } = string.Empty;
    public string shipping_solution { get; set; } = string.Empty;
    public long? dept_id { get; set; }
    public string dept_name { get; set; } = string.Empty;
    public long? order_user_id { get; set; }
    public string order_user_name { get; set; } = string.Empty;
    public long from_warehouse_id { get; set; }
    public string from_warehouse_name { get; set; } = string.Empty;
    public long? freight_forwarder_id { get; set; }
    public string freight_forwarder_name { get; set; } = string.Empty;
    public string logistics_name { get; set; } = string.Empty;
    public string primary_tracking_no { get; set; } = string.Empty;
    public int product_count { get; set; }
    public long shipment_total_qty { get; set; }
    public long locked_qty { get; set; }
    public List<string> tracking_numbers { get; set; } = [];
    public bool inventory_ready { get; set; }
    public string inventory_status_name { get; set; } = string.Empty;
    public DateTime prepared_time { get; set; }
    public DateTime source_update_time { get; set; }
    public List<FbaShipmentItemViewModel> item_list { get; set; } = [];
}

/// <summary>
/// Product and stock verification details for an ERP FBA preparation item.
/// </summary>
public class FbaShipmentItemViewModel
{
    public long stock_move_item_id { get; set; }
    public long? stock_id { get; set; }
    public long? commodity_id { get; set; }
    public long? fba_shipment_item_id { get; set; }
    public string main_image { get; set; } = string.Empty;
    public string commodity_name { get; set; } = string.Empty;
    public string stock_sku { get; set; } = string.Empty;
    public string fba_sku { get; set; } = string.Empty;
    public long qty { get; set; }
    public long variant_qty { get; set; }
    public long shipment_total_qty { get; set; }
    public bool sku_matched { get; set; }
    public bool sku_mismatch_confirmed { get; set; }
    public long stock_available_qty { get; set; }
    public long stock_occupied_qty { get; set; }
    public long stock_total_qty { get; set; }
    public bool inventory_ready { get; set; }
}
