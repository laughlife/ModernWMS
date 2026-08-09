using System.ComponentModel.DataAnnotations.Schema;

namespace ModernWMS.Core.DBContext.Entities;

/// <summary>
/// ERP stock-move header used as the authoritative FBA shipment preparation fact.
/// </summary>
[Table("trk_stock_move")]
public class ErpStockMoveEntity
{
    public long id { get; set; }
    public string no { get; set; } = string.Empty;
    public long from_warehouse_id { get; set; }
    public string? from_warehouse_name { get; set; }
    public long? to_freight_forwarder_id { get; set; }
    public string? to_freight_forwarder_name { get; set; }
    public long? dept_id { get; set; }
    public string? dept_name { get; set; }
    public long? order_user_id { get; set; }
    public string? order_user_name { get; set; }
    public string status { get; set; } = string.Empty;
    public string transfer_type { get; set; } = string.Empty;
    public string? provider_code { get; set; }
    public string? logistics_code { get; set; }
    public string? logistics_name { get; set; }
    public string? tracking_no { get; set; }
    public long total_count { get; set; }
    public long frozen_qty { get; set; }
    public string? remark { get; set; }
    public string? creator { get; set; }
    public DateTime create_time { get; set; }
    public DateTime update_time { get; set; }
    public bool deleted { get; set; }
    public string? shipment_status { get; set; }
    public DateTime? shipment_status_time { get; set; }
}

/// <summary>
/// ERP stock-move item that binds a prepared FBA item to frozen business stock.
/// </summary>
[Table("trk_stock_move_item")]
public class ErpStockMoveItemEntity
{
    public long id { get; set; }
    public long stock_move_id { get; set; }
    public long? stock_id { get; set; }
    public long? commodity_id { get; set; }
    public string? commodity_sku { get; set; }
    public string? commodity_name { get; set; }
    public long qty { get; set; }
    public long? occupied_qty { get; set; }
    public string? remark { get; set; }
    public string? product_snapshot_json { get; set; }
    public DateTime update_time { get; set; }
    public bool deleted { get; set; }
}

/// <summary>
/// ERP business stock pool used to verify the frozen quantity before outbound.
/// </summary>
[Table("trk_stock")]
public class ErpBusinessStockEntity
{
    public long id { get; set; }
    public long warehouse_id { get; set; }
    public string? warehouse_name { get; set; }
    public long? dept_id { get; set; }
    public string? dept_name { get; set; }
    public long? order_user_id { get; set; }
    public string? order_user_name { get; set; }
    public long? commodity_id { get; set; }
    public string? commodity_sku { get; set; }
    public string? commodity_name { get; set; }
    public string? product_snapshot_json { get; set; }
    public long available_qty { get; set; }
    public long occupied_qty { get; set; }
    public long total_qty { get; set; }
    public bool deleted { get; set; }
}

/// <summary>
/// ERP FBA shipment header synchronized from Sellfox/Amazon.
/// </summary>
[Table("erp_fba_shipment")]
public class ErpFbaShipmentEntity
{
    public long id { get; set; }
    public string? name { get; set; }
    public string? shop_name { get; set; }
    public string? marketplace_name { get; set; }
    public string? region { get; set; }
    public string? amazon_shipment_id { get; set; }
    public string? shipment_status { get; set; }
    public string? fulfillment_center_id { get; set; }
    public int? quantity { get; set; }
    public int? box_quantity { get; set; }
    public int? carton_num { get; set; }
    public string? shipping_mode { get; set; }
    public string? shipping_solution { get; set; }
    public DateTime? sellfox_update_time { get; set; }
    public DateTime? update_time { get; set; }
    public bool deleted { get; set; }
}

/// <summary>
/// ERP FBA box and tracking-number snapshot.
/// </summary>
[Table("erp_fba_spd_box")]
public class ErpFbaSpdBoxEntity
{
    public long id { get; set; }
    public long shipment_id { get; set; }
    public string box_id { get; set; } = string.Empty;
    public string? tracking_id { get; set; }
    public string? tracking_number_validation_status { get; set; }
    public int? idx { get; set; }
    public decimal? weight { get; set; }
    public string? weight_unit { get; set; }
    public decimal? length { get; set; }
    public decimal? width { get; set; }
    public decimal? height { get; set; }
    public string? length_unit { get; set; }
    public bool deleted { get; set; }
}
