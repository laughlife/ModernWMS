import http from '@/utils/http/request'
import { unwrapApiResult } from '@/utils/http/apiResult'
import { PageConfigProps } from '@/types/System/Form'
import type { ApiResult } from '@/types/System/ApiResult'
import { StockAsnVO, SortingVo, PutawayVo } from '@/types/WMS/StockAsn'

export const listNew = (data: PageConfigProps) => http({
    url: '/asn/asnmaster/list',
    method: 'post',
    data
  })

export const addAsnNew = (data: StockAsnVO) => http({
    url: '/asn/asnmaster',
    method: 'post',
    data
  })

export const updateAsnNew = (data: StockAsnVO) => http({
    url: '/asn/asnmaster',
    method: 'put',
    data
  })

export const confirmArrival = (data: { id: number; arrival_time: string }[]) => http({
    url: '/asn/confirm',
    method: 'put',
    data
  })

export const confirmUnload = (data: { id: number; unloadTime: string; unloadPerson: string; unloadPersonID: number }[]) => http({
    url: '/asn/unload',
    method: 'put',
    data
  })

export const unconfirmArrival = (idList: number[]) => http({
    url: '/asn/confirm-cancel',
    method: 'put',
    data: idList
  })

export const editSorting = (data: { asn_id: number; series_number: string; sorted_qty: number }[]) => http({
    url: '/asn/sorting',
    method: 'put',
    data
  })

export const modifySorting = (data: { asn_id: number; series_number: string; sorted_qty: number }[]) => http({
    url: '/asn/sorting-modify',
    method: 'put',
    data
  })

export const getSorting = (id: number) => http({
    url: '/asn/sorting',
    method: 'get',
    params: {
      asn_id: id
    }
  })

export const getGrouding = (id: number) => http({
    url: '/asn/pending-putaway',
    method: 'get',
    params: {
      id
    }
  })

export const confirmPutaway = (
  data: {
    asn_id: number
    goods_owner_id: number
    series_number: string
    goods_location_id: number
    putaway_qty: number
  }[]
) => http({
    url: '/asn/putaway',
    method: 'put',
    data
  })

export const confirmSorted = (id: number) => http({
    url: '/asn/sorted',
    method: 'put',
    data: [id]
  })

export const revokeUnload = (idList: number[]) => http({
    url: '/asn/unload-cancel',
    method: 'put',
    data: idList
  })

export const revokeSorting = (idList: number[]) => http({
    url: '/asn/sorted-cancel',
    method: 'put',
    data: idList
  })

export const getStockAsnList = (data: PageConfigProps) => http({
    url: '/asn/list',
    method: 'post',
    data
  })

export const getErpPendingReceiptList = (data: PageConfigProps) => http({
    url: '/asn/erp-pending-receipt/list',
    method: 'post',
    data
  })

export const getErpArrivedReceiptList = (data: PageConfigProps) => http({
    url: '/asn/erp-pending-receipt/arrived-list',
    method: 'post',
    data
  })

export const getErpReceiptLogistics = (shipmentId: number) => http({
    url: '/asn/erp-pending-receipt/logistics',
    method: 'get',
    params: { shipmentId }
  })

export interface ErpReceiptOssImage {
  name: string
  path: string
  url: string
  access_url: string
  content_type: string
  size: number
}

export type ErpReceiptImageCategory = 'freight' | 'loss' | 'receipt'

export interface ErpReceiptConfirmInput {
  shipment_id: number
  source_version: number
  items: Array<{
    source_item_key: string
    commodity_id?: number | null
    commodity_sku: string
    shipment_qty: number
    actual_receipt_qty: number
    loss_qty: number
  }>
  receipt_freight_payment_status: 'NO_PAY' | 'PAY'
  receipt_freight_amount: number | null
  receipt_freight_files: ErpReceiptOssImage[]
  receipt_files: ErpReceiptOssImage[]
  loss_reason: string
  loss_files: ErpReceiptOssImage[]
  receipt_remark: string
}

export const confirmErpReceipt = (data: ErpReceiptConfirmInput): Promise<ApiResult<number>> => {
  return http<ApiResult<number>>({
    url: '/asn/erp-pending-receipt/confirm',
    method: 'post',
    data
  }).then((response) => unwrapApiResult<number>(response))
}

export const uploadErpReceiptImage = (
  file: File,
  shipmentId: number,
  category: ErpReceiptImageCategory
): Promise<ApiResult<ErpReceiptOssImage>> => {
  const data = new FormData()
  data.append('file', file)
  data.append('shipmentId', String(shipmentId))
  data.append('category', category)
  return http<ApiResult<ErpReceiptOssImage>>({
    url: '/file/erp-oss/image',
    method: 'post',
    data,
    timeout: 120000
  }).then((response) => unwrapApiResult<ErpReceiptOssImage>(response))
}

export const addAsn = (data: StockAsnVO) => http({
    url: '/asn',
    method: 'post',
    data
  })

export const updateAsn = (data: StockAsnVO) => http({
    url: '/asn',
    method: 'put',
    data
  })

export const deleteAsn = (id: number) => http({
    url: '/asn',
    method: 'delete',
    params: {
      id
    }
  })

export const deleteAsnByID = (id: number) => http({
    url: '/asn/asnmaster',
    method: 'delete',
    params: {
      id
    }
  })

export const confirmAsn = (id: number) => http({
    url: `/asn/confirm/${ id }`,
    method: 'put'
  })
export const confirmAsnCancel = (id: number) => http({
    url: `/asn/confirm-cancel/${ id }`,
    method: 'put'
  })

export const unloadAsn = (id: number) => http({
    url: `/asn/unload/${ id }`,
    method: 'put'
  })
export const unloadAsnCancel = (id: number) => http({
    url: `/asn/unload-cancel/${ id }`,
    method: 'put'
  })

export const sortedAsn = (id: number) => http({
    url: `/asn/sorted/${ id }`,
    method: 'put'
  })
export const sortedAsnCancel = (id: number) => http({
    url: `/asn/sorted-cancel/${ id }`,
    method: 'put'
  })

export const sortingAsn = (data: SortingVo) => http({
    url: '/asn/sorting',
    method: 'put',
    data
  })

export const putawayAsn = (data: PutawayVo) => http({
    url: '/asn/putaway',
    method: 'put',
    data
  })

export const getSkuInfo = (id: number) => http({
    url: '/spu/sku',
    method: 'get',
    params: {
      sku_id: id
    }
  })

  export const getPrintAsnList = (data: number[]) => http({
    url: '/asn/print-sn',
    method: 'post',
    data
  })
