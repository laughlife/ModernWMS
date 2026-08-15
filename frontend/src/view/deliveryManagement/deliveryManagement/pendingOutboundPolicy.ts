import type {
  DispatchOrderDetail,
  DispatchOrderPageRequest,
  DispatchOrderStatus,
  OutboundCommandRequest,
  SourceDecisionRequest,
  WeighingBox
} from '@/types/DeliveryManagement/DispatchWorkflow'

export interface PendingOutboundMetrics {
  taskCount: number
  skuLineCount: number
  totalQty: number
  expectedBoxCount: number
  measuredBoxCount: number
  totalWeight: number
  totalVolumeCubicMeters: number
}

export interface PendingOutboundLoadState<T> {
  tableData: T[]
  total: number
}

export interface LatestRequestGuard {
  begin: () => number
  isCurrent: (requestId: number) => boolean
}

export const createLatestRequestGuard = (): LatestRequestGuard => {
  let latestRequestId = 0
  return {
    begin: () => ++latestRequestId,
    isCurrent: requestId => requestId === latestRequestId
  }
}

export const beginPendingOutboundLoad = <T>(
  state: PendingOutboundLoadState<T>,
  guard: LatestRequestGuard
): number => {
  state.tableData = []
  state.total = 0
  return guard.begin()
}

export const buildPendingOutboundPageRequest = (
  warehouseId: number,
  keyword: string,
  pageIndex: number,
  pageSize: number
): DispatchOrderPageRequest => ({
  status: 'PENDING_OUTBOUND',
  warehouse_id: warehouseId,
  keyword: keyword.trim(),
  pageIndex,
  pageSize
})

export const getPendingOutboundMetrics = (
  order: DispatchOrderDetail,
  boxesByTask: Record<number, WeighingBox[]>
): PendingOutboundMetrics => {
  const boxes = order.packing_tasks.flatMap(task => boxesByTask[task.id] ?? [])
  const volumeCm3 = boxes.reduce((total, box) => total
    + Number(box.length ?? 0) * Number(box.width ?? 0) * Number(box.height ?? 0), 0)
  return {
    taskCount: order.packing_tasks.length,
    skuLineCount: order.packing_tasks.reduce((total, task) => total + task.items.length, 0),
    totalQty: order.packing_tasks.reduce(
      (total, task) => total + task.items.reduce((taskTotal, item) => taskTotal + Number(item.required_qty ?? 0), 0),
      0
    ),
    expectedBoxCount: order.packing_tasks.reduce((total, task) => total + task.expected_box_count, 0),
    measuredBoxCount: order.packing_tasks.reduce((total, task) => total + task.measured_box_count, 0),
    totalWeight: boxes.reduce((total, box) => total + Number(box.weight ?? 0), 0),
    totalVolumeCubicMeters: Number((volumeCm3 / 1_000_000).toFixed(6))
  }
}

export const isPendingOutboundReady = (order: DispatchOrderDetail): boolean =>
  order.status === 'PENDING_OUTBOUND'
  && !order.source_change_pending
  && order.packing_tasks.length > 0
  && order.packing_tasks.every(task => task.expected_box_count > 0 && task.measured_box_count >= task.expected_box_count)

export const buildConfirmOutboundCommand = (
  order: Pick<DispatchOrderDetail, 'row_version'>,
  requestId: string
): OutboundCommandRequest => ({ request_id: requestId, row_version: order.row_version })

export const buildSourceDecisionCommand = (
  order: Pick<DispatchOrderDetail, 'pending_source_version' | 'row_version'>,
  decision: 'CONTINUE' | 'CANCEL',
  reason: string,
  requestId: string
): SourceDecisionRequest => {
  const normalizedReason = reason.trim()
  if (!normalizedReason) throw new Error('reason is required')
  const pendingSourceVersion = order.pending_source_version.trim()
  if (!pendingSourceVersion) throw new Error('pending source version is required')
  return {
    decision,
    source_version: pendingSourceVersion,
    reason: normalizedReason,
    request_id: requestId,
    row_version: order.row_version
  }
}

export const shouldOpenCompleted = (isSuccess: boolean, status?: DispatchOrderStatus): boolean =>
  isSuccess && status === 'OUTBOUND'
