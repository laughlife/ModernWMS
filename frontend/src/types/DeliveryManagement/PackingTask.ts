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
  stock_id: number
  erp_stock_id?: number | null
  stock_allocation_id?: number | null
  sku_id: number
  sku_code: string
  spu_code: string
  commodity_name: string
  main_image: string
  goods_location_id: number | null
  location_name: string
  warehouse_id: number
  warehouse_name: string
  goods_owner_id: number
  goods_owner_name: string
  qty: number
  available_qty: number
  series_number: string
  expiry_date?: string | null
  matched: boolean
  selected: boolean
  selected_qty?: number
  is_creator_stock: boolean
  row_version: number
  can_manage: boolean
}

export interface PackingTaskStockPageRequest {
  sellfox_task_id: number
  sellfox_item_id: number
  page_index: number
  page_size: number
  search_others?: boolean
  keyword?: string
  location?: string
  owner?: string
}

export interface PackingTaskStockSelectRequest {
  sellfox_task_id: number
  sellfox_item_id: number
  stock_id: number
  erp_stock_id?: number | null
  stock_allocation_id?: number | null
  qty: number
  variant?: number
  row_version: number
  request_id: string
  goods_owner_id: number
  sku_mismatch_confirmed: boolean
  sku_mismatch_challenge?: string
}

export interface PackingTaskSkuMismatchChallengeRequest {
  sellfox_task_id: number
  sellfox_item_id: number
  stock_id: number
  goods_owner_id: number
  qty: number
  variant: number
  request_id: string
}
