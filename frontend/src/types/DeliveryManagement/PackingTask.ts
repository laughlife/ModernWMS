export interface PackingTaskItemVO {
  id: number
  sellfox_item_id: number
  commodity_id?: number | null
  commodity_sku?: string | null
  commodity_name?: string | null
  main_image?: string | null
  fn_sku?: string | null
  sku?: string | null
  msku?: string | null
  task_num?: number | null
  quantity_shipped?: number | null
  stock_available?: number | null
  stock_sku_code?: string | null
  stock_qty?: number | null
  stock_available_qty?: number | null
  locked_qty?: number | null
}

export interface PackingTaskVO {
  id: number
  sellfox_task_id: number
  packing_task_sn: string
  warehouse_id?: number | null
  warehouse_name?: string | null
  complete_num?: number | null
  task_num?: number | null
  create_name?: string | null
  source_create_time?: string | null
  item_count?: number | null
  shop_name?: string | null
  marketplace_name?: string | null
  item_list: PackingTaskItemVO[]
}

export interface SelectableStockVO {
  erp_stock_id: number
  commodity_id?: number | null
  sku_code: string
  commodity_name: string
  main_image: string
  warehouse_id: number
  warehouse_name: string
  order_user_id: number
  order_user_name: string
  available_qty: number
  occupied_qty: number
  total_qty: number
  matched: boolean
  selected: boolean
  selected_qty: number
}

export interface PackingTaskStockPageRequest {
  sellfox_task_id: number
  sellfox_item_id: number
  page_index: number
  page_size: number
  keyword?: string
}

export interface PackingTaskStockSelectRequest {
  sellfox_task_id: number
  sellfox_item_id: number
  erp_stock_id: number
  variant: number
}
