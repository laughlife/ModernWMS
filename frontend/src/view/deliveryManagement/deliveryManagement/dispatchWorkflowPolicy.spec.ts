import { describe, expect, it, vi } from 'vitest'
import {
  getDispatchStatusRefreshTargets,
  getDispatchStatusTab,
  getSourceErrorActions,
  getDispatchOrderRowKey,
  getOutboundSourceAnomalySnapshot,
  loadWarehouseAccessSafely,
  resolveDefaultWarehouseId
} from './dispatchWorkflowPolicy'

describe('dispatch workflow policy', () => {
  it.each([
    ['PENDING_PICK', 'tabGoodsToBePicked'],
    ['PICKED', 'tabPicked'],
    ['WEIGHING', 'tabWeighed'],
    ['PENDING_OUTBOUND', 'tabDelivered'],
    ['OUTBOUND', 'tabCompleted']
  ] as const)('maps %s to %s', (status, tab) => {
    expect(getDispatchStatusTab(status)).toBe(tab)
  })

  it.each(['SOURCE_CANCELLED', 'MANUAL_CANCELLED'] as const)(
    'keeps terminal status %s outside the five workflow tabs and refreshes both affected lists',
    (status) => {
      expect(getDispatchStatusTab(status)).toBeNull()
      expect(getDispatchStatusRefreshTargets(status)).toEqual(['packingTasks', 'currentTab'])
    }
  )

  it('offers explicit human decisions only for a pending source change', () => {
    expect(getSourceErrorActions('SOURCE_CHANGE_PENDING')).toEqual(['continue', 'cancel'])
    expect(getSourceErrorActions('SOURCE_VERSION_CONFLICT')).toEqual(['refresh'])
    expect(getSourceErrorActions('CONCURRENCY_CONFLICT')).toEqual(['refresh'])
  })

  it('uses only the backend-provided default warehouse', () => {
    const access = {
      warehouses: [{ id: 320118, name: '深圳自建仓' }, { id: 8, name: '备用仓' }],
      default_warehouse_id: 320118
    }

    expect(resolveDefaultWarehouseId(access)).toBe(320118)
    expect(resolveDefaultWarehouseId({ ...access, default_warehouse_id: null })).toBeNull()
    expect(resolveDefaultWarehouseId({ ...access, default_warehouse_id: 99 })).toBeNull()
  })

  it('uses the WMS order id as the single row identity', () => {
    expect(getDispatchOrderRowKey({ id: 42, packing_task_nos: ['CW1', 'CW2'] })).toBe(42)
  })

  it('exposes an outbound source anomaly only when the backend flag is set', () => {
    expect(getOutboundSourceAnomalySnapshot({
      outbound_source_anomaly: true,
      outbound_source_anomaly_snapshot: '{"changed":true}'
    })).toBe('{"changed":true}')
    expect(getOutboundSourceAnomalySnapshot({
      outbound_source_anomaly: false,
      outbound_source_anomaly_snapshot: 'stale'
    })).toBeNull()
  })

  it('keeps warehouse unselected after failure and allows the same action to retry', async () => {
    const access = {
      warehouses: [{ id: 320118, name: '深圳自建仓' }],
      default_warehouse_id: 320118
    }
    const load = vi.fn()
      .mockRejectedValueOnce(new Error('network'))
      .mockResolvedValueOnce({ isSuccess: true, code: 0, errorMessage: '', data: access })

    expect(await loadWarehouseAccessSafely(load)).toEqual({ access: null, hasError: true })
    expect(await loadWarehouseAccessSafely(load)).toEqual({ access, hasError: false })
    expect(load).toHaveBeenCalledTimes(2)
  })
})
