import { describe, expect, it } from 'vitest'
import type { PackingTaskVO } from '@/types/DeliveryManagement/PackingTask'
import {
  buildPackingTaskPageRequest,
  createTaskSetIdempotencyKey,
  removeCreatedPackingTasks,
  resetPackingTaskPageState,
  validatePackingTaskSelection
} from './packingTaskSelection'

const task = (sellfoxTaskId: number, warehouseId: number): PackingTaskVO => ({
  id: sellfoxTaskId,
  sellfox_task_id: sellfoxTaskId,
  packing_task_sn: `CW${sellfoxTaskId}`,
  warehouse_id: warehouseId,
  item_list: []
})

describe('packing task selection', () => {
  it('does not build a source request before the parent selects a warehouse', () => {
    expect(buildPackingTaskPageRequest(null, '', 1, 20)).toBeNull()
  })

  it('always scopes source queries to the parent-provided warehouse', () => {
    expect(buildPackingTaskPageRequest(320118, 'CW-01', 2, 50)).toEqual({
      pageIndex: 2,
      pageSize: 50,
      searchObjects: [
        { name: 'warehouse_id', operator: 1, text: '320118', value: '320118' },
        { name: 'keyword', operator: 1, text: 'CW-01', value: 'CW-01' }
      ]
    })
  })

  it('accepts multiple tasks from the current warehouse and sorts/deduplicates source ids', () => {
    expect(validatePackingTaskSelection([task(12, 320118), task(7, 320118), task(12, 320118)], 320118))
      .toEqual({ ok: true, sourceTaskIds: [7, 12] })
  })

  it('rejects a selection containing another warehouse', () => {
    expect(validatePackingTaskSelection([task(7, 320118), task(8, 9)], 320118))
      .toEqual({ ok: false, reason: 'CROSS_WAREHOUSE' })
  })

  it('creates the backend-compatible deterministic SHA-256 task-set key', async () => {
    expect(await createTaskSetIdempotencyKey([102, 101, 102]))
      .toBe('6874fb31f521eb11f704a287f9a5f001e3e131e09027eadc777e3c2205ef1740')
  })

  it('falls back to an empty key when crypto.subtle is unavailable on HTTP', async () => {
    expect(await createTaskSetIdempotencyKey([102, 101], null)).toBe('')
  })

  it('clears rows, totals and selection together after an unavailable warehouse or request failure', () => {
    const state = {
      tableData: [task(7, 320118)],
      tablePage: { total: 1 },
      selectedTaskCount: 1
    }

    resetPackingTaskPageState(state)

    expect(state).toEqual({ tableData: [], tablePage: { total: 0 }, selectedTaskCount: 0 })
  })

  it('removes successfully created source tasks from the visible source page', () => {
    expect(removeCreatedPackingTasks([task(7, 320118), task(8, 320118)], [7]))
      .toEqual([task(8, 320118)])
  })
})
