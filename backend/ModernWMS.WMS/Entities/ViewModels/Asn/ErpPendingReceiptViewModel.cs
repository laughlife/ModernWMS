namespace ModernWMS.WMS.Entities.ViewModels;

/// <summary>
/// ERP shipment waiting to be received by ModernWMS.
/// </summary>
public class ErpPendingReceiptViewModel
{
    public long id { get; set; }
    public string source_type { get; set; } = string.Empty;
    public string source_stock_move_no { get; set; } = string.Empty;
    public string purchase_no { get; set; } = string.Empty;
    public string supplier_name { get; set; } = string.Empty;
    public string order_user_text { get; set; } = string.Empty;
    public string shipment_batch_no { get; set; } = string.Empty;
    public string shipment_type { get; set; } = string.Empty;
    public long shipment_qty { get; set; }
    public DateTime? shipment_time { get; set; }
    public long warehouse_id { get; set; }
    public string warehouse_name { get; set; } = string.Empty;
    public string freight_forwarder_name { get; set; } = string.Empty;
    public string source_freight_payment_type { get; set; } = string.Empty;
    public string provider_code { get; set; } = string.Empty;
    public string logistics_code { get; set; } = string.Empty;
    public string logistics_name { get; set; } = string.Empty;
    public string tracking_no { get; set; } = string.Empty;
    public string lifecycle_status { get; set; } = string.Empty;
    public string tracking_status { get; set; } = string.Empty;
    public string tracking_status_name { get; set; } = string.Empty;
    public string latest_event_desc { get; set; } = string.Empty;
    public DateTime? latest_event_time { get; set; }
    public string latest_event_location { get; set; } = string.Empty;
    public DateTime? estimated_delivery_time { get; set; }
    public DateTime? actual_delivery_time { get; set; }
    public int source_version { get; set; }
    public string product_summary { get; set; } = string.Empty;
    public int product_count { get; set; }
    public List<ErpPendingReceiptProductViewModel> product_list { get; set; } = [];
}

/// <summary>
/// Product snapshot contained in an ERP logistics shipment.
/// </summary>
public class ErpPendingReceiptProductViewModel
{
    public string source_item_key { get; set; } = string.Empty;
    public long? task_item_id { get; set; }
    public long? allocation_id { get; set; }
    public long? commodity_id { get; set; }
    public long? order_user_id { get; set; }
    public long? dept_id { get; set; }
    public string sku { get; set; } = string.Empty;
    public string product_name { get; set; } = string.Empty;
    public long? quantity { get; set; }
    public string usage_type { get; set; } = string.Empty;
    public string order_user_name { get; set; } = string.Empty;
    public string dept_name { get; set; } = string.Empty;
}

/// <summary>
/// Logistics detail and event timeline for an ERP receipt shipment.
/// </summary>
public class ErpPendingReceiptLogisticsViewModel
{
    public long shipment_id { get; set; }
    public string logistics_name { get; set; } = string.Empty;
    public string tracking_no { get; set; } = string.Empty;
    public string tracking_status { get; set; } = string.Empty;
    public string tracking_status_name { get; set; } = string.Empty;
    public string latest_event_desc { get; set; } = string.Empty;
    public DateTime? latest_event_time { get; set; }
    public string latest_event_location { get; set; } = string.Empty;
    public DateTime? estimated_delivery_time { get; set; }
    public DateTime? actual_delivery_time { get; set; }
    public List<ErpPendingReceiptTrackEventViewModel> event_list { get; set; } = [];
}

/// <summary>
/// One ERP logistics tracking event.
/// </summary>
public class ErpPendingReceiptTrackEventViewModel
{
    public long id { get; set; }
    public DateTime? event_time { get; set; }
    public string status_name { get; set; } = string.Empty;
    public string description { get; set; } = string.Empty;
    public string location { get; set; } = string.Empty;
    public string stage { get; set; } = string.Empty;
}
