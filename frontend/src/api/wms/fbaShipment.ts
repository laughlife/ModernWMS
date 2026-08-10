import http from '@/utils/http/request'
import type { PageConfigProps } from '@/types/System/Form'

export const getFbaShipmentPage = (data: PageConfigProps) => http({
  url: '/fba-shipment/page',
  method: 'post',
  data
})

export const prepareFbaShipmentPicking = (stockMoveId: number) => http({
  url: `/fba-shipment/${stockMoveId}/prepare-picking`,
  method: 'post'
})
