import type { DeliveryManagementDetailVO, DeliveryVO } from '@/types/DeliveryManagement/DeliveryManagement'

export type OutboundStage = 'pending' | 'completed'
export type OutboundSubmissionScope = 'single' | 'batch'
export type OutboundSuccessAction = 'open-completed' | 'refresh-pending'

export const getOutboundStatusQuery = (stage: OutboundStage) =>
  `dispatch_status=${stage === 'pending' ? 5 : 6}`

type SingleDeliveryRow = Pick<DeliveryManagementDetailVO, 'id' | 'dispatch_no' | 'dispatch_status' | 'picked_qty'>

export const buildSingleDeliveryPayload = (row: SingleDeliveryRow): DeliveryVO[] => [{
  id: row.id,
  dispatch_no: row.dispatch_no,
  dispatch_status: row.dispatch_status,
  picked_qty: row.picked_qty
}]

export const buildDeliveryPayload = (rows: SingleDeliveryRow[]): DeliveryVO[] =>
  rows.flatMap(row => buildSingleDeliveryPayload(row))

export const getOutboundSuccessAction = (scope: OutboundSubmissionScope): OutboundSuccessAction =>
  scope === 'single' ? 'open-completed' : 'refresh-pending'
