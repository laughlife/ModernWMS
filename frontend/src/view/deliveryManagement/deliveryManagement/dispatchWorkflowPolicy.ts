import type {
  DispatchOrderStatus,
  DispatchWorkflowErrorCode,
  WarehouseAccess
} from '@/types/DeliveryManagement/DispatchWorkflow'
import type { ApiResult } from '@/types/System/ApiResult'

export type DispatchWorkflowTab =
  | 'tabGoodsToBePicked'
  | 'tabPicked'
  | 'tabWeighed'
  | 'tabDelivered'
  | 'tabCompleted'

export type SourceErrorAction = 'continue' | 'cancel' | 'refresh'
export type DispatchRefreshTarget = 'packingTasks' | 'currentTab'

const STATUS_TAB: Record<DispatchOrderStatus, DispatchWorkflowTab | null> = {
  PENDING_PICK: 'tabGoodsToBePicked',
  PICKED: 'tabPicked',
  WEIGHING: 'tabWeighed',
  PENDING_OUTBOUND: 'tabDelivered',
  OUTBOUND: 'tabCompleted',
  SOURCE_CANCELLED: null,
  MANUAL_CANCELLED: null
}

export const getDispatchStatusTab = (status: DispatchOrderStatus): DispatchWorkflowTab | null => STATUS_TAB[status]

export const getDispatchStatusRefreshTargets = (status: DispatchOrderStatus): DispatchRefreshTarget[] =>
  status === 'SOURCE_CANCELLED' || status === 'MANUAL_CANCELLED'
    ? ['packingTasks', 'currentTab']
    : ['currentTab']

export const getSourceErrorActions = (errorCode: DispatchWorkflowErrorCode): SourceErrorAction[] =>
  errorCode === 'SOURCE_CHANGE_PENDING' ? ['continue', 'cancel'] : ['refresh']

export const resolveDefaultWarehouseId = (access: WarehouseAccess): number | null => {
  const defaultId = access.default_warehouse_id
  return defaultId !== null && access.warehouses.some(({ id }) => id === defaultId) ? defaultId : null
}

export const getDispatchOrderRowKey = <T extends { id: number }>(order: T): number => order.id

export interface WarehouseAccessLoadResult {
  access: WarehouseAccess | null
  hasError: boolean
}

export const loadWarehouseAccessSafely = async (
  load: () => Promise<ApiResult<WarehouseAccess>>
): Promise<WarehouseAccessLoadResult> => {
  try {
    const result = await load()
    return result.isSuccess
      ? { access: result.data, hasError: false }
      : { access: null, hasError: true }
  } catch {
    return { access: null, hasError: true }
  }
}
