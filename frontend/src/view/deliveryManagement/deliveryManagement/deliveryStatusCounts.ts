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

const safeCount = (value: unknown): number => {
  const count = Number(value)
  return Number.isFinite(count) && count > 0 ? count : 0
}

export const mapWorkflowCountsToTabs = (
  workflow: WorkflowCountSource,
  packingTaskCount: number
): DeliveryStatusCounts => ({
  tabFbaShipment: safeCount(packingTaskCount),
  tabGoodsToBePicked: safeCount(workflow.PENDING_PICK),
  tabPicked: safeCount(workflow.PICKED),
  tabWeighed: safeCount(workflow.WEIGHING),
  tabDelivered: safeCount(workflow.PENDING_OUTBOUND),
  tabCompleted: safeCount(workflow.OUTBOUND)
})

export const loadDeliveryStatusCounts = async ({
  loadWorkflowCounts,
  loadPackingTaskCount
}: {
  loadWorkflowCounts: () => Promise<WorkflowCountSource>
  loadPackingTaskCount: () => Promise<number>
}): Promise<DeliveryStatusCounts> => {
  const [workflow, packingTaskCount] = await Promise.all([
    loadWorkflowCounts(),
    loadPackingTaskCount()
  ])
  return mapWorkflowCountsToTabs(workflow, packingTaskCount)
}
