import { describe, expect, it } from 'vitest'
import type { PackingPlan, PackingPlanBox } from '@/types/DeliveryManagement/DispatchWorkflow'
import {
  canConfirmPackingPlan,
  expandPackingPlanBoxes,
  inspectActualPackingLines,
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
  items: taskQty > 0 ? [{
    client_line_key: `line-${sequence}`,
    packing_task_item_id: 11,
    stock_allocation_id: 101,
    erp_stock_id: 1001,
    wms_sku_id: 7,
    goods_owner_id: 88,
    goods_location_id: 66,
    sku_code: 'OTHER-SKU',
    commodity_name: '其他货主商品',
    available_qty: -20,
    actual_qty: taskQty,
    dispatchpicklist_id: null
  }] : []
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

  it('填写n后自动补齐第2到第n箱且不伪造实际商品', () => {
    const packingPlan = plan()

    const expanded = expandPackingPlanBoxes(packingPlan, 3)

    expect(expanded).toHaveLength(3)
    expect(expanded[0]).toBe(packingPlan.boxes[0])
    expect(expanded.map((item) => item.box_sequence)).toEqual([1, 2, 3])
    expect(expanded[1].items).toEqual([])
    expect(expanded[2].items).toEqual([])
  })

  it('计划数调小时不删除已有箱', () => {
    const packingPlan = plan([box(1, 3), box(2, 2), box(3, 0)])

    expect(expandPackingPlanBoxes(packingPlan, 1)).toHaveLength(3)
  })
})

describe('实际装箱规则', () => {
  it('允许超计划、不同SKU、其他货主和负可用库存', () => {
    const packingPlan = plan([{
      ...box(1, 501),
      weight: 1,
      length: 10,
      width: 10,
      height: 10
    }])

    expect(inspectActualPackingLines(packingPlan).issues).toEqual([])
    expect(inspectActualPackingLines(packingPlan).warnings).toContain('OTHER-SKU 当前可用-20，本箱填写501，确认后可能形成负库存')
    expect(canConfirmPackingPlan(packingPlan)).toBe(true)
  })

  it('允许任务外商品但拒绝重复行键和非正数量', () => {
    const packingPlan = plan([{
      ...box(1, 1),
      weight: 1,
      length: 10,
      width: 10,
      height: 10
    }])
    const original = packingPlan.boxes[0].items[0]
    packingPlan.boxes[0].items = [
      { ...original, packing_task_item_id: null, actual_qty: 0 },
      { ...original, packing_task_item_id: null }
    ]

    expect(inspectActualPackingLines(packingPlan).issues).toEqual([
      '第1箱实际商品数量必须为正整数',
      '第1箱实际商品行键重复'
    ])
    expect(canConfirmPackingPlan(packingPlan)).toBe(false)
  })
})
