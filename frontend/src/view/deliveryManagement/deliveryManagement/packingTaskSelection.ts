import type {
  PackingTaskPageRequest
} from '@/types/DeliveryManagement/DispatchWorkflow'
import type { PackingTaskItemVO, PackingTaskVO, SelectableStockVO } from '@/types/DeliveryManagement/PackingTask'

export type PackingTaskSelectionResult =
  | { ok: true; sourceTaskIds: number[] }
  | { ok: false; reason: 'WAREHOUSE_REQUIRED' | 'EMPTY_SELECTION' | 'CROSS_WAREHOUSE' | 'INVALID_TASK' }

export interface PackingTaskPageMutableState {
  tableData: PackingTaskVO[]
  tablePage: { total: number }
  selectedTaskCount: number
}

export const computeLockedQty = (taskNum: number | null | undefined, variant: number): number =>
  Math.max(0, Number(taskNum) || 0) * variant

export const deriveVariant = (
  taskNum: number | null | undefined,
  lockedQty: number | null | undefined,
  selected: boolean
): number => {
  const quantity = Number(taskNum) || 0
  if (!selected || quantity <= 0 || !lockedQty) return 1
  return Math.max(1, Math.round(lockedQty / quantity))
}

export const getPackingStockCapacity = (
  stock: Pick<SelectableStockVO, 'available_qty' | 'selected_qty'>
): number => Math.max(0, stock.available_qty || 0) + Math.max(0, stock.selected_qty || 0)

export const validatePackingStockSelection = (
  stock: Pick<SelectableStockVO, 'available_qty' | 'selected_qty'>,
  taskNum: number | null | undefined,
  variant: number
): { ok: true } | { ok: false; reason: 'INVALID_VARIANT' | 'INSUFFICIENT_AVAILABLE' } => {
  if (!Number.isInteger(variant) || variant <= 0) return { ok: false, reason: 'INVALID_VARIANT' }
  return computeLockedQty(taskNum, variant) <= getPackingStockCapacity(stock)
    ? { ok: true }
    : { ok: false, reason: 'INSUFFICIENT_AVAILABLE' }
}

export const isPackingItemReady = (item: PackingTaskItemVO): boolean => {
  const taskNum = Number(item.task_num) || 0
  return taskNum > 0 && (Number(item.locked_qty) || 0) >= taskNum
}

export const isPackingTaskReady = (task: PackingTaskVO): boolean =>
  task.item_list.length > 0 && task.item_list.every(isPackingItemReady)

export const isPackingSelectionReady = (tasks: PackingTaskVO[]): boolean =>
  tasks.length > 0 && tasks.every(isPackingTaskReady)

export const buildPackingTaskPageRequest = (
  warehouseId: number | null,
  keyword: string,
  pageIndex: number,
  pageSize: number
): PackingTaskPageRequest | null => {
  if (warehouseId === null) return null

  const searchObjects: PackingTaskPageRequest['searchObjects'] = [
    {
      name: 'warehouse_id',
      operator: 1,
      text: String(warehouseId),
      value: String(warehouseId)
    }
  ]
  const normalizedKeyword = keyword.trim()
  if (normalizedKeyword) {
    searchObjects.push({
      name: 'keyword',
      operator: 1,
      text: normalizedKeyword,
      value: normalizedKeyword
    })
  }

  return { pageIndex, pageSize, searchObjects }
}

export const validatePackingTaskSelection = (
  tasks: PackingTaskVO[],
  warehouseId: number | null
): PackingTaskSelectionResult => {
  if (warehouseId === null) return { ok: false, reason: 'WAREHOUSE_REQUIRED' }
  if (tasks.length === 0) return { ok: false, reason: 'EMPTY_SELECTION' }
  if (tasks.some(({ warehouse_id }) => warehouse_id !== warehouseId)) {
    return { ok: false, reason: 'CROSS_WAREHOUSE' }
  }

  const sourceTaskIds = [...new Set(tasks.map(({ sellfox_task_id }) => sellfox_task_id))]
    .sort((left, right) => left - right)
  if (sourceTaskIds.length === 0 || sourceTaskIds.some((id) => !Number.isSafeInteger(id) || id <= 0)) {
    return { ok: false, reason: 'INVALID_TASK' }
  }
  return { ok: true, sourceTaskIds }
}

export const createTaskSetIdempotencyKey = async (
  sourceTaskIds: number[],
  subtleCrypto: Pick<SubtleCrypto, 'digest'> | null = globalThis.crypto?.subtle ?? null
): Promise<string> => {
  // crypto.subtle is unavailable on non-secure HTTP origins. The backend accepts an
  // empty key and deterministically generates the same task-set key server-side.
  if (!subtleCrypto) return ''
  const normalizedIds = [...new Set(sourceTaskIds)].sort((left, right) => left - right)
  const bytes = new TextEncoder().encode(normalizedIds.join(','))
  const digest = await subtleCrypto.digest('SHA-256', bytes)
  return Array.from(new Uint8Array(digest), (byte) => byte.toString(16).padStart(2, '0')).join('')
}

export const resetPackingTaskPageState = (state: PackingTaskPageMutableState): void => {
  state.tableData = []
  state.tablePage.total = 0
  state.selectedTaskCount = 0
}

export const removeCreatedPackingTasks = (
  tasks: PackingTaskVO[],
  sourceTaskIds: number[]
): PackingTaskVO[] => {
  const createdTaskIds = new Set(sourceTaskIds)
  return tasks.filter(({ sellfox_task_id }) => !createdTaskIds.has(sellfox_task_id))
}
