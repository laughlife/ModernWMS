namespace ModernWMS.WMS.Entities.ViewModels;

/// <summary>
/// One Shenzhen self-operated warehouse FBA shipment prepared by ERP.
/// </summary>
public class FbaShipmentViewModel
{
    /// <summary>
    /// 获取或设置 stock_move_id。
    /// </summary>
    public long stock_move_id { get; set; }
    /// <summary>
    /// 获取或设置 stock_move_no。
    /// </summary>
    public string stock_move_no { get; set; } = string.Empty;
    /// <summary>
    /// 获取或设置 fba_shipment_id。
    /// </summary>
    public long fba_shipment_id { get; set; }
    /// <summary>
    /// 获取或设置 fba_no。
    /// </summary>
    public string fba_no { get; set; } = string.Empty;
    /// <summary>
    /// 获取或设置 shipment_name。
    /// </summary>
    public string shipment_name { get; set; } = string.Empty;
    /// <summary>
    /// 获取或设置 fba_status。
    /// </summary>
    public string fba_status { get; set; } = string.Empty;
    /// <summary>
    /// 获取或设置 fulfillment_center_id。
    /// </summary>
    public string fulfillment_center_id { get; set; } = string.Empty;
    /// <summary>
    /// 获取或设置 shop_name。
    /// </summary>
    public string shop_name { get; set; } = string.Empty;
    /// <summary>
    /// 获取或设置 marketplace_name。
    /// </summary>
    public string marketplace_name { get; set; } = string.Empty;
    /// <summary>
    /// 获取或设置 shipping_mode。
    /// </summary>
    public string shipping_mode { get; set; } = string.Empty;
    /// <summary>
    /// 获取或设置 shipping_solution。
    /// </summary>
    public string shipping_solution { get; set; } = string.Empty;
    /// <summary>
    /// 获取或设置 dept_id。
    /// </summary>
    public long? dept_id { get; set; }
    /// <summary>
    /// 获取或设置 dept_name。
    /// </summary>
    public string dept_name { get; set; } = string.Empty;
    /// <summary>
    /// 获取或设置 order_user_id。
    /// </summary>
    public long? order_user_id { get; set; }
    /// <summary>
    /// 获取或设置 order_user_name。
    /// </summary>
    public string order_user_name { get; set; } = string.Empty;
    /// <summary>
    /// 获取或设置 creator。
    /// </summary>
    public string creator { get; set; } = string.Empty;
    /// <summary>
    /// 获取或设置 from_warehouse_id。
    /// </summary>
    public long from_warehouse_id { get; set; }
    /// <summary>
    /// 获取或设置 from_warehouse_name。
    /// </summary>
    public string from_warehouse_name { get; set; } = string.Empty;
    /// <summary>
    /// 获取或设置 freight_forwarder_id。
    /// </summary>
    public long? freight_forwarder_id { get; set; }
    /// <summary>
    /// 获取或设置 freight_forwarder_name。
    /// </summary>
    public string freight_forwarder_name { get; set; } = string.Empty;
    /// <summary>
    /// 获取或设置 logistics_name。
    /// </summary>
    public string logistics_name { get; set; } = string.Empty;
    /// <summary>
    /// 获取或设置 product_count。
    /// </summary>
    public int product_count { get; set; }
    /// <summary>
    /// 获取或设置 shipment_total_qty。
    /// </summary>
    public long shipment_total_qty { get; set; }
    /// <summary>
    /// 获取或设置 locked_qty。
    /// </summary>
    public long locked_qty { get; set; }
    /// <summary>
    /// 获取或设置 inventory_ready。
    /// </summary>
    public bool inventory_ready { get; set; }
    /// <summary>
    /// 获取或设置 inventory_status_name。
    /// </summary>
    public string inventory_status_name { get; set; } = string.Empty;
    /// <summary>
    /// 获取或设置 prepared_time。
    /// </summary>
    public DateTime prepared_time { get; set; }
    /// <summary>
    /// 获取或设置 source_update_time。
    /// </summary>
    public DateTime source_update_time { get; set; }
    /// <summary>
    /// 获取或设置 item_list。
    /// </summary>
    public List<FbaShipmentItemViewModel> item_list { get; set; } = [];
}

/// <summary>
/// Product and stock verification details for an ERP FBA preparation item.
/// </summary>
public class FbaShipmentItemViewModel
{
    /// <summary>
    /// 获取或设置 stock_move_item_id。
    /// </summary>
    public long stock_move_item_id { get; set; }
    /// <summary>
    /// 获取或设置 stock_id。
    /// </summary>
    public long? stock_id { get; set; }
    /// <summary>
    /// 获取或设置 commodity_id。
    /// </summary>
    public long? commodity_id { get; set; }
    /// <summary>
    /// 获取或设置 fba_shipment_item_id。
    /// </summary>
    public long? fba_shipment_item_id { get; set; }
    /// <summary>
    /// 获取或设置 main_image。
    /// </summary>
    public string main_image { get; set; } = string.Empty;
    /// <summary>
    /// 获取或设置 commodity_name。
    /// </summary>
    public string commodity_name { get; set; } = string.Empty;
    /// <summary>
    /// 获取或设置 stock_sku。
    /// </summary>
    public string stock_sku { get; set; } = string.Empty;
    /// <summary>
    /// 获取或设置 fba_sku。
    /// </summary>
    public string fba_sku { get; set; } = string.Empty;
    /// <summary>
    /// 获取或设置 qty。
    /// </summary>
    public long qty { get; set; }
    /// <summary>
    /// 获取或设置 variant_qty。
    /// </summary>
    public long variant_qty { get; set; }
    /// <summary>
    /// 获取或设置 shipment_total_qty。
    /// </summary>
    public long shipment_total_qty { get; set; }
    /// <summary>
    /// 获取或设置 sku_matched。
    /// </summary>
    public bool sku_matched { get; set; }
    /// <summary>
    /// 获取或设置 sku_mismatch_confirmed。
    /// </summary>
    public bool sku_mismatch_confirmed { get; set; }
    /// <summary>
    /// 获取或设置 stock_available_qty。
    /// </summary>
    public long stock_available_qty { get; set; }
    /// <summary>
    /// 获取或设置 stock_occupied_qty。
    /// </summary>
    public long stock_occupied_qty { get; set; }
    /// <summary>
    /// 获取或设置 stock_total_qty。
    /// </summary>
    public long stock_total_qty { get; set; }
    /// <summary>
    /// 获取或设置 inventory_ready。
    /// </summary>
    public bool inventory_ready { get; set; }
}
