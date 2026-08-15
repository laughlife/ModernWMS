import type { RoleWarehouseBindingPayload } from '@/types/Base/RoleMenu'

export const buildRoleWarehousePayload = (
  userroleId: number,
  warehouseIds: number[]
): RoleWarehouseBindingPayload => ({
  userrole_id: userroleId,
  warehouse_ids: [...new Set(warehouseIds)].sort((left, right) => left - right)
})
