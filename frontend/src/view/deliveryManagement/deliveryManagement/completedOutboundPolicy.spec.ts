import { describe, expect, it } from 'vitest'
import type { DispatchOrderSummary, WeighingBox } from '@/types/DeliveryManagement/DispatchWorkflow'
import {
  buildCancelOutboundCommand,
  buildCompletedPageRequest,
  buildSignCommand,
  canCancelOutbound,
  emptyCompletedPage,
  formatPackingTaskNumbers,
  groupCompletedOrderDetails,
  isCompletedPageRequestCurrent,
  isCompletedRowContextCurrent,
  notificationCanRetry,
  sourceAnomalyPresentation
} from './completedOutboundPolicy'

const order = (overrides: Partial<DispatchOrderSummary> = {}): DispatchOrderSummary => ({
  id: 18,
  dispatch_no: 'WMS-OUT-18',
  warehouse_id: 320118,
  status: 'OUTBOUND',
  packing_task_nos: ['CW2608150043', 'CW2608150042'],
  creator: 'admin',
  create_time: '2026-08-15 15:00:00',
  last_update_time: '2026-08-15 18:00:00',
  source_change_pending: false,
  outbound_source_anomaly: false,
  outbound_source_anomaly_snapshot: '',
  pending_source_version: '',
  source_change_snapshot: '',
  accepted_source_version: '',
  signed_qty: null,
  damaged_qty: null,
  signed_at: null,
  signed_by_name: '',
  notification_status: 'NONE',
  notification_last_error: '',
  row_version: 7,
  ...overrides
})

describe('completed outbound policy', () => {
  it('keeps one WMS order as one row and searches OUTBOUND by order or task number', () => {
    const rows = [order()]
    expect(rows).toHaveLength(1)
    expect(rows[0].packing_task_nos).toHaveLength(2)
    expect(formatPackingTaskNumbers(rows[0])).toBe('CW2608150043、CW2608150042')
    expect(buildCompletedPageRequest(320118, 'CW2608150042', 2, 30)).toEqual({
      status: 'OUTBOUND', warehouse_id: 320118, keyword: 'CW2608150042', pageIndex: 2, pageSize: 30
    })
  })

  it('preserves task, item and physical-box hierarchy as read-only detail', () => {
    const box: WeighingBox = {
      id: 91, packing_task_id: 31, source_box_identity: 'box-A', box_sequence: 1,
      weight: 12.5, length: 40, width: 30, height: 20,
      measurement_status: 'COMPLETED', copied_from_box_id: null, row_version: 3
    }
    const detail = groupCompletedOrderDetails({
      ...order(),
      source_version: 'v2',
      packing_tasks: [{
        id: 31, source_task_id: 301, source_task_no: 'CW2608150043', status: 'OUTBOUND',
        source_version: 't1', expected_box_count: 1, measured_box_count: 1,
        items: [{
          id: 41, source_item_id: 401, source_commodity_id: 501, wms_sku_id: 601,
          commodity_sku: 'SKU-A', commodity_name: '商品A', fn_sku: 'FN-A', msku: 'MSKU-A',
          required_qty: 8, source_stock_available: 20
        }]
      }]
    }, new Map([[31, [box]]]))

    expect(detail[0].source_task_no).toBe('CW2608150043')
    expect(detail[0].items[0].commodity_sku).toBe('SKU-A')
    expect(detail[0].boxes[0]).toMatchObject({ source_box_identity: 'box-A', readonly: true })
  })

  it('builds whole-order reversal and signing commands with id and concurrency version', () => {
    expect(buildCancelOutboundCommand(order(), 'cancel-18')).toEqual({
      orderId: 18, request: { request_id: 'cancel-18', row_version: 7 }
    })
    expect(buildSignCommand(order(), 2, 'sign-18')).toEqual({
      orderId: 18, request: { request_id: 'sign-18', row_version: 7, damaged_qty: 2 }
    })
  })

  it('forbids reversal after signing and allows failed notification to retry', () => {
    expect(canCancelOutbound(order())).toBe(true)
    expect(canCancelOutbound({ ...order(), signed_at: '2026-08-15 20:00:00' })).toBe(false)
    expect(notificationCanRetry('FAILED')).toBe(true)
    expect(notificationCanRetry('SENT')).toBe(false)
  })

  it('presents a post-outbound source anomaly as warning without changing the OUTBOUND fact', () => {
    expect(sourceAnomalyPresentation(order({
      outbound_source_anomaly: true,
      outbound_source_anomaly_snapshot: '{"changed":true}'
    }))).toEqual({
      status: 'OUTBOUND', warning: true, snapshot: '{"changed":true}'
    })
  })

  it('rejects an out-of-order response and a response from the previous warehouse', () => {
    expect(isCompletedPageRequestCurrent({ sequence: 1, warehouseId: 320118 }, 2, 320118)).toBe(false)
    expect(isCompletedPageRequestCurrent({ sequence: 2, warehouseId: 320118 }, 2, 99)).toBe(false)
    expect(isCompletedPageRequestCurrent({ sequence: 2, warehouseId: 320118 }, 2, 320118)).toBe(true)
  })

  it('keeps the page empty after switching to no warehouse or when the current request fails', () => {
    expect(isCompletedPageRequestCurrent({ sequence: 3, warehouseId: 320118 }, 3, null)).toBe(false)
    expect(emptyCompletedPage()).toEqual({ rows: [], total: 0 })
    expect(emptyCompletedPage().rows).not.toBe(emptyCompletedPage().rows)
  })

  it('rejects late detail after switching warehouse so the signing dialog cannot reopen', () => {
    const context = { sequence: 4, warehouseId: 320118, orderId: 18, rowVersion: 7 }
    expect(isCompletedRowContextCurrent(context, 5, 99, [order({ warehouse_id: 99 })])).toBe(false)
    expect(isCompletedRowContextCurrent(context, 4, 320118, [])).toBe(false)
  })

  it('rejects a reversal confirmation after its row or warehouse context changed', () => {
    const context = { sequence: 6, warehouseId: 320118, orderId: 18, rowVersion: 7 }
    expect(isCompletedRowContextCurrent(context, 6, 99, [order({ warehouse_id: 99 })])).toBe(false)
    expect(isCompletedRowContextCurrent(context, 7, 320118, [order()])).toBe(false)
    expect(isCompletedRowContextCurrent(context, 6, 320118, [order({ row_version: 8 })])).toBe(false)
    expect(isCompletedRowContextCurrent(context, 6, 320118, [order()])).toBe(true)
  })
})
