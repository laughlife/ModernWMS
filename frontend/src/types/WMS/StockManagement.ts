export interface StockLocationVO {
    warehouse_id: number
    warehouse_name: string
    warehouse_area_id: number
    location_name: string
    warehouse_area_name: string
    spu_name: string
    product_image: string
    sku_id: number
    sku_code: string
    qty: number
    qty_available: number
    qty_locked: number
    goods_owner_name: string
}

export interface StockVO {
    spu_name: string
    product_image: string
    sku_code: string
    sku_id: number
    qty: number
    qty_available: number
    qty_locked: number
    qty_to_sort: number
    qty_sorted: number
}
