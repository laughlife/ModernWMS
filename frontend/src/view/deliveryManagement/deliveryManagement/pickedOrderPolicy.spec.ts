import { describe, expect, it } from 'vitest'
import type { DispatchOrderSummary } from '@/types/DeliveryManagement/DispatchWorkflow'
import {
  buildPickedOrderDecisionRequest,
  buildStartWeighingRequest,
  canStartPickedOrderWeighing,
  getPickedOrderRowKey,
  isCurrentPickedPageRequest,
  isDecisionReasonValid,
  resolveStartWeighingOutcome
} from './pickedOrderPolicy'

const baseOrder: DispatchOrderSummary = {
  id: 42,
  dispatch_no: 'WMS-42',
  warehouse_id: 320118,
  status: 'PICKED',
  packing_task_nos: ['CW-1', 'CW-2'],
  creator: 'admin',
  create_time: '2026-08-16T08:00:00',
  last_update_time: '2026-08-16T08:10:00',
  source_change_pending: false,
  pending_source_version: '',
  source_change_snapshot: '',
  accepted_source_version: '',
  signed_qty: null,
  damaged_qty: null,
  signed_at: null,
  signed_by_name: '',
  notification_status: 'NONE',
  notification_last_error: '',
  outbound_source_anomaly: false,
  outbound_source_anomaly_snapshot: '',
  row_version: 7,
}

const order = (overrides: Partial<DispatchOrderSummary> = {}): DispatchOrderSummary => ({
  ...baseOrder,
  ...overrides
})

describe('picked order policy', () => {
  it('uses the WMS order id as the row identity', () => {
    expect(getPickedOrderRowKey(order())).toBe(42)
  })

  it('freezes weighing only while a source decision is pending', () => {
    expect(canStartPickedOrderWeighing(order())).toBe(true)
    expect(canStartPickedOrderWeighing(order({ source_change_pending: true }))).toBe(false)
  })

  it('requires a non-blank human reason', () => {
    expect(isDecisionReasonValid('')).toBe(false)
    expect(isDecisionReasonValid('   ')).toBe(false)
    expect(isDecisionReasonValid('人工确认按WMS快照继续')).toBe(true)
  })

  it.each(['CONTINUE', 'CANCEL'] as const)('builds the %s decision payload with pending version and row version', (decision) => {
    expect(buildPickedOrderDecisionRequest({
      order: order({ source_change_pending: true, pending_source_version: 'source-v2', row_version: 9 }),
      decision,
      reason: '  人工复核完成  ',
      requestId: 'decision-1'
    })).toEqual({
      decision,
      source_version: 'source-v2',
      reason: '人工复核完成',
      request_id: 'decision-1',
      row_version: 9
    })
  })

  it('rejects a decision without reason or pending source version', () => {
    expect(() => buildPickedOrderDecisionRequest({
      order: order({ source_change_pending: true, pending_source_version: 'source-v2' }),
      decision: 'CONTINUE',
      reason: ' ',
      requestId: 'decision-1'
    })).toThrow('reason is required')
    expect(() => buildPickedOrderDecisionRequest({
      order: order({ source_change_pending: true, pending_source_version: '' }),
      decision: 'CANCEL',
      reason: '人工取消',
      requestId: 'decision-2'
    })).toThrow('pending source version is required')
  })

  it('starts weighing with order row version and never uses a shipment identity', () => {
    expect(buildStartWeighingRequest(order({ row_version: 12 }), 'start-1')).toEqual({
      request_id: 'start-1',
      row_version: 12
    })
  })

  it('moves tabs only after start weighing succeeds', () => {
    expect(resolveStartWeighingOutcome({
      isSuccess: true,
      code: 0,
      errorMessage: '',
      data: { order_id: 42, request_id: 'start-1', row_version: 13, status: 'WEIGHING' }
    })).toBe('go-weighing')
    expect(resolveStartWeighingOutcome({
      isSuccess: false,
      code: 409,
      errorMessage: 'SOURCE_CHANGE_PENDING',
      data: null
    })).toBe('source-decision')
    expect(resolveStartWeighingOutcome({
      isSuccess: false,
      code: 409,
      errorMessage: 'CONCURRENCY_CONFLICT',
      data: null
    })).toBe('stay')
  })

  it.each([
    { sequence: 2 },
    { warehouseId: 8 },
    { keyword: 'CW-2' },
    { pageIndex: 2 },
    { pageSize: 50 }
  ])('rejects a stale page response when request identity changes: %o', (change) => {
    const request = { sequence: 1, warehouseId: 320118, keyword: 'CW-1', pageIndex: 1, pageSize: 20 }
    expect(isCurrentPickedPageRequest(request, { ...request, ...change })).toBe(false)
  })

  it('accepts only the exact current page request identity', () => {
    const request = { sequence: 3, warehouseId: 320118, keyword: 'CW-1', pageIndex: 2, pageSize: 50 }
    expect(isCurrentPickedPageRequest(request, { ...request })).toBe(true)
  })
})
