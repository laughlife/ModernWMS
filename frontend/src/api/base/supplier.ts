import http from '@/utils/http/request'
import { PageConfigProps } from '@/types/System/Form'

// Get all
export const getSupplierAll = () => http({
  url: '/supplier/all',
  method: 'get'
})

// Find data by pagination
export const getSupplierList = (data: PageConfigProps) => http({
  url: '/supplier/list',
  method: 'post',
  data
})
