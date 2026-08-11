import { describe, expect, it } from 'vitest'
import { getDeliveryFlowAction, getNextDeliveryTab } from './deliveryFlow'

describe('delivery management flow navigation', () => {
  it('moves a completed weighing row to the pending outbound tab', () => {
    expect(getNextDeliveryTab(false)).toBe('tabDelivered')
  })

  it('keeps an incomplete weighing row in the weighing tab', () => {
    expect(getNextDeliveryTab(true)).toBeNull()
  })

  it('confirms status 4 before opening pending outbound', () => {
    expect(getDeliveryFlowAction({ dispatchStatus: 4, isTodo: false })).toBe('confirm-weighing')
  })

  it('opens pending outbound directly for status 5', () => {
    expect(getDeliveryFlowAction({ dispatchStatus: 5, isTodo: false })).toBe('open-pending-outbound')
  })

  it('does nothing while weighing is incomplete', () => {
    expect(getDeliveryFlowAction({ dispatchStatus: 4, isTodo: true })).toBe('stay')
  })
})
