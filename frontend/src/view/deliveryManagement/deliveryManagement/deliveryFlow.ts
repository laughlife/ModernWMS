export type DeliveryFlowTab = 'tabDelivered'

export const getNextDeliveryTab = (isTodo: boolean): DeliveryFlowTab | null =>
  isTodo ? null : 'tabDelivered'
