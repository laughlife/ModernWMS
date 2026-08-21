namespace ModernWMS.WMS.Entities.ViewModels;

/// <summary>
/// ERP shipment waiting to be received by ModernWMS.
/// </summary>
public class ErpPendingReceiptViewModel
{
    /// <summary>
    /// 获取或设置 id。
    /// </summary>
    public long id { get; set; }
    /// <summary>
    /// 获取或设置 source_type。
    /// </summary>
    public string source_type { get; set; } = string.Empty;
    /// <summary>
    /// 获取或设置 source_stock_move_no。
    /// </summary>
    public string source_stock_move_no { get; set; } = string.Empty;
    /// <summary>
    /// 获取或设置 purchase_no。
    /// </summary>
    public string purchase_no { get; set; } = string.Empty;
    /// <summary>
    /// 获取或设置 supplier_name。
    /// </summary>
    public string supplier_name { get; set; } = string.Empty;
    /// <summary>
    /// 获取或设置 order_user_text。
    /// </summary>
    public string order_user_text { get; set; } = string.Empty;
    /// <summary>
    /// 获取或设置 shipment_batch_no。
    /// </summary>
    public string shipment_batch_no { get; set; } = string.Empty;
    /// <summary>
    /// 获取或设置 shipment_type。
    /// </summary>
    public string shipment_type { get; set; } = string.Empty;
    /// <summary>
    /// 获取或设置 shipment_qty。
    /// </summary>
    public long shipment_qty { get; set; }
    /// <summary>
    /// 获取或设置 shipment_time。
    /// </summary>
    public DateTime? shipment_time { get; set; }
    /// <summary>
    /// 获取或设置 warehouse_id。
    /// </summary>
    public long warehouse_id { get; set; }
    /// <summary>
    /// 获取或设置 warehouse_name。
    /// </summary>
    public string warehouse_name { get; set; } = string.Empty;
    /// <summary>
    /// 获取或设置 wms_warehouse_id。
    /// </summary>
    public int wms_warehouse_id { get; set; }
    /// <summary>
    /// 获取或设置 freight_forwarder_name。
    /// </summary>
    public string freight_forwarder_name { get; set; } = string.Empty;
    /// <summary>
    /// 获取或设置 source_freight_payment_type。
    /// </summary>
    public string source_freight_payment_type { get; set; } = string.Empty;
    /// <summary>
    /// 获取或设置 provider_code。
    /// </summary>
    public string provider_code { get; set; } = string.Empty;
    /// <summary>
    /// 获取或设置 logistics_code。
    /// </summary>
    public string logistics_code { get; set; } = string.Empty;
    /// <summary>
    /// 获取或设置 logistics_name。
    /// </summary>
    public string logistics_name { get; set; } = string.Empty;
    /// <summary>
    /// 获取或设置 tracking_no。
    /// </summary>
    public string tracking_no { get; set; } = string.Empty;
    /// <summary>
    /// 获取或设置 lifecycle_status。
    /// </summary>
    public string lifecycle_status { get; set; } = string.Empty;
    /// <summary>
    /// 获取或设置 tracking_status。
    /// </summary>
    public string tracking_status { get; set; } = string.Empty;
    /// <summary>
    /// 获取或设置 tracking_status_name。
    /// </summary>
    public string tracking_status_name { get; set; } = string.Empty;
    /// <summary>
    /// 获取或设置 latest_event_desc。
    /// </summary>
    public string latest_event_desc { get; set; } = string.Empty;
    /// <summary>
    /// 获取或设置 latest_event_time。
    /// </summary>
    public DateTime? latest_event_time { get; set; }
    /// <summary>
    /// 获取或设置 latest_event_location。
    /// </summary>
    public string latest_event_location { get; set; } = string.Empty;
    /// <summary>
    /// 获取或设置 estimated_delivery_time。
    /// </summary>
    public DateTime? estimated_delivery_time { get; set; }
    /// <summary>
    /// 获取或设置 actual_delivery_time。
    /// </summary>
    public DateTime? actual_delivery_time { get; set; }
    /// <summary>
    /// 获取或设置 source_version。
    /// </summary>
    public int source_version { get; set; }
    /// <summary>
    /// 获取或设置 product_summary。
    /// </summary>
    public string product_summary { get; set; } = string.Empty;
    /// <summary>
    /// 获取或设置 product_count。
    /// </summary>
    public int product_count { get; set; }
    /// <summary>
    /// 获取或设置 product_list。
    /// </summary>
    public List<ErpPendingReceiptProductViewModel> product_list { get; set; } = [];
}

/// <summary>
/// Product snapshot contained in an ERP logistics shipment.
/// </summary>
public class ErpPendingReceiptProductViewModel
{
    /// <summary>
    /// 获取或设置 source_item_key。
    /// </summary>
    public string source_item_key { get; set; } = string.Empty;
    /// <summary>
    /// 获取或设置 task_item_id。
    /// </summary>
    public long? task_item_id { get; set; }
    /// <summary>
    /// 获取或设置 allocation_id。
    /// </summary>
    public long? allocation_id { get; set; }
    /// <summary>
    /// 获取或设置 commodity_id。
    /// </summary>
    public long? commodity_id { get; set; }
    /// <summary>
    /// 获取或设置 order_user_id。
    /// </summary>
    public long? order_user_id { get; set; }
    /// <summary>
    /// 获取或设置 dept_id。
    /// </summary>
    public long? dept_id { get; set; }
    /// <summary>
    /// 获取或设置 sku。
    /// </summary>
    public string sku { get; set; } = string.Empty;
    /// <summary>
    /// 获取或设置 product_name。
    /// </summary>
    public string product_name { get; set; } = string.Empty;
    /// <summary>
    /// 获取或设置 main_image。
    /// </summary>
    public string main_image { get; set; } = string.Empty;
    /// <summary>
    /// 获取或设置 quantity。
    /// </summary>
    public long? quantity { get; set; }
    /// <summary>
    /// 获取或设置 usage_type。
    /// </summary>
    public string usage_type { get; set; } = string.Empty;
    /// <summary>
    /// 获取或设置 order_user_name。
    /// </summary>
    public string order_user_name { get; set; } = string.Empty;
    /// <summary>
    /// 获取或设置 dept_name。
    /// </summary>
    public string dept_name { get; set; } = string.Empty;
    /// <summary>
    /// 获取或设置 default_warehouse_area_id。
    /// </summary>
    public int? default_warehouse_area_id { get; set; }
    /// <summary>
    /// 获取或设置 default_warehouse_area_name。
    /// </summary>
    public string default_warehouse_area_name { get; set; } = string.Empty;
    /// <summary>
    /// 获取或设置 default_goods_owner_id。
    /// </summary>
    public long? default_goods_owner_id { get; set; }
    /// <summary>
    /// 获取或设置 default_goods_owner_name。
    /// </summary>
    public string default_goods_owner_name { get; set; } = string.Empty;
}

/// <summary>
/// One product-level receipt result after inventory posting.
/// </summary>
public class ErpReceiptDetailViewModel
{
    /// <summary>
    /// 获取或设置 id。
    /// </summary>
    public long id { get; set; }
    /// <summary>
    /// 获取或设置 shipment_id。
    /// </summary>
    public long shipment_id { get; set; }
    /// <summary>
    /// 获取或设置 erp_stock_id。
    /// </summary>
    public long? erp_stock_id { get; set; }
    /// <summary>
    /// 获取或设置 purchase_no。
    /// </summary>
    public string purchase_no { get; set; } = string.Empty;
    /// <summary>
    /// 获取或设置 shipment_batch_no。
    /// </summary>
    public string shipment_batch_no { get; set; } = string.Empty;
    /// <summary>
    /// 获取或设置 commodity_sku。
    /// </summary>
    public string commodity_sku { get; set; } = string.Empty;
    /// <summary>
    /// 获取或设置 commodity_name。
    /// </summary>
    public string commodity_name { get; set; } = string.Empty;
    /// <summary>
    /// 获取或设置 main_image。
    /// </summary>
    public string main_image { get; set; } = string.Empty;
    /// <summary>
    /// 获取或设置 dept_name。
    /// </summary>
    public string dept_name { get; set; } = string.Empty;
    /// <summary>
    /// 获取或设置 order_user_name。
    /// </summary>
    public string order_user_name { get; set; } = string.Empty;
    /// <summary>
    /// 获取或设置 warehouse_area_id。
    /// </summary>
    public int warehouse_area_id { get; set; }
    /// <summary>
    /// 获取或设置 warehouse_area_name。
    /// </summary>
    public string warehouse_area_name { get; set; } = string.Empty;
    /// <summary>
    /// 获取或设置 warehouse_id。
    /// </summary>
    public long warehouse_id { get; set; }
    /// <summary>
    /// 获取或设置 warehouse_name。
    /// </summary>
    public string warehouse_name { get; set; } = string.Empty;
    /// <summary>
    /// 获取或设置 lifecycle_status。
    /// </summary>
    public string lifecycle_status { get; set; } = string.Empty;
    /// <summary>
    /// 获取或设置 location_state。
    /// </summary>
    public string location_state { get; set; } = string.Empty;
    /// <summary>
    /// 获取或设置 data_source。
    /// </summary>
    public string data_source { get; set; } = string.Empty;
    /// <summary>
    /// 获取或设置 unlocated。
    /// </summary>
    public bool unlocated { get; set; }
    /// <summary>
    /// 获取或设置 receipt_time。
    /// </summary>
    public DateTime receipt_time { get; set; }
    /// <summary>
    /// 获取或设置 actual_receipt_qty。
    /// </summary>
    public long actual_receipt_qty { get; set; }
    /// <summary>
    /// 获取或设置 loss_qty。
    /// </summary>
    public long loss_qty { get; set; }
    /// <summary>
    /// 获取或设置 inbound_qty。
    /// </summary>
    public long inbound_qty { get; set; }
    /// <summary>
    /// 获取或设置 total_weight。
    /// </summary>
    public decimal? total_weight { get; set; }
    /// <summary>
    /// 获取或设置 total_volume。
    /// </summary>
    public decimal? total_volume { get; set; }
    /// <summary>
    /// 获取或设置 allocation_list。
    /// </summary>
    public List<ErpReceiptAllocationViewModel> allocation_list { get; set; } = [];
}

/// <summary>
/// One physical-inventory allocation recorded for a receipt item.
/// </summary>
public class ErpReceiptAllocationViewModel
{
    /// <summary>
    /// 获取或设置 warehouse_area_id。
    /// </summary>
    public int warehouse_area_id { get; set; }
    /// <summary>
    /// 获取或设置 warehouse_area_name。
    /// </summary>
    public string warehouse_area_name { get; set; } = string.Empty;
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
    public long qty { get; set; }
}

/// <summary>
/// Logistics detail and event timeline for an ERP receipt shipment.
/// </summary>
public class ErpPendingReceiptLogisticsViewModel
{
    /// <summary>
    /// 获取或设置 shipment_id。
    /// </summary>
    public long shipment_id { get; set; }
    /// <summary>
    /// 获取或设置 logistics_name。
    /// </summary>
    public string logistics_name { get; set; } = string.Empty;
    /// <summary>
    /// 获取或设置 tracking_no。
    /// </summary>
    public string tracking_no { get; set; } = string.Empty;
    /// <summary>
    /// 获取或设置 tracking_status。
    /// </summary>
    public string tracking_status { get; set; } = string.Empty;
    /// <summary>
    /// 获取或设置 tracking_status_name。
    /// </summary>
    public string tracking_status_name { get; set; } = string.Empty;
    /// <summary>
    /// 获取或设置 latest_event_desc。
    /// </summary>
    public string latest_event_desc { get; set; } = string.Empty;
    /// <summary>
    /// 获取或设置 latest_event_time。
    /// </summary>
    public DateTime? latest_event_time { get; set; }
    /// <summary>
    /// 获取或设置 latest_event_location。
    /// </summary>
    public string latest_event_location { get; set; } = string.Empty;
    /// <summary>
    /// 获取或设置 estimated_delivery_time。
    /// </summary>
    public DateTime? estimated_delivery_time { get; set; }
    /// <summary>
    /// 获取或设置 actual_delivery_time。
    /// </summary>
    public DateTime? actual_delivery_time { get; set; }
    /// <summary>
    /// 获取或设置 event_list。
    /// </summary>
    public List<ErpPendingReceiptTrackEventViewModel> event_list { get; set; } = [];
}

/// <summary>
/// One ERP logistics tracking event.
/// </summary>
public class ErpPendingReceiptTrackEventViewModel
{
    /// <summary>
    /// 获取或设置 id。
    /// </summary>
    public long id { get; set; }
    /// <summary>
    /// 获取或设置 event_time。
    /// </summary>
    public DateTime? event_time { get; set; }
    /// <summary>
    /// 获取或设置 status_name。
    /// </summary>
    public string status_name { get; set; } = string.Empty;
    /// <summary>
    /// 获取或设置 description。
    /// </summary>
    public string description { get; set; } = string.Empty;
    /// <summary>
    /// 获取或设置 location。
    /// </summary>
    public string location { get; set; } = string.Empty;
    /// <summary>
    /// 获取或设置 stage。
    /// </summary>
    public string stage { get; set; } = string.Empty;
}
