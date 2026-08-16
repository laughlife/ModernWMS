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

  it('keeps workflow badge quantities when the packing-task counter is temporarily unavailable', async () => {
    const counts = await loadDeliveryStatusCounts({
      loadPackingTaskCount: async () => { throw new Error('packing source unavailable') },
      loadWorkflowCounts: async () => ({ PENDING_PICK: 7, PICKED: 2 }),
      fallbackCounts: {
        tabFbaShipment: 4,
        tabGoodsToBePicked: 6,
        tabPicked: 1,
        tabWeighed: 0,
        tabDelivered: 0,
        tabCompleted: 0
      }
    })

    expect(counts.tabFbaShipment).toBe(4)
    expect(counts.tabGoodsToBePicked).toBe(7)
    expect(counts.tabPicked).toBe(2)
  })

  it('keeps the last workflow badge quantities when only workflow counts fail', async () => {
    const counts = await loadDeliveryStatusCounts({
      loadPackingTaskCount: async () => 9,
      loadWorkflowCounts: async () => { throw new Error('workflow unavailable') },
      fallbackCounts: {
        tabFbaShipment: 4,
        tabGoodsToBePicked: 7,
        tabPicked: 2,
        tabWeighed: 3,
        tabDelivered: 1,
        tabCompleted: 5
      }
    })

    expect(counts).toEqual({
      tabFbaShipment: 9,
      tabGoodsToBePicked: 7,
      tabPicked: 2,
      tabWeighed: 3,
      tabDelivered: 1,
      tabCompleted: 5
    })
  })
})
