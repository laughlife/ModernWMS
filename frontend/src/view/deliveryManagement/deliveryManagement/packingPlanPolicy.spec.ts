import { describe, expect, it } from 'vitest'
import type { PackingPlan, PackingPlanBox } from '@/types/DeliveryManagement/DispatchWorkflow'
import {
  expandPackingPlanBoxes,
  normalizePlannedBoxCount,
  plannedBoxCountDigits
} from './packingPlanPolicy'

const box = (sequence: number, taskQty: number): PackingPlanBox => ({
  id: sequence,
  client_key: `box-${sequence}`,
  box_sequence: sequence,
  weight: null,
  length: null,
  width: null,
  height: null,
  row_version: 0,
  items: [{ packing_task_item_id: 11, task_qty: taskQty }]
})

const plan = (boxes: PackingPlanBox[] = [box(1, 3)]): PackingPlan => ({
  order_id: 1,
  packing_task_id: 2,
  packing_task_no: 'CW-1',
  packing_plan_status: 'DRAFT',
  row_version: 0,
  task_row_version: 0,
  items: [{
    id: 11,
    commodity_sku: 'SKU-1',
    commodity_name: '商品1',
    fn_sku: 'FNSKU-1',
    msku: 'MSKU-1',
    main_image: '',
    task_qty: 5,
    variant_qty: 1,
    required_qty: 5,
    actual_packed_task_qty: null,
    actual_packed_required_qty: null
  }],
  boxes
})

describe('计划箱数', () => {
  it('输入内容仅保留数字且最小值为1', () => {
    expect(plannedBoxCountDigits('1a2')).toBe('12')
    expect(plannedBoxCountDigits('')).toBe('')
    expect(normalizePlannedBoxCount('0')).toBe(1)
    expect(normalizePlannedBoxCount('6')).toBe(6)
  })

  it('填写n后自动补齐第2到第n箱并沿用剩余任务量规则', () => {
    const packingPlan = plan()

    const expanded = expandPackingPlanBoxes(packingPlan, 3)

    expect(expanded).toHaveLength(3)
    expect(expanded[0]).toBe(packingPlan.boxes[0])
    expect(expanded.map((item) => item.box_sequence)).toEqual([1, 2, 3])
    expect(expanded[1].items).toEqual([{ packing_task_item_id: 11, task_qty: 2 }])
    expect(expanded[2].items).toEqual([{ packing_task_item_id: 11, task_qty: 0 }])
  })

  it('计划数调小时不删除已有箱', () => {
    const packingPlan = plan([box(1, 3), box(2, 2), box(3, 0)])

    expect(expandPackingPlanBoxes(packingPlan, 1)).toHaveLength(3)
  })
})
