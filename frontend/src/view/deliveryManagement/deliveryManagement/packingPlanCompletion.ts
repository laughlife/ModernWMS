import {
  completeDispatchOrderWeighing,
  completeDispatchTaskWeighing,
  confirmDispatchActualPacking,
  saveDispatchPackingPlan
} from '@/api/wms/dispatchWorkflow'
import type { PackingPlan, PackingPlanBox } from '@/types/DeliveryManagement/DispatchWorkflow'
import { allocatedTaskQty, itemTaskLimit, remainingTaskQty } from './packingPlanPolicy'

export interface UnfinishedPackingProduct {
  name: string
  remainingTaskQty: number
  remainingRequiredQty: number
}

export interface PackingPlanInspection {
  issues: string[]
  unfinishedProducts: UnfinishedPackingProduct[]
}

const requestId = (prefix: string): string =>
  globalThis.crypto?.randomUUID?.() ?? `${prefix}-${Date.now()}-${Math.random().toString(16).slice(2)}`

export const inspectPackingPlan = (plan: PackingPlan): PackingPlanInspection => {
  const issues: string[] = []
  if (plan.boxes.length === 0) issues.push('至少建立一个箱子')
  plan.boxes.forEach((box, index) => {
    const missing: string[] = []
    if (Number(box.weight) <= 0) missing.push('重量')
    if (Number(box.length) <= 0) missing.push('长')
    if (Number(box.width) <= 0) missing.push('宽')
    if (Number(box.height) <= 0) missing.push('高')
    if (!box.items.some((item) => Number(item.task_qty) > 0)) missing.push('商品任务量')
    if (box.items.some((item) => !Number.isInteger(Number(item.task_qty)) || Number(item.task_qty) < 0)) missing.push('有效整数任务量')
    if (missing.length > 0) issues.push(`第${index + 1}箱缺少${missing.join('、')}`)
  })
  plan.items.forEach((item) => {
    const totalTaskQty = itemTaskLimit(plan, item)
    const actualTaskQty = allocatedTaskQty(plan, item.id)
    if (actualTaskQty > totalTaskQty) {
      issues.push(`${item.commodity_name || item.commodity_sku || '未命名商品'}（sku：${item.commodity_sku || '-'}）总任务量${totalTaskQty}，实际任务量${actualTaskQty}`)
    }
  })
  const unfinishedProducts = plan.items
    .map((item) => ({ item, remaining: remainingTaskQty(plan, item) }))
    .filter(({ remaining }) => remaining > 0)
    .map(({ item, remaining }) => ({
      name: item.commodity_name || item.commodity_sku || '未命名商品',
      remainingTaskQty: remaining,
      remainingRequiredQty: remaining * Number(item.variant_qty)
    }))
  return { issues, unfinishedProducts }
}

const boxesForSave = (boxes: PackingPlanBox[]): PackingPlanBox[] => boxes.map((box) => ({
  ...box,
  items: box.items.filter((item) => Number(item.task_qty) > 0)
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
