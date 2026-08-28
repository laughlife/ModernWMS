import type { PackingPlan, PackingPlanBox, PackingPlanItem } from '@/types/DeliveryManagement/DispatchWorkflow'

export const allocatedTaskQty = (plan: PackingPlan, itemId: number): number =>
  plan.boxes.flatMap((box) => box.items).filter((item) => item.packing_task_item_id === itemId)
    .reduce((sum, item) => sum + Number(item.actual_qty || 0), 0)

export const itemTaskLimit = (_plan: PackingPlan, item: PackingPlanItem): number =>
  Number(item.required_qty ?? 0)

export const remainingTaskQty = (plan: PackingPlan, item: PackingPlanItem): number =>
  Math.max(0, itemTaskLimit(plan, item) - allocatedTaskQty(plan, item.id))

export const releasedRequiredQty = (plan: PackingPlan, item: PackingPlanItem): number =>
  Math.max(0, Number(item.required_qty) - allocatedTaskQty(plan, item.id))

export const isMeasuredBox = (box: PackingPlanBox): boolean =>
  Number(box.weight) > 0 && Number(box.length) > 0 && Number(box.width) > 0 && Number(box.height) > 0

export const canCompletePackingPlan = (plan: PackingPlan): boolean =>
  canConfirmPackingPlan(plan)

export const canConfirmPackingPlan = (plan: PackingPlan): boolean =>
  plan.packing_plan_status === 'DRAFT'
  && plan.boxes.length > 0
  && inspectActualPackingLines(plan).issues.length === 0

export interface ActualPackingInspection {
  issues: string[]
  warnings: string[]
}

export const inspectActualPackingLines = (plan: PackingPlan): ActualPackingInspection => {
  const issues: string[] = []
  const warnings: string[] = []
  if (plan.boxes.length === 0) issues.push('至少建立一个箱子')
  plan.boxes.forEach((box, index) => {
    if (!isMeasuredBox(box)) issues.push(`第${index + 1}箱重量和箱规不完整`)
    if (box.items.length === 0) issues.push(`第${index + 1}箱至少填写一条实际商品`)
    if (box.items.some((item) => !Number.isInteger(Number(item.actual_qty)) || Number(item.actual_qty) <= 0)) {
      issues.push(`第${index + 1}箱实际商品数量必须为正整数`)
    }
    if (box.items.some((item) => !item.client_line_key?.trim() || Number(item.stock_allocation_id) <= 0)) {
      issues.push(`第${index + 1}箱必须选择实际库存`)
    }
    const keys = box.items.map((item) => item.client_line_key)
    if (new Set(keys).size !== keys.length) issues.push(`第${index + 1}箱实际商品行键重复`)
    box.items.forEach((item) => {
      if (Number(item.available_qty) < Number(item.actual_qty)) {
        warnings.push(`${item.sku_code || '未选择SKU'} 当前可用${Number(item.available_qty)}，本箱填写${Number(item.actual_qty)}，确认后可能形成负库存`)
      }
    })
  })
  return { issues, warnings }
}

export const newDraftBox = (sequence: number, item?: PackingPlanItem, qty = 1): PackingPlanBox => ({
  id: null,
  client_key: globalThis.crypto?.randomUUID?.() ?? `draft-${Date.now()}-${Math.random().toString(16).slice(2)}`,
  box_sequence: sequence,
  weight: null,
  length: null,
  width: null,
  height: null,
  row_version: 0,
  items: []
})

export const copyDraftBox = (source: PackingPlanBox, sequence: number): PackingPlanBox => ({
  id: null,
  client_key: globalThis.crypto?.randomUUID?.() ?? `draft-${Date.now()}-${Math.random().toString(16).slice(2)}`,
  box_sequence: sequence,
  weight: source.weight,
  length: source.length,
  width: source.width,
  height: source.height,
  row_version: 0,
  items: source.items.map((item) => ({
    ...item,
    client_line_key: globalThis.crypto?.randomUUID?.() ?? `line-${Date.now()}-${Math.random().toString(16).slice(2)}`,
    dispatchpicklist_id: null
  }))
})

export const plannedBoxCountDigits = (value: unknown): string =>
  String(value ?? '').replace(/\D/g, '')

export const normalizePlannedBoxCount = (value: unknown): number => {
  const digits = plannedBoxCountDigits(value)
  if (!digits) return 1
  const count = Number(digits)
  return Number.isSafeInteger(count) && count >= 1 ? count : 1
}

export const expandPackingPlanBoxes = (plan: PackingPlan, plannedCount: number): PackingPlanBox[] => {
  const boxes = [...plan.boxes]
  const targetCount = Math.max(boxes.length, normalizePlannedBoxCount(plannedCount))
  while (boxes.length < targetCount) {
    const box = newDraftBox(boxes.length + 1)
    boxes.push(box)
  }
  return boxes
}
