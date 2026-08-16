import type {
  CompletePickingRequest,
  DispatchOrderDetail,
  DispatchOrderPageRequest,
  DispatchOrderSummary,
  DispatchWorkflowErrorCode
} from '@/types/DeliveryManagement/DispatchWorkflow'

export const PENDING_PICK_PRINT_POLICY = Object.freeze({
  usesRequestTimeSnapshot: true,
  expandsAllTasks: true,
  changesStatus: false
})

export interface PendingPickFailureOutcome {
  stayOnPendingPick: true
  refreshList: true
  refreshDetail: true
  emitStatusChanged: false
  messageKey: string
  message?: string
}

export interface PendingPickResponseIdentity {
  requestSeq: number
  latestRequestSeq: number
  requestedWarehouseId: number | null
  currentWarehouseId: number | null
}

const FAILURE_MESSAGE_KEYS: Partial<Record<DispatchWorkflowErrorCode, string>> = {
  STOCK_SHORTAGE: 'wms.deliveryManagement.inventoryShortage',
  SOURCE_CHANGED: 'wms.deliveryManagement.sourceChangeRefresh',
  CONCURRENCY_CONFLICT: 'wms.deliveryManagement.inventoryConflict'
}

const FAILURE_MESSAGES: Partial<Record<DispatchWorkflowErrorCode, string>> = {
  SKU_MAPPING_MISSING: '商品映射缺失',
  SKU_MAPPING_CONFLICT: '商品映射冲突'
}

export const toPendingPickRows = (orders: DispatchOrderSummary[]): DispatchOrderSummary[] => [...orders]

export const shouldAcceptPendingPickResponse = ({
  requestSeq,
  latestRequestSeq,
  requestedWarehouseId,
  currentWarehouseId
}: PendingPickResponseIdentity): boolean =>
  requestedWarehouseId !== null
  && requestSeq === latestRequestSeq
  && currentWarehouseId === requestedWarehouseId

export const buildPendingPickPageRequest = (
  warehouseId: number,
  keyword: string,
  pageIndex: number,
  pageSize: number
): DispatchOrderPageRequest => ({
  status: 'PENDING_PICK',
  warehouse_id: warehouseId,
  keyword: keyword.trim(),
  pageIndex,
  pageSize
})

export const buildPendingPickPrintSnapshot = (detail: DispatchOrderDetail): DispatchOrderDetail => ({
  ...detail,
  packing_task_nos: [...detail.packing_task_nos],
  packing_tasks: detail.packing_tasks.map((task) => ({
    ...task,
    items: task.items.map((item) => ({ ...item }))
  }))
})

export const buildCompletePickingPayload = (
  order: Pick<DispatchOrderSummary, 'row_version'>,
  requestId: string
): CompletePickingRequest => ({
  request_id: requestId,
  row_version: order.row_version
})

export const getPendingPickFailureOutcome = (errorCode: string): PendingPickFailureOutcome => {
  const workflowErrorCode = errorCode as DispatchWorkflowErrorCode
  const message = FAILURE_MESSAGES[workflowErrorCode]
  return {
    stayOnPendingPick: true,
    refreshList: true,
    refreshDetail: true,
    emitStatusChanged: false,
    messageKey: FAILURE_MESSAGE_KEYS[workflowErrorCode] ?? 'wms.deliveryManagement.sourceChangeRefresh',
    ...(message ? { message } : {})
  }
}
