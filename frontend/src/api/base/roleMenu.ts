import http from '@/utils/http/request'
import { RoleMenuBatchPayload, RoleMenuVO, RoleWarehouseBindingPayload } from '@/types/Base/RoleMenu'

// Get user authority
export const getUserAuthority = (userrole_id: number) => http({
    url: '/rolemenu/authority',
    method: 'get',
    params: {
      userrole_id
    }
  })

// Get all
export const getRoleMenuAll = () => http({
    url: '/rolemenu/all',
    method: 'get'
  })

// Get all menu setting
export const getMenus = () => http({
    url: '/rolemenu/menus',
    method: 'get'
  })

// Get form by id
export const getRoleMenuById = (userrole_id: number) => http({
    url: '/rolemenu',
    method: 'get',
    params: {
      userrole_id
    }
  })

// Get form by id
export const addRoleMenu = (data: RoleMenuVO) => http({
    url: '/rolemenu',
    method: 'post',
    data
  })

// Update form
export const updateRoleMenu = (data: RoleMenuVO) => http({
    url: '/rolemenu',
    method: 'put',
    data
  })

// Batch update current role's full permission tree
export const updateRoleMenuBatch = (data: RoleMenuBatchPayload) => http({
    url: '/rolemenu/batch',
    method: 'put',
    data
})

// Get explicit ERP warehouse bindings for one role
export const getRoleWarehouses = (userrole_id: number) => http({
  url: '/rolemenu/warehouses',
  method: 'get',
  params: { userrole_id }
})

// Atomically replace explicit ERP warehouse bindings for one role
export const updateRoleWarehouses = (data: RoleWarehouseBindingPayload) => http({
  url: '/rolemenu/warehouses',
  method: 'put',
  data
})

// Get all warehouses the current administrator may assign
export const getWarehouseAccessOptions = () => http({
  url: '/warehouse/access-options',
  method: 'get'
})

// Delete form
export const deleteRoleMenu = (userrole_id: number) => http({
    url: '/rolemenu',
    method: 'delete',
    params: {
      userrole_id
    }
  })
