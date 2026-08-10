export interface FbaShipmentItemVO {
  stock_move_item_id: number
  stock_id?: number | null
  commodity_id?: number | null
  fba_shipment_item_id?: number | null
  main_image: string
  commodity_name: string
  stock_sku: string
  fba_sku: string
  qty: number
  variant_qty: number
  shipment_total_qty: number
  sku_matched: boolean
  sku_mismatch_confirmed: boolean
  stock_available_qty: number
  stock_occupied_qty: number
  stock_total_qty: number
  inventory_ready: boolean
}

export interface FbaShipmentVO {
  stock_move_id: number
  stock_move_no: string
  fba_shipment_id: number
  fba_no: string
  shipment_name: string
  fba_status: string
  fulfillment_center_id: string
  shop_name: string
  marketplace_name: string
  shipping_mode: string
  shipping_solution: string
  dept_id?: number | null
  dept_name: string
  order_user_id?: number | null
  order_user_name: string
  from_warehouse_id: number
  from_warehouse_name: string
  freight_forwarder_id?: number | null
  freight_forwarder_name: string
  logistics_name: string
  primary_tracking_no: string
  product_count: number
  shipment_total_qty: number
  locked_qty: number
  tracking_numbers: string[]
  inventory_ready: boolean
  inventory_status_name: string
  prepared_time: string
  source_update_time: string
  item_list: FbaShipmentItemVO[]
}
