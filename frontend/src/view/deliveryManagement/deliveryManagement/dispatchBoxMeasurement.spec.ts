import { describe, expect, it } from 'vitest'
import type { DispatchOrderSummary, WeighingBox } from '@/types/DeliveryManagement/DispatchWorkflow'
import {
  applyCopiedMeasurement,
  buildCopyMeasurementCommand,
  buildSaveMeasurementCommand,
  buildWeighingSourceDecisionCommand,
  getMeasurementCapabilityError,
  isBoxMeasurementComplete,
  isTaskMeasurementComplete,
  isCurrentDialogRequest,
  isCurrentWeighingListRequest,
  mergeRefreshedBoxesPreservingDirtyDrafts
} from './dispatchBoxMeasurement'

const order: DispatchOrderSummary = {
  id: 12,
  dispatch_no: 'WMS-0012',
  warehouse_id: 320118,
  status: 'WEIGHING',
  packing_task_nos: ['CW001'],
  creator: 'admin',
  create_time: '2026-08-16 10:00:00',
  last_update_time: '2026-08-16 10:00:00',
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
  row_version: 9
}

const box = (overrides: Partial<WeighingBox> = {}): WeighingBox => ({
  id: 51,
  packing_task_id: 31,
  source_box_identity: 'CW001:1',
  box_sequence: 1,
  weight: 1.2,
  length: 20,
  width: 15,
  height: 10,
  measurement_status: 'MEASURED',
  copied_from_box_id: null,
  row_version: 3,
  ...overrides
})

describe('dispatch physical-box measurement policy', () => {
  it('requires all four WMS measurements to be positive', () => {
    expect(isBoxMeasurementComplete(box())).toBe(true)
    expect(isBoxMeasurementComplete(box({ weight: 0 }))).toBe(false)
    expect(isBoxMeasurementComplete(box({ length: null }))).toBe(false)
    expect(isTaskMeasurementComplete([box(), box({ id: 52, box_sequence: 2 })])).toBe(true)
    expect(isTaskMeasurementComplete([box(), box({ id: 52, height: 0 })])).toBe(false)
  })

  it('fails closed when a source box lacks a stable identity', () => {
    expect(getMeasurementCapabilityError([])).toBeTruthy()
    expect(getMeasurementCapabilityError([box({ source_box_identity: '' })])).toContain('稳定标识')
    expect(getMeasurementCapabilityError([box()])).toBeNull()
  })

  it('builds save PUT command with order and box row versions', () => {
    expect(buildSaveMeasurementCommand(order, box(), 'save-1')).toEqual({
      orderId: 12,
      boxId: 51,
      payload: {
        request_id: 'save-1',
        row_version: 9,
        box_row_version: 3,
        weight: 1.2,
        length: 20,
        width: 15,
        height: 10
      }
    })
  })

  it('copies only to an existing box in the same packing task without creating a box', () => {
    const source = box()
    const target = box({ id: 52, box_sequence: 2, source_box_identity: 'CW001:2', weight: null,
      length: null, width: null, height: null, measurement_status: 'PENDING', row_version: 7 })
    expect(buildCopyMeasurementCommand(order, source, target, 'copy-1')).toEqual({
      orderId: 12,
      targetBoxId: 52,
      payload: { request_id: 'copy-1', row_version: 9, source_box_id: 51, target_box_row_version: 7 }
    })

    const boxes = [source, target]
    const copied = applyCopiedMeasurement(boxes, source, target)
    expect(copied).toHaveLength(2)
    expect(copied[1]).toMatchObject({ id: 52, copied_from_box_id: 51, weight: 1.2, length: 20, width: 15, height: 10 })
    copied[1].weight = 2.4
    expect(copied[1].weight).toBe(2.4)
    expect(source.weight).toBe(1.2)
  })

  it('rejects cross-task copy and self copy', () => {
    expect(() => buildCopyMeasurementCommand(order, box(), box({ id: 52, packing_task_id: 99 }), 'copy-2'))
      .toThrow('同一装箱任务')
    expect(() => buildCopyMeasurementCommand(order, box(), box(), 'copy-3')).toThrow('目标箱')
  })

  it('preserves another dirty box when saving one box reloads server row versions', () => {
    const current = [box({ id: 51, weight: 1.5 }), box({ id: 52, weight: 8.8, row_version: 2 })]
    const refreshed = [box({ id: 51, weight: 1.5, row_version: 4 }), box({ id: 52, weight: 2.2, row_version: 3 })]
    const merged = mergeRefreshedBoxesPreservingDirtyDrafts(current, refreshed, new Set([51, 52]), 51)
    expect(merged[0]).toMatchObject({ id: 51, weight: 1.5, row_version: 4 })
    expect(merged[1]).toMatchObject({ id: 52, weight: 8.8, row_version: 3 })
  })

  it('refreshes a copied target without changing other dirty box measurements', () => {
    const current = [box(), box({ id: 52, weight: 7.7 }), box({ id: 53, weight: 9.9 })]
    const refreshed = [box(), box({ id: 52, weight: 1.2, copied_from_box_id: 51, row_version: 8 }), box({ id: 53, weight: 3.3, row_version: 5 })]
    const merged = mergeRefreshedBoxesPreservingDirtyDrafts(current, refreshed, new Set([52, 53]), 52)
    expect(merged[1]).toMatchObject({ id: 52, weight: 1.2, copied_from_box_id: 51, row_version: 8 })
    expect(merged[2]).toMatchObject({ id: 53, weight: 9.9, row_version: 5 })
  })

  it('builds a required-reason source decision from pending source version', () => {
    const changed = { ...order, source_change_pending: true, pending_source_version: 'source-v2' }
    expect(buildWeighingSourceDecisionCommand(changed, 'CONTINUE', '  仓库复核后继续  ', 'decision-1')).toEqual({
      decision: 'CONTINUE', source_version: 'source-v2', reason: '仓库复核后继续', request_id: 'decision-1', row_version: 9
    })
    expect(() => buildWeighingSourceDecisionCommand(changed, 'CANCEL', ' ', 'decision-2')).toThrow('处理原因')
  })

  it('rejects stale weighing list success and error after warehouse or query changes', () => {
    const oldRequest = { sequence: 2, warehouseId: 320118, keyword: 'CW-A', pageIndex: 1, pageSize: 20 }
    expect(isCurrentWeighingListRequest(oldRequest, { ...oldRequest })).toBe(true)
    expect(isCurrentWeighingListRequest(oldRequest, { ...oldRequest, sequence: 3 })).toBe(false)
    expect(isCurrentWeighingListRequest(oldRequest, { ...oldRequest, warehouseId: 99 })).toBe(false)
    expect(isCurrentWeighingListRequest(oldRequest, { ...oldRequest, keyword: 'CW-B' })).toBe(false)
    expect(isCurrentWeighingListRequest(oldRequest, { ...oldRequest, pageIndex: 2 })).toBe(false)
  })

  it('rejects stale dialog work after close or opening another order', () => {
    const request = { generation: 4, orderId: 12 }
    expect(isCurrentDialogRequest(request, { generation: 4, orderId: 12, visible: true })).toBe(true)
    expect(isCurrentDialogRequest(request, { generation: 5, orderId: 12, visible: true })).toBe(false)
    expect(isCurrentDialogRequest(request, { generation: 4, orderId: 99, visible: true })).toBe(false)
    expect(isCurrentDialogRequest(request, { generation: 4, orderId: 12, visible: false })).toBe(false)
  })
})
