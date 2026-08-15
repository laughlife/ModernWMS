import type {
  DispatchOrderSummary,
  DispatchSourceDecision,
  SourceDecisionRequest,
  WeighingCommandResult,
  WeighingOrderCommandRequest
} from '@/types/DeliveryManagement/DispatchWorkflow'
import type { ApiResult } from '@/types/System/ApiResult'

export type StartWeighingOutcome = 'go-weighing' | 'source-decision' | 'stay'

export interface PickedPageRequestIdentity {
  sequence: number
  warehouseId: number | null
  keyword: string
  pageIndex: number
  pageSize: number
}

export const isCurrentPickedPageRequest = (
  request: PickedPageRequestIdentity,
  current: PickedPageRequestIdentity
): boolean => request.sequence === current.sequence
  && request.warehouseId === current.warehouseId
  && request.keyword === current.keyword
  && request.pageIndex === current.pageIndex
  && request.pageSize === current.pageSize

export const getPickedOrderRowKey = (order: DispatchOrderSummary): number => order.id

export const canStartPickedOrderWeighing = (order: DispatchOrderSummary): boolean =>
  order.status === 'PICKED' && !order.source_change_pending

export const isDecisionReasonValid = (reason: string): boolean => reason.trim().length > 0

export const buildStartWeighingRequest = (
  order: DispatchOrderSummary,
  requestId: string
): WeighingOrderCommandRequest => ({
  request_id: requestId,
  row_version: order.row_version
})

export const buildPickedOrderDecisionRequest = ({
  order,
  decision,
  reason,
  requestId
}: {
  order: DispatchOrderSummary
  decision: DispatchSourceDecision
  reason: string
  requestId: string
}): SourceDecisionRequest => {
  const normalizedReason = reason.trim()
  const normalizedVersion = order.pending_source_version.trim()
  if (!isDecisionReasonValid(normalizedReason)) throw new Error('reason is required')
  if (!normalizedVersion) throw new Error('pending source version is required')

  return {
    decision,
    source_version: normalizedVersion,
    reason: normalizedReason,
    request_id: requestId,
    row_version: order.row_version
  }
}

export const resolveStartWeighingOutcome = (
  result: ApiResult<WeighingCommandResult>
): StartWeighingOutcome => {
  if (result.isSuccess && result.data.status === 'WEIGHING') return 'go-weighing'
  return result.errorMessage === 'SOURCE_CHANGE_PENDING' ? 'source-decision' : 'stay'
}
