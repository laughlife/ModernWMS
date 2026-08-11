import { describe, expect, it } from 'vitest'
import { getNextDeliveryTab } from './deliveryFlow'

describe('delivery management flow navigation', () => {
  it('moves a completed weighing row to the pending outbound tab', () => {
    expect(getNextDeliveryTab(false)).toBe('tabDelivered')
  })

  it('keeps an incomplete weighing row in the weighing tab', () => {
    expect(getNextDeliveryTab(true)).toBeNull()
  })
})
