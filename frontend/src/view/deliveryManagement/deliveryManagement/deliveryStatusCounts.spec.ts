import { describe, expect, it } from 'vitest'
import { loadDeliveryStatusCounts, mapWorkflowCountsToTabs } from './deliveryStatusCounts'

describe('delivery status counts', () => {
  it('loads every visible unfinished status count', async () => {
    const counts = await loadDeliveryStatusCounts({
      loadPackingTaskCount: async () => 2,
      loadWorkflowCounts: async () => ({
        PENDING_PICK: 3,
        PICKED: 1,
        WEIGHING: 4,
        PENDING_OUTBOUND: 5,
        OUTBOUND: 6
      })
    })

    expect(counts).toEqual({
      tabFbaShipment: 2,
      tabGoodsToBePicked: 3,
      tabPicked: 1,
      tabWeighed: 4,
      tabDelivered: 5,
      tabCompleted: 6
    })
  })

  it('maps one backend order status to one workflow tab', () => {
    expect(mapWorkflowCountsToTabs({
      PENDING_PICK: 3,
      PICKED: 1,
      WEIGHING: 4,
      PENDING_OUTBOUND: 5,
      OUTBOUND: 99,
      SOURCE_CANCELLED: 80,
      MANUAL_CANCELLED: 70
    }, 2)).toEqual({
      tabFbaShipment: 2,
      tabGoodsToBePicked: 3,
      tabPicked: 1,
      tabWeighed: 4,
      tabDelivered: 5,
      tabCompleted: 99
    })
  })

  it('normalizes missing or invalid backend counts to zero', () => {
    expect(mapWorkflowCountsToTabs({ PENDING_PICK: -1 }, Number.NaN)).toEqual({
      tabFbaShipment: 0,
      tabGoodsToBePicked: 0,
      tabPicked: 0,
      tabWeighed: 0,
      tabDelivered: 0,
      tabCompleted: 0
    })
  })
})
