import { describe, expect, it } from 'vitest'
import type { DispatchOrderDetail, PackingPlanBoxItem, WeighingBox } from '@/types/DeliveryManagement/DispatchWorkflow'
import {
  buildConfirmOutboundCommand,
  beginPendingOutboundLoad,
  buildPendingOutboundPageRequest,
  buildSourceDecisionCommand,
  createLatestRequestGuard,
  getPendingOutboundMetrics,
  isPendingOutboundReady,
  shouldOpenCompleted
} from './pendingOutboundPolicy'

const order = (overrides: Partial<DispatchOrderDetail> = {}): DispatchOrderDetail => ({
  id: 31,
  dispatch_no: 'WMS-31',
  warehouse_id: 320118,
  status: 'PENDING_OUTBOUND',
  packing_task_nos: ['CW001', 'CW002'],
  creator: 'admin',
  create_time: '2026-08-16 10:00:00',
  last_update_time: '2026-08-16 10:10:00',
  source_change_pending: false,
  pending_source_version: '',
  source_change_snapshot: '',
  accepted_source_version: 'source-v3',
  outbound_source_anomaly: false,
  outbound_source_anomaly_snapshot: '',
  signed_qty: null,
  damaged_qty: null,
  signed_at: null,
  signed_by_name: '',
  notification_status: 'NONE',
  notification_last_error: '',
  row_version: 7,
  source_version: 'source-v3',
  packing_tasks: [
    {
      id: 101,
      source_task_id: 1001,
      source_task_no: 'CW001',
      status: 'ACTIVE',
      source_version: 'v1',
      expected_box_count: 2,
      measured_box_count: 2,
      items: [
        { id: 1, source_item_id: 11, source_commodity_id: null, wms_sku_id: 21, commodity_sku: 'SKU-A', commodity_name: 'A', main_image: '', fn_sku: 'FN-A', msku: 'M-A', task_qty: 3, required_qty: 3, source_stock_available: null },
        { id: 2, source_item_id: 12, source_commodity_id: null, wms_sku_id: 22, commodity_sku: 'SKU-B', commodity_name: 'B', main_image: '', fn_sku: 'FN-B', msku: 'M-B', task_qty: 4, required_qty: 4, source_stock_available: null }
      ]
    },
    {
      id: 102,
      source_task_id: 1002,
      source_task_no: 'CW002',
      status: 'ACTIVE',
      source_version: 'v1',
      expected_box_count: 1,
      measured_box_count: 1,
      items: [
        { id: 3, source_item_id: 13, source_commodity_id: null, wms_sku_id: 21, commodity_sku: 'SKU-A', commodity_name: 'A', main_image: '', fn_sku: 'FN-A', msku: 'M-A', task_qty: 1, required_qty: 5, source_stock_available: null }
      ]
    }
  ],
  ...overrides
})

type BoxWithItems = WeighingBox & { items: PackingPlanBoxItem[] }

const actualLine = (line: number, packingTaskItemId: number, actualQty: number): PackingPlanBoxItem => ({
  client_line_key: `line-${line}`,
  packing_task_item_id: packingTaskItemId,
  erp_stock_id: 1000 + line,
  sku_code: `SKU-${line}`,
  commodity_name: `商品-${line}`,
  available_qty: 100,
  actual_qty: actualQty,
  dispatchpicklist_id: null
})

const boxes: Record<number, BoxWithItems[]> = {
  101: [
    { id: 1, packing_task_id: 101, source_box_identity: 'A-1', box_sequence: 1, weight: 10, length: 40, width: 30, height: 20, measurement_status: 'MEASURED', copied_from_box_id: null, row_version: 1, items: [actualLine(1, 1, 3)] },
    { id: 2, packing_task_id: 101, source_box_identity: 'A-2', box_sequence: 2, weight: 12, length: 50, width: 30, height: 20, measurement_status: 'MEASURED', copied_from_box_id: null, row_version: 1, items: [actualLine(2, 2, 4)] }
  ],
  102: [
    { id: 3, packing_task_id: 102, source_box_identity: 'B-1', box_sequence: 1, weight: 8, length: 30, width: 20, height: 10, measurement_status: 'MEASURED', copied_from_box_id: null, row_version: 1, items: [actualLine(3, 3, 5)] }
  ]
}

describe('pendingOutboundPolicy', () => {
  it('requests one WMS order row from the pending outbound workflow page', () => {
    expect(buildPendingOutboundPageRequest(320118, 'CW001', 2, 20)).toEqual({
      status: 'PENDING_OUTBOUND', warehouse_id: 320118, keyword: 'CW001', pageIndex: 2, pageSize: 20
    })
  })

  it('keeps task and SKU facts separate while computing order totals', () => {
    expect(getPendingOutboundMetrics(order(), boxes)).toEqual({
      taskCount: 2,
      skuLineCount: 3,
      totalQty: 12,
      plannedLoadingQty: 12,
      actualLoadingQty: 12,
      loadingQtyMismatch: false,
      expectedBoxCount: 3,
      measuredBoxCount: 3,
      totalWeight: 30,
      totalVolumeCubicMeters: 0.06
    })
  })

  it('marks the loading quantities as mismatched when physical boxes contain fewer products', () => {
    const incompleteBoxes = structuredClone(boxes)
    incompleteBoxes[101][0].items[0].actual_qty = 2

    expect(getPendingOutboundMetrics(order(), incompleteBoxes)).toMatchObject({
      plannedLoadingQty: 12,
      actualLoadingQty: 11,
      loadingQtyMismatch: true
    })
  })

  it('requires measured tasks and no unresolved source change', () => {
    expect(isPendingOutboundReady(order())).toBe(true)
    expect(isPendingOutboundReady(order({ source_change_pending: true }))).toBe(false)
    const incomplete = order()
    incomplete.packing_tasks[0].measured_box_count = 1
    expect(isPendingOutboundReady(incomplete)).toBe(false)
  })

  it('builds whole-order commands with request id and row version', () => {
    expect(buildConfirmOutboundCommand(order(), 'req-outbound')).toEqual({ request_id: 'req-outbound', row_version: 7 })
    const changedOrder = order({ source_version: 'accepted-v3', pending_source_version: 'changed-v4' })
    expect(buildSourceDecisionCommand(changedOrder, 'CONTINUE', '  checked by warehouse  ', 'req-decision')).toEqual({
      decision: 'CONTINUE', source_version: 'changed-v4', reason: 'checked by warehouse', request_id: 'req-decision', row_version: 7
    })
    expect(() => buildSourceDecisionCommand(changedOrder, 'CANCEL', ' ', 'req-decision')).toThrow('reason is required')
    expect(() => buildSourceDecisionCommand(order(), 'CANCEL', 'cancelled', 'req-decision')).toThrow('pending source version is required')
  })

  it('clears the previous warehouse immediately and rejects stale responses', () => {
    const guard = createLatestRequestGuard()
    const state = { tableData: [{ id: 1 }], total: 1 }
    const warehouseARequest = beginPendingOutboundLoad(state, guard)
    expect(state).toEqual({ tableData: [], total: 0 })

    state.tableData = [{ id: 2 }]
    state.total = 1
    const warehouseBRequest = beginPendingOutboundLoad(state, guard)
    expect(state).toEqual({ tableData: [], total: 0 })
    expect(guard.isCurrent(warehouseARequest)).toBe(false)
    expect(guard.isCurrent(warehouseBRequest)).toBe(true)
  })

  it('opens completed only after a successful OUTBOUND response', () => {
    expect(shouldOpenCompleted(true, 'OUTBOUND')).toBe(true)
    expect(shouldOpenCompleted(false, 'OUTBOUND')).toBe(false)
    expect(shouldOpenCompleted(true, 'PENDING_OUTBOUND')).toBe(false)
  })
})
