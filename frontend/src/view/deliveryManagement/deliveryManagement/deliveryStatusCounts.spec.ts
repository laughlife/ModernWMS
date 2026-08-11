import { describe, expect, it } from 'vitest'
import { loadDeliveryStatusCounts } from './deliveryStatusCounts'

describe('delivery status counts', () => {
  it('loads every visible unfinished status count', async () => {
    const counts = await loadDeliveryStatusCounts({
      tabFbaShipment: async () => 2,
      tabGoodsToBePicked: async () => 3,
      tabPicked: async () => 1,
      tabWeighed: async () => 4,
      tabDelivered: async () => 5
    })

    expect(counts).toEqual({
      tabFbaShipment: 2,
      tabGoodsToBePicked: 3,
      tabPicked: 1,
      tabWeighed: 4,
      tabDelivered: 5
    })
  })

  it('omits a failed status so the caller can preserve its previous count', async () => {
    const counts = await loadDeliveryStatusCounts({
      tabFbaShipment: async () => 2,
      tabGoodsToBePicked: async () => { throw new Error('network error') },
      tabPicked: async () => 1,
      tabWeighed: async () => 4,
      tabDelivered: async () => 5
    })

    expect(counts).not.toHaveProperty('tabGoodsToBePicked')
    expect(counts.tabDelivered).toBe(5)
  })
})
