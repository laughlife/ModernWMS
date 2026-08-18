export const COUNTED_DELIVERY_TABS = [
  'tabFbaShipment',
  'tabGoodsToBePicked',
  'tabPicked',
  'tabWeighed',
  'tabDelivered',
  'tabCompleted'
] as const

export type CountedDeliveryTab = typeof COUNTED_DELIVERY_TABS[number]
export type DeliveryStatusCounts = Record<CountedDeliveryTab, number>
export interface WorkflowCountSource {
  PENDING_PICK?: number
  PICKED?: number
  WEIGHING?: number
  PENDING_OUTBOUND?: number
  OUTBOUND?: number
  SOURCE_CANCELLED?: number
  MANUAL_CANCELLED?: number
}

type WorkflowCountResponse = WorkflowCountSource | Record<string, number | undefined>

const safeCount = (value: unknown): number => {
  const count = Number(value)
  return Number.isFinite(count) && count > 0 ? count : 0
}

export const mapWorkflowCountsToTabs = (
  workflow: WorkflowCountResponse,
  packingTaskCount: number
): DeliveryStatusCounts => {
  // Newtonsoft 的 CamelCasePropertyNamesContractResolver 会把字典键
  // PENDING_PICK 序列化成 pendinG_PICK；在接口边界统一恢复状态契约。
  const normalizedWorkflow = Object.fromEntries(
    Object.entries(workflow).map(([status, count]) => [status.toUpperCase(), count])
  ) as WorkflowCountSource

  return {
    tabFbaShipment: safeCount(packingTaskCount),
    tabGoodsToBePicked: safeCount(normalizedWorkflow.PENDING_PICK),
    tabPicked: safeCount(normalizedWorkflow.PICKED),
    tabWeighed: safeCount(normalizedWorkflow.WEIGHING),
    tabDelivered: safeCount(normalizedWorkflow.PENDING_OUTBOUND),
    tabCompleted: safeCount(normalizedWorkflow.OUTBOUND)
  }
}

export const loadDeliveryStatusCounts = async ({
  loadWorkflowCounts,
  loadPackingTaskCount,
  fallbackCounts = mapWorkflowCountsToTabs({}, 0)
}: {
  loadWorkflowCounts: () => Promise<WorkflowCountResponse>
  loadPackingTaskCount: () => Promise<number>
  fallbackCounts?: DeliveryStatusCounts
}): Promise<DeliveryStatusCounts> => {
  const [workflowResult, packingTaskResult] = await Promise.allSettled([
    loadWorkflowCounts(),
    loadPackingTaskCount()
  ])
  const result = mapWorkflowCountsToTabs(
    workflowResult.status === 'fulfilled' ? workflowResult.value : {},
    packingTaskResult.status === 'fulfilled' ? packingTaskResult.value : fallbackCounts.tabFbaShipment
  )
  if (workflowResult.status === 'rejected') {
    result.tabGoodsToBePicked = fallbackCounts.tabGoodsToBePicked
    result.tabPicked = fallbackCounts.tabPicked
    result.tabWeighed = fallbackCounts.tabWeighed
    result.tabDelivered = fallbackCounts.tabDelivered
    result.tabCompleted = fallbackCounts.tabCompleted
  }
  return result
}
