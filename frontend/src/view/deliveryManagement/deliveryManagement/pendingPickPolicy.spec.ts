import { describe, expect, it } from 'vitest'
import type { DispatchOrderDetail, DispatchOrderSummary } from '@/types/DeliveryManagement/DispatchWorkflow'
import {
  PENDING_PICK_PRINT_POLICY,
  buildCompletePickingPayload,
  buildPendingPickPageRequest,
  buildPendingPickPrintSnapshot,
  getPendingPickFailureOutcome,
  shouldAcceptPendingPickResponse,
  toPendingPickRows
} from './pendingPickPolicy'

const summary: DispatchOrderSummary = {
  id: 18,
  dispatch_no: 'WMS-PICK-0018',
  warehouse_id: 320118,
  status: 'PENDING_PICK',
  packing_task_nos: ['CW001', 'CW002'],
  creator: 'admin',
  create_time: '2026-08-16 10:00:00',
  last_update_time: '2026-08-16 10:10:00',
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
  row_version: 7
}

const detail: DispatchOrderDetail = {
  ...summary,
  source_version: 'source-v7',
  packing_tasks: [
    {
      id: 101,
      source_task_id: 1001,
      source_task_no: 'CW001',
      status: 'ACTIVE',
      source_version: 'task-v1',
      expected_box_count: 1,
      measured_box_count: 0,
      items: [{
        id: 10001,
        source_item_id: 20001,
        source_commodity_id: 30001,
        wms_sku_id: 40001,
        commodity_sku: 'SKU-SAME',
        commodity_name: '商品 A',
        fn_sku: 'FN-A',
        msku: 'MSKU-A',
        required_qty: 3,
        source_stock_available: 10
      }]
    },
    {
      id: 102,
      source_task_id: 1002,
      source_task_no: 'CW002',
      status: 'ACTIVE',
      source_version: 'task-v1',
      expected_box_count: 2,
      measured_box_count: 0,
      items: [{
        id: 10002,
        source_item_id: 20002,
        source_commodity_id: 30001,
        wms_sku_id: 40001,
        commodity_sku: 'SKU-SAME',
        commodity_name: '商品 A',
        fn_sku: 'FN-A',
        msku: 'MSKU-A',
        required_qty: 5,
        source_stock_available: 10
      }]
    }
  ]
}

describe('pending pick policy', () => {
  it('keeps one WMS order as one table row even when it contains multiple packing tasks', () => {
    const rows = toPendingPickRows([summary])

    expect(rows).toHaveLength(1)
    expect(rows[0].packing_task_nos).toEqual(['CW001', 'CW002'])
  })

  it('queries only the selected warehouse and PENDING_PICK workflow status', () => {
    expect(buildPendingPickPageRequest(320118, 'CW001', 2, 25)).toEqual({
      status: 'PENDING_PICK',
      warehouse_id: 320118,
      keyword: 'CW001',
      pageIndex: 2,
      pageSize: 25
    })
  })

  it('rejects a slow response from the previous warehouse', () => {
    expect(shouldAcceptPendingPickResponse({
      requestSeq: 1,
      latestRequestSeq: 2,
      requestedWarehouseId: 320118,
      currentWarehouseId: 8
    })).toBe(false)
  })

  it('rejects every in-flight response after warehouse selection is cleared', () => {
    expect(shouldAcceptPendingPickResponse({
      requestSeq: 1,
      latestRequestSeq: 2,
      requestedWarehouseId: 320118,
      currentWarehouseId: null
    })).toBe(false)
  })

  it.each([
    ['search', 3, 4],
    ['page', 8, 9]
  ])('rejects an older %s response even when the warehouse is unchanged', (_operation, requestSeq, latestRequestSeq) => {
    expect(shouldAcceptPendingPickResponse({
      requestSeq,
      latestRequestSeq,
      requestedWarehouseId: 320118,
      currentWarehouseId: 320118
    })).toBe(false)
  })

  it('accepts only the latest response for the warehouse that is still selected', () => {
    expect(shouldAcceptPendingPickResponse({
      requestSeq: 5,
      latestRequestSeq: 5,
      requestedWarehouseId: 320118,
      currentWarehouseId: 320118
    })).toBe(true)
  })

  it('preserves task boundaries and never merges identical SKUs across tasks', () => {
    const snapshot = buildPendingPickPrintSnapshot(detail)

    expect(snapshot.packing_tasks).toHaveLength(2)
    expect(snapshot.packing_tasks[0].items).toEqual([expect.objectContaining({ id: 10001, required_qty: 3 })])
    expect(snapshot.packing_tasks[1].items).toEqual([expect.objectContaining({ id: 10002, required_qty: 5 })])
  })

  it('prints the request-time fully expanded snapshot without a status transition', () => {
    const snapshot = buildPendingPickPrintSnapshot(detail)

    expect(PENDING_PICK_PRINT_POLICY).toEqual({
      usesRequestTimeSnapshot: true,
      expandsAllTasks: true,
      changesStatus: false
    })
    expect(snapshot.packing_tasks.flatMap((task) => task.items)).toHaveLength(2)
    expect(snapshot.status).toBe('PENDING_PICK')
  })

  it('keeps stock shortage and source/concurrency conflicts on pending pick and refreshes current data', () => {
    expect(getPendingPickFailureOutcome('STOCK_SHORTAGE')).toEqual({
      stayOnPendingPick: true,
      refreshList: true,
      refreshDetail: true,
      emitStatusChanged: false,
      messageKey: 'wms.deliveryManagement.inventoryShortage'
    })
    expect(getPendingPickFailureOutcome('SOURCE_CHANGED')).toMatchObject({
      stayOnPendingPick: true,
      refreshList: true,
      emitStatusChanged: false
    })
    expect(getPendingPickFailureOutcome('CONCURRENCY_CONFLICT')).toMatchObject({
      stayOnPendingPick: true,
      refreshList: true,
      emitStatusChanged: false
    })
  })

  it.each([
    ['SKU_MAPPING_MISSING', '商品映射缺失'],
    ['SKU_MAPPING_CONFLICT', '商品映射冲突']
  ])('shows an explicit %s message without misreporting inventory or source changes', (errorCode, message) => {
    const outcome = getPendingPickFailureOutcome(errorCode)

    expect(outcome).toMatchObject({
      stayOnPendingPick: true,
      refreshList: true,
      refreshDetail: true,
      emitStatusChanged: false,
      message
    })
    expect(outcome.message).not.toContain('库存不足')
    expect(outcome.message).not.toContain('来源')
  })

  it('builds the whole-order complete-picking payload from request id and current row version', () => {
    expect(buildCompletePickingPayload(summary, 'pick-request-18')).toEqual({
      request_id: 'pick-request-18',
      row_version: 7
    })
  })
})
