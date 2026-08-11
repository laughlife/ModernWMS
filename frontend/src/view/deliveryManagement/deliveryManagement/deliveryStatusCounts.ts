export const COUNTED_DELIVERY_TABS = [
  'tabFbaShipment',
  'tabGoodsToBePicked',
  'tabPicked',
  'tabWeighed',
  'tabDelivered'
] as const

export type CountedDeliveryTab = typeof COUNTED_DELIVERY_TABS[number]
export type DeliveryStatusCounts = Record<CountedDeliveryTab, number>
export type DeliveryStatusCountLoaders = Record<CountedDeliveryTab, () => Promise<number>>

export const loadDeliveryStatusCounts = async (loaders: DeliveryStatusCountLoaders): Promise<Partial<DeliveryStatusCounts>> => {
  const entries = await Promise.all(COUNTED_DELIVERY_TABS.map(async tab => {
    try {
      const total = Number(await loaders[tab]())
      return [tab, Number.isFinite(total) && total > 0 ? total : 0] as const
    } catch {
      return null
    }
  }))
  return Object.fromEntries(entries.filter(entry => entry !== null)) as Partial<DeliveryStatusCounts>
}
