import { describe, expect, it } from 'vitest'
import { buildDeliveryPayload, buildSingleDeliveryPayload, getOutboundStatusQuery, getOutboundSuccessAction } from './outboundFlow'
import type { DeliveryManagementDetailVO } from '@/types/DeliveryManagement/DeliveryManagement'

describe('outbound workflow', () => {
  it('keeps pending and completed outbound lists on distinct statuses', () => {
    expect(getOutboundStatusQuery('pending')).toBe('dispatch_status=5')
    expect(getOutboundStatusQuery('completed')).toBe('dispatch_status=6')
  })

  it('builds a single-row outbound request', () => {
    expect(buildSingleDeliveryPayload({
      id: 18,
      dispatch_no: 'DB20260811001',
      dispatch_status: 5,
      picked_qty: 12
    })).toEqual([{
      id: 18,
      dispatch_no: 'DB20260811001',
      dispatch_status: 5,
      picked_qty: 12
    }])
  })

  it('keeps every selected row in a batch outbound request', () => {
    const rows = [
      { id: 1, dispatch_no: 'D001', dispatch_status: 5, picked_qty: 2 },
      { id: 2, dispatch_no: 'D002', dispatch_status: 5, picked_qty: 3 }
    ] as DeliveryManagementDetailVO[]

    expect(buildDeliveryPayload(rows).map(item => item.id)).toEqual([1, 2])
  })

  it('opens completed after a single outbound and keeps batch outbound on the pending list', () => {
    expect(getOutboundSuccessAction('single')).toBe('open-completed')
    expect(getOutboundSuccessAction('batch')).toBe('refresh-pending')
  })
})
