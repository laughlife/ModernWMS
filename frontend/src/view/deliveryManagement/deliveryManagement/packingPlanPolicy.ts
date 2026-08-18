import type { PackingPlan, PackingPlanBox, PackingPlanItem } from '@/types/DeliveryManagement/DispatchWorkflow'

export const allocatedTaskQty = (plan: PackingPlan, itemId: number): number =>
  plan.boxes.flatMap((box) => box.items).filter((item) => item.packing_task_item_id === itemId)
    .reduce((sum, item) => sum + Number(item.task_qty || 0), 0)

export const itemTaskLimit = (plan: PackingPlan, item: PackingPlanItem): number =>
  plan.packing_plan_status === 'ACTUAL_CONFIRMED'
    ? Number(item.actual_packed_task_qty ?? 0)
    : Number(item.task_qty ?? 0)

export const remainingTaskQty = (plan: PackingPlan, item: PackingPlanItem): number =>
  Math.max(0, itemTaskLimit(plan, item) - allocatedTaskQty(plan, item.id))

export const releasedRequiredQty = (plan: PackingPlan, item: PackingPlanItem): number =>
  Math.max(0, Number(item.task_qty) - allocatedTaskQty(plan, item.id)) * Number(item.variant_qty)

export const isMeasuredBox = (box: PackingPlanBox): boolean =>
  Number(box.weight) > 0 && Number(box.length) > 0 && Number(box.width) > 0 && Number(box.height) > 0

export const canCompletePackingPlan = (plan: PackingPlan): boolean =>
  plan.boxes.length > 0
  && plan.boxes.every((box) => box.items.length > 0 && isMeasuredBox(box))
  && plan.items.every((item) => allocatedTaskQty(plan, item.id) === itemTaskLimit(plan, item))

export const canConfirmPackingPlan = (plan: PackingPlan): boolean =>
  plan.packing_plan_status === 'DRAFT'
  && plan.boxes.length > 0
  && plan.boxes.every((box) => box.items.length > 0
    && isMeasuredBox(box)
    && box.items.every((item) => Number.isInteger(Number(item.task_qty)) && Number(item.task_qty) > 0))
  && plan.boxes.some((box) => box.items.some((item) => Number(item.task_qty) > 0))
  && plan.items.every((item) => allocatedTaskQty(plan, item.id) <= Number(item.task_qty))

export const newDraftBox = (sequence: number, item?: PackingPlanItem, qty = 1): PackingPlanBox => ({
  id: null,
  client_key: globalThis.crypto?.randomUUID?.() ?? `draft-${Date.now()}-${Math.random().toString(16).slice(2)}`,
  box_sequence: sequence,
  weight: null,
  length: null,
  width: null,
  height: null,
  row_version: 0,
  items: item ? [{ packing_task_item_id: item.id, task_qty: qty }] : []
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
    packing_task_item_id: item.packing_task_item_id,
    task_qty: item.task_qty
  }))
})
