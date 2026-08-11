export type DeliveryFlowTab = 'tabDelivered'
export type DeliveryFlowAction = 'stay' | 'confirm-weighing' | 'open-pending-outbound'

export const getNextDeliveryTab = (isTodo: boolean): DeliveryFlowTab | null =>
  isTodo ? null : 'tabDelivered'

export const getDeliveryFlowAction = ({
  dispatchStatus,
  isTodo
}: {
  dispatchStatus: number
  isTodo: boolean
}): DeliveryFlowAction => {
  if (isTodo) return 'stay'
  if (dispatchStatus === 4) return 'confirm-weighing'
  if (dispatchStatus === 5) return 'open-pending-outbound'
  return 'stay'
}
