import {
  completeDispatchOrderWeighing,
  completeDispatchTaskWeighing,
  confirmDispatchActualPacking,
  saveDispatchPackingPlan
} from '@/api/wms/dispatchWorkflow'
import type { PackingPlan, PackingPlanBox } from '@/types/DeliveryManagement/DispatchWorkflow'
import { inspectActualPackingLines, remainingTaskQty } from './packingPlanPolicy'

export interface UnfinishedPackingProduct {
  name: string
  remainingTaskQty: number
  remainingRequiredQty: number
}

export interface PackingPlanInspection {
  issues: string[]
  warnings: string[]
  unfinishedProducts: UnfinishedPackingProduct[]
}

const requestId = (prefix: string): string =>
  globalThis.crypto?.randomUUID?.() ?? `${prefix}-${Date.now()}-${Math.random().toString(16).slice(2)}`

export const inspectPackingPlan = (plan: PackingPlan): PackingPlanInspection => {
  const { issues, warnings } = inspectActualPackingLines(plan)
  const unfinishedProducts = plan.items
    .map((item) => ({ item, remaining: remainingTaskQty(plan, item) }))
    .filter(({ remaining }) => remaining > 0)
    .map(({ item, remaining }) => ({
      name: item.commodity_name || item.commodity_sku || '未命名商品',
      remainingTaskQty: remaining,
      remainingRequiredQty: remaining * Number(item.variant_qty)
    }))
  return { issues, warnings, unfinishedProducts }
}

const boxesForSave = (boxes: PackingPlanBox[]): PackingPlanBox[] => boxes.map((box) => ({
  ...box,
  items: box.items.filter((item) => Number(item.actual_qty) > 0 && Number(item.erp_stock_id) > 0)
}))

export const advancePackingPlan = async (orderId: number, packingTaskId: number, plan: PackingPlan): Promise<void> => {
  const saved = await saveDispatchPackingPlan(orderId, packingTaskId, {
    request_id: requestId('save-packing'),
    row_version: plan.row_version,
    task_row_version: plan.task_row_version,
    boxes: boxesForSave(plan.boxes)
  })
  if (!saved.isSuccess) throw new Error(saved.errorMessage)
  const confirmed = await confirmDispatchActualPacking(orderId, packingTaskId, {
    request_id: requestId('confirm-actual-packing'),
    row_version: saved.data.row_version,
    task_row_version: saved.data.task_row_version
  })
  if (!confirmed.isSuccess) throw new Error(confirmed.errorMessage)
  const taskCompleted = await completeDispatchTaskWeighing(orderId, packingTaskId, {
    request_id: requestId('complete-task-weighing'),
    row_version: confirmed.data.row_version
  })
  if (!taskCompleted.isSuccess) throw new Error(taskCompleted.errorMessage)
  const orderCompleted = await completeDispatchOrderWeighing(orderId, {
    request_id: requestId('complete-order-weighing'),
    row_version: taskCompleted.data.row_version
  })
  if (!orderCompleted.isSuccess) throw new Error(orderCompleted.errorMessage)
}
