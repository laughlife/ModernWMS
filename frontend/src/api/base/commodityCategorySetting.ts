import http from '@/utils/http/request'

// Get all operator groups from ruoyi-vue-pro readonly data source
export const getOperatorGroupAll = () => http({
    url: '/operator-group/all',
    method: 'get'
  })
