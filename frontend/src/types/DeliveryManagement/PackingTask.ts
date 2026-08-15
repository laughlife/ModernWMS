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
