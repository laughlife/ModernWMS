using System.ComponentModel.DataAnnotations.Schema;

namespace ModernWMS.Core.DBContext.Entities;

/// <summary>
/// ERP stock-move header used as the authoritative FBA shipment preparation fact.
/// </summary>
[Table("trk_stock_move")]
public class ErpStockMoveEntity
{
    /// <summary>
    /// 获取或设置 id。
    /// </summary>
    public long id { get; set; }
    /// <summary>
    /// 获取或设置 no。
    /// </summary>
    public string no { get; set; } = string.Empty;
    /// <summary>
    /// 获取或设置 from_warehouse_id。
    /// </summary>
    public long from_warehouse_id { get; set; }
    /// <summary>
    /// 获取或设置 from_warehouse_name。
    /// </summary>
    public string? from_warehouse_name { get; set; }
    /// <summary>
    /// 获取或设置 to_freight_forwarder_id。
    /// </summary>
    public long? to_freight_forwarder_id { get; set; }
    /// <summary>
    /// 获取或设置 to_freight_forwarder_name。
    /// </summary>
    public string? to_freight_forwarder_name { get; set; }
    /// <summary>
    /// 获取或设置 dept_id。
    /// </summary>
    public long? dept_id { get; set; }
    /// <summary>
    /// 获取或设置 dept_name。
    /// </summary>
    public string? dept_name { get; set; }
    /// <summary>
    /// 获取或设置 order_user_id。
    /// </summary>
    public long? order_user_id { get; set; }
    /// <summary>
    /// 获取或设置 order_user_name。
    /// </summary>
    public string? order_user_name { get; set; }
    /// <summary>
    /// 获取或设置 status。
    /// </summary>
    public string status { get; set; } = string.Empty;
    /// <summary>
    /// 获取或设置 transfer_type。
    /// </summary>
    public string transfer_type { get; set; } = string.Empty;
    /// <summary>
    /// 获取或设置 provider_code。
    /// </summary>
    public string? provider_code { get; set; }
    /// <summary>
    /// 获取或设置 logistics_code。
    /// </summary>
    public string? logistics_code { get; set; }
    /// <summary>
    /// 获取或设置 logistics_name。
    /// </summary>
    public string? logistics_name { get; set; }
    /// <summary>
    /// 获取或设置 tracking_no。
    /// </summary>
    public string? tracking_no { get; set; }
    /// <summary>
    /// 获取或设置 total_count。
    /// </summary>
    public long total_count { get; set; }
    /// <summary>
    /// 获取或设置 frozen_qty。
    /// </summary>
    public long frozen_qty { get; set; }
    /// <summary>
    /// 获取或设置 remark。
    /// </summary>
    public string? remark { get; set; }
    /// <summary>
    /// 获取或设置 creator。
    /// </summary>
    public string? creator { get; set; }
    /// <summary>
    /// 获取或设置 create_time。
    /// </summary>
    public DateTime create_time { get; set; }
    /// <summary>
    /// 获取或设置 update_time。
    /// </summary>
    public DateTime update_time { get; set; }
    /// <summary>
    /// 获取或设置 deleted。
    /// </summary>
    public bool deleted { get; set; }
    /// <summary>
    /// 获取或设置 shipment_status。
    /// </summary>
    public string? shipment_status { get; set; }
    /// <summary>
    /// 获取或设置 shipment_status_time。
    /// </summary>
    public DateTime? shipment_status_time { get; set; }
}

/// <summary>
/// ERP stock-move item that binds a prepared FBA item to frozen business stock.
/// </summary>
[Table("trk_stock_move_item")]
public class ErpStockMoveItemEntity
{
    /// <summary>
    /// 获取或设置 id。
    /// </summary>
    public long id { get; set; }
    /// <summary>
    /// 获取或设置 stock_move_id。
    /// </summary>
    public long stock_move_id { get; set; }
    /// <summary>
    /// 获取或设置 stock_id。
    /// </summary>
    public long? stock_id { get; set; }
    /// <summary>
    /// 获取或设置 commodity_id。
    /// </summary>
    public long? commodity_id { get; set; }
    /// <summary>
    /// 获取或设置 commodity_sku。
    /// </summary>
    public string? commodity_sku { get; set; }
    /// <summary>
    /// 获取或设置 commodity_name。
    /// </summary>
    public string? commodity_name { get; set; }
    /// <summary>
    /// 获取或设置 qty。
    /// </summary>
    public long qty { get; set; }
    /// <summary>
    /// 获取或设置 occupied_qty。
    /// </summary>
    public long? occupied_qty { get; set; }
    /// <summary>
    /// 获取或设置 remark。
    /// </summary>
    public string? remark { get; set; }
    /// <summary>
    /// 获取或设置 product_snapshot_json。
    /// </summary>
    public string? product_snapshot_json { get; set; }
    /// <summary>
    /// 获取或设置 update_time。
    /// </summary>
    public DateTime update_time { get; set; }
    /// <summary>
    /// 获取或设置 deleted。
    /// </summary>
    public bool deleted { get; set; }
}

/// <summary>
/// ERP business stock pool used to verify the frozen quantity before outbound.
/// </summary>
[Table("trk_stock")]
public class ErpBusinessStockEntity
{
    /// <summary>
    /// 获取或设置 id。
    /// </summary>
    public long id { get; set; }
    /// <summary>
    /// 获取或设置 warehouse_id。
    /// </summary>
    public long warehouse_id { get; set; }
    /// <summary>
    /// 获取或设置 warehouse_name。
    /// </summary>
    public string? warehouse_name { get; set; }
    /// <summary>
    /// 获取或设置 dept_id。
    /// </summary>
    public long? dept_id { get; set; }
    /// <summary>
    /// 获取或设置 dept_name。
    /// </summary>
    public string? dept_name { get; set; }
    /// <summary>
    /// 获取或设置 order_user_id。
    /// </summary>
    public long? order_user_id { get; set; }
    /// <summary>
    /// 获取或设置 order_user_name。
    /// </summary>
    public string? order_user_name { get; set; }
    /// <summary>
    /// 获取或设置 commodity_id。
    /// </summary>
    public long? commodity_id { get; set; }
    /// <summary>
    /// 获取或设置 commodity_sku。
    /// </summary>
    public string? commodity_sku { get; set; }
    /// <summary>
    /// 获取或设置 commodity_name。
    /// </summary>
    public string? commodity_name { get; set; }
    /// <summary>
    /// 获取或设置 product_snapshot_json。
    /// </summary>
    public string? product_snapshot_json { get; set; }
    /// <summary>
    /// 获取或设置 available_qty。
    /// </summary>
    public long available_qty { get; set; }
    /// <summary>
    /// 获取或设置 occupied_qty。
    /// </summary>
    public long occupied_qty { get; set; }
    /// <summary>
    /// 获取或设置 total_qty。
    /// </summary>
    public long total_qty { get; set; }
    /// <summary>
    /// 获取或设置 deleted。
    /// </summary>
    public bool deleted { get; set; }
}

/// <summary>
/// ERP FBA shipment header synchronized from Sellfox/Amazon.
/// </summary>
[Table("erp_fba_shipment")]
public class ErpFbaShipmentEntity
{
    /// <summary>
    /// 获取或设置 id。
    /// </summary>
    public long id { get; set; }
    /// <summary>
    /// 获取或设置 name。
    /// </summary>
    public string? name { get; set; }
    /// <summary>
    /// 获取或设置 shop_name。
    /// </summary>
    public string? shop_name { get; set; }
    /// <summary>
    /// 获取或设置 marketplace_name。
    /// </summary>
    public string? marketplace_name { get; set; }
    /// <summary>
    /// 获取或设置 region。
    /// </summary>
    public string? region { get; set; }
    /// <summary>
    /// 获取或设置 amazon_shipment_id。
    /// </summary>
    public string? amazon_shipment_id { get; set; }
    /// <summary>
    /// 获取或设置 shipment_status。
    /// </summary>
    public string? shipment_status { get; set; }
    /// <summary>
    /// 获取或设置 fulfillment_center_id。
    /// </summary>
    public string? fulfillment_center_id { get; set; }
    /// <summary>
    /// 获取或设置 quantity。
    /// </summary>
    public int? quantity { get; set; }
    /// <summary>
    /// 获取或设置 box_quantity。
    /// </summary>
    public int? box_quantity { get; set; }
    /// <summary>
    /// 获取或设置 carton_num。
    /// </summary>
    public int? carton_num { get; set; }
    /// <summary>
    /// 获取或设置 shipping_mode。
    /// </summary>
    public string? shipping_mode { get; set; }
    /// <summary>
    /// 获取或设置 shipping_solution。
    /// </summary>
    public string? shipping_solution { get; set; }
    /// <summary>
    /// 获取或设置 sellfox_update_time。
    /// </summary>
    public DateTime? sellfox_update_time { get; set; }
    /// <summary>
    /// 获取或设置 update_time。
    /// </summary>
    public DateTime? update_time { get; set; }
    /// <summary>
    /// 获取或设置 deleted。
    /// </summary>
    public bool deleted { get; set; }
}

/// <summary>
/// ERP FBA box and tracking-number snapshot.
/// </summary>
[Table("erp_fba_spd_box")]
public class ErpFbaSpdBoxEntity
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
    /// 获取或设置 box_id。
    /// </summary>
    public string box_id { get; set; } = string.Empty;
    /// <summary>
    /// 获取或设置 tracking_id。
    /// </summary>
    public string? tracking_id { get; set; }
    /// <summary>
    /// 获取或设置 tracking_number_validation_status。
    /// </summary>
    public string? tracking_number_validation_status { get; set; }
    /// <summary>
    /// 获取或设置 idx。
    /// </summary>
    public int? idx { get; set; }
    /// <summary>
    /// 获取或设置 weight。
    /// </summary>
    public decimal? weight { get; set; }
    /// <summary>
    /// 获取或设置 weight_unit。
    /// </summary>
    public string? weight_unit { get; set; }
    /// <summary>
    /// 获取或设置 length。
    /// </summary>
    public decimal? length { get; set; }
    /// <summary>
    /// 获取或设置 width。
    /// </summary>
    public decimal? width { get; set; }
    /// <summary>
    /// 获取或设置 height。
    /// </summary>
    public decimal? height { get; set; }
    /// <summary>
    /// 获取或设置 length_unit。
    /// </summary>
    public string? length_unit { get; set; }
    /// <summary>
    /// 获取或设置 deleted。
    /// </summary>
    public bool deleted { get; set; }
}

/// <summary>
/// ERP FBA shipment item that carries the authoritative FN SKU and product image.
/// </summary>
[Table("erp_fba_shipment_item")]
public class ErpFbaShipmentItemEntity
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
    /// 获取或设置 msku。
    /// </summary>
    public string? msku { get; set; }
    /// <summary>
    /// 获取或设置 fn_sku。
    /// </summary>
    public string? fn_sku { get; set; }
    /// <summary>
    /// 获取或设置 commodity_id。
    /// </summary>
    public long? commodity_id { get; set; }
    /// <summary>
    /// 获取或设置 commodity_sku。
    /// </summary>
    public string? commodity_sku { get; set; }
    /// <summary>
    /// 获取或设置 commodity_name。
    /// </summary>
    public string? commodity_name { get; set; }
    /// <summary>
    /// 获取或设置 title。
    /// </summary>
    public string? title { get; set; }
    /// <summary>
    /// 获取或设置 main_image。
    /// </summary>
    public string? main_image { get; set; }
    /// <summary>
    /// 获取或设置 quantity。
    /// </summary>
    public int? quantity { get; set; }
    /// <summary>
    /// 获取或设置 create_time。
    /// </summary>
    public DateTime? create_time { get; set; }
    /// <summary>
    /// 获取或设置 deleted。
    /// </summary>
    public bool deleted { get; set; }
}
