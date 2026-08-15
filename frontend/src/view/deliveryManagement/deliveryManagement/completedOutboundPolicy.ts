import type {
  DispatchOrderDetail,
  DispatchOrderPageRequest,
  DispatchOrderSummary,
  DispatchSignNotificationStatus,
  OutboundCommandRequest,
  SignDispatchOrderRequest,
  WeighingBox
} from '@/types/DeliveryManagement/DispatchWorkflow'

export type CompletedOrderRow = DispatchOrderSummary

export interface CompletedPageRequestToken {
  sequence: number
  warehouseId: number
}

export interface CompletedRowContext extends CompletedPageRequestToken {
  orderId: number
  rowVersion: number
}

export const isCompletedPageRequestCurrent = (
  token: CompletedPageRequestToken,
  currentSequence: number,
  currentWarehouseId: number | null
): boolean => token.sequence === currentSequence && token.warehouseId === currentWarehouseId

export const isCompletedRowContextCurrent = (
  context: CompletedRowContext,
  currentSequence: number,
  currentWarehouseId: number | null,
  currentRows: ReadonlyArray<Pick<DispatchOrderSummary, 'id' | 'warehouse_id' | 'row_version'>>
): boolean => isCompletedPageRequestCurrent(context, currentSequence, currentWarehouseId)
  && currentRows.some(row => row.id === context.orderId
    && row.warehouse_id === context.warehouseId
    && row.row_version === context.rowVersion)

export const emptyCompletedPage = (): { rows: CompletedOrderRow[]; total: number } => ({
  rows: [],
  total: 0
})

export type ReadonlyWeighingBox = Readonly<WeighingBox> & { readonly: true }

export type CompletedTaskDetail = DispatchOrderDetail['packing_tasks'][number] & {
  boxes: ReadonlyWeighingBox[]
}

export const buildCompletedPageRequest = (
  warehouseId: number,
  keyword: string,
  pageIndex: number,
  pageSize: number
): DispatchOrderPageRequest => ({
  status: 'OUTBOUND',
  warehouse_id: warehouseId,
  keyword: keyword.trim(),
  pageIndex,
  pageSize
})

export const formatPackingTaskNumbers = (order: Pick<DispatchOrderSummary, 'packing_task_nos'>): string =>
  order.packing_task_nos.join('、') || '-'

export const groupCompletedOrderDetails = (
  detail: DispatchOrderDetail,
  boxesByTaskId: ReadonlyMap<number, WeighingBox[]>
): CompletedTaskDetail[] => detail.packing_tasks.map(task => ({
  ...task,
  boxes: (boxesByTaskId.get(task.id) ?? []).map(box => ({ ...box, readonly: true as const }))
}))

export const buildCancelOutboundCommand = (
  order: DispatchOrderSummary,
  requestId: string
): { orderId: number; request: OutboundCommandRequest } => ({
  orderId: order.id,
  request: { request_id: requestId, row_version: order.row_version }
})

export const buildSignCommand = (
  order: DispatchOrderSummary,
  damagedQty: number,
  requestId: string
): { orderId: number; request: SignDispatchOrderRequest } => ({
  orderId: order.id,
  request: { request_id: requestId, row_version: order.row_version, damaged_qty: damagedQty }
})

export const canCancelOutbound = (order: CompletedOrderRow): boolean => !order.signed_at

export const notificationCanRetry = (status: DispatchSignNotificationStatus): boolean => status === 'FAILED'

export const sourceAnomalyPresentation = (
  order: Pick<DispatchOrderSummary, 'status' | 'outbound_source_anomaly' | 'outbound_source_anomaly_snapshot'>
): { status: 'OUTBOUND'; warning: boolean; snapshot: string } => ({
  status: 'OUTBOUND',
  warning: order.outbound_source_anomaly,
  snapshot: order.outbound_source_anomaly_snapshot
})
