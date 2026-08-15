import http from '@/utils/http/request'
import type { PageConfigProps } from '@/types/System/Form'

export const getPackingTaskPage = (data: PageConfigProps) => http({
  url: '/packing-task-query/page',
  method: 'post',
  data
})
