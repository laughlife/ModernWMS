import http from '@/utils/http/request'
import { PageConfigProps } from '@/types/System/Form'
import { UpdateSaftyStockReqBodyVO } from '@/types/Base/CommodityManagement'

// Find Data by Pagination
export const getSpuList = (data: PageConfigProps) => http({
    url: '/spu/list',
    method: 'post',
    data
  })

// Read-only SKU catalog used by commodity management
export const getCommodityCatalog = (data: PageConfigProps) => http({
    url: '/spu/catalog',
    method: 'post',
    data
  })

// Update safety stock
export const updateSaftyStock = (data: { sku_id: number; detailList: UpdateSaftyStockReqBodyVO[] }) => http({
    url: '/spu/sku-safety-stock',
    method: 'put',
    data
  })
