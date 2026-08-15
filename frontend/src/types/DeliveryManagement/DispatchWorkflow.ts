import type { PackingTaskVO } from './PackingTask'

export type DispatchOrderStatus =
  | 'PENDING_PICK'
  | 'PICKED'
  | 'WEIGHING'
  | 'PENDING_OUTBOUND'
  | 'OUTBOUND'
  | 'SOURCE_CANCELLED'
  | 'MANUAL_CANCELLED'

export type VisibleDispatchOrderStatus = Exclude<
  DispatchOrderStatus,
  'SOURCE_CANCELLED' | 'MANUAL_CANCELLED'
>

export type DispatchSourceDecision = 'CONTINUE' | 'CANCEL'

export type DispatchWorkflowErrorCode =
  | 'SOURCE_CHANGED'
  | 'SOURCE_CHANGE_PENDING'
  | 'SOURCE_VERSION_CONFLICT'
  | 'SOURCE_DECISION_NOT_PENDING'
  | 'STOCK_ALREADY_DEDUCTED'
  | 'SOURCE_BOX_ID_UNSUPPORTED'
  | 'STOCK_SHORTAGE'
  | 'STOCK_CONFLICT'
  | 'CONCURRENCY_CONFLICT'
  | 'IDEMPOTENCY_CONFLICT'
  | 'BOX_NOT_AVAILABLE'
  | 'WEIGHING_INCOMPLETE'
  | 'STATUS_NOT_ALLOWED'
  | 'ORDER_ALREADY_SIGNED'

export interface PageData<T> {
  rows: T[]
  totals: number
}

export interface WarehouseOption {
  id: number
  name: string
}

export interface WarehouseAccess {
  warehouses: WarehouseOption[]
  default_warehouse_id: number | null
}

export interface PackingTaskPageRequest {
  pageIndex: number
  pageSize: number
  searchObjects: Array<{
    name: 'keyword' | 'warehouse_id'
    operator?: number
    text?: string
    value?: string
  }>
}

export type PackingTaskPage = PageData<PackingTaskVO>

export interface CreateDispatchOrderRequest {
  warehouse_id: number
  source_task_ids: number[]
  idempotency_key: string
}

export interface DispatchOrderPageRequest {
  status: VisibleDispatchOrderStatus
  warehouse_id: number
  keyword: string
  pageIndex: number
  pageSize: number
}

export interface DispatchOrderSummary {
  id: number
  dispatch_no: string
  warehouse_id: number
  status: DispatchOrderStatus
  packing_task_nos: string[]
  creator: string
  create_time: string
  last_update_time: string
  source_change_pending: boolean
  row_version: number
}

export interface DispatchPackingTaskItem {
  id: number
  source_item_id: number
  source_commodity_id: number | null
  wms_sku_id: number | null
  commodity_sku: string
  commodity_name: string
  fn_sku: string
  msku: string
  required_qty: number | null
  source_stock_available: number | null
}

export interface DispatchPackingTask {
  id: number
  source_task_id: number
  source_task_no: string
  status: string
  source_version: string
  expected_box_count: number
  measured_box_count: number
  items: DispatchPackingTaskItem[]
}

export interface DispatchOrderDetail extends DispatchOrderSummary {
  source_version: string
  packing_tasks: DispatchPackingTask[]
}

export type DispatchOrderPage = PageData<DispatchOrderSummary>
export type DispatchStatusCounts = Partial<Record<DispatchOrderStatus, number>>

export interface VersionedCommandRequest {
  request_id: string
  row_version: number
}

export interface WorkflowCommandResult extends VersionedCommandRequest {
  order_id: number
  status: DispatchOrderStatus
}

export type CompletePickingRequest = VersionedCommandRequest
export type CompletePickingResult = WorkflowCommandResult
export type WeighingOrderCommandRequest = VersionedCommandRequest
export type WeighingCommandResult = WorkflowCommandResult
export type OutboundCommandRequest = VersionedCommandRequest
export type OutboundCommandResult = WorkflowCommandResult

export interface WeighingBox {
  id: number
  packing_task_id: number
  source_box_identity: string
  box_sequence: number
  weight: number | null
  length: number | null
  width: number | null
  height: number | null
  measurement_status: string
  copied_from_box_id: number | null
  row_version: number
}

export interface SaveWeighingBoxRequest extends VersionedCommandRequest {
  box_row_version: number
  weight: number
  length: number
  width: number
  height: number
}

export interface CopyWeighingBoxRequest extends VersionedCommandRequest {
  source_box_id: number
  target_box_row_version: number
}

export interface SourceDecisionRequest extends VersionedCommandRequest {
  decision: DispatchSourceDecision
  source_version: string
  reason: string
}

export interface SourceDecisionResult extends WorkflowCommandResult {
  decision: string
  source_version: string
  source_change_pending: boolean
}

export interface SignDispatchOrderRequest extends VersionedCommandRequest {
  damaged_qty: number
}

export interface SignDispatchOrderResult extends WorkflowCommandResult {
  signed_qty: number
  damaged_qty: number
  notification_status: string
}
