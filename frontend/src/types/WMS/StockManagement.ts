export interface StockLocationVO {
    erp_stock_id: number | null
    stock_allocation_id: number | null
    inventory_mode: 'LEGACY_READ' | 'CANONICAL_ERP'
    location_state: 'LEGACY' | 'ACTIVE' | 'UNLOCATED'
    is_pending_location: boolean
    allocation_consistent: boolean
    warehouse_id: number
    warehouse_name: string
    warehouse_area_id: number
    location_name: string
    warehouse_area_name: string
    spu_code: string
    spu_name: string
    product_image: string
    sku_id: number
    sku_code: string
    qty: number
    qty_available: number
    qty_locked: number
    erp_total_qty: number
    erp_available_qty: number
    erp_occupied_qty: number
    goods_owner_name: string
}

export interface StockVO {
    spu_code: string
    spu_name: string
    product_image: string
    sku_code: string
    sku_id: number
    qty: number
    qty_available: number
    qty_locked: number
    qty_pending_location: number
    erp_total_qty: number
    erp_available_qty: number
    erp_occupied_qty: number
    allocation_consistent: boolean
    qty_to_sort: number
    qty_sorted: number
}
