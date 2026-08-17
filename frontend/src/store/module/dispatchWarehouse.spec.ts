import { beforeEach, describe, expect, it, vi } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import { useDispatchWarehouseStore } from './dispatchWarehouse'

const { accessMock } = vi.hoisted(() => ({ accessMock: vi.fn() }))

vi.mock('@/api/wms/dispatchWorkflow', () => ({
  getDispatchWarehouseAccess: accessMock
}))

const domesticAccess = {
  warehouses: [
    { id: 320118, name: '深圳自建仓' },
    { id: 9, name: '备用仓' }
  ],
  default_warehouse_id: 320118
}

describe('dispatch warehouse store', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    accessMock.mockReset()
  })

  it('loads warehouse options and the backend default once', async () => {
    accessMock.mockResolvedValue({ isSuccess: true, code: 0, errorMessage: '', data: domesticAccess })
    const store = useDispatchWarehouseStore()

    await store.loadWarehouseAccess()

    expect(store.warehouseOptions.map((t) => t.id)).toEqual([320118, 9])
    expect(store.selectedWarehouseId).toBe(320118)
    expect(store.loadError).toBe(false)
    expect(store.initialized).toBe(true)
  })

  it('skips reloading once initialized without error and keeps the selection', async () => {
    accessMock.mockResolvedValue({ isSuccess: true, code: 0, errorMessage: '', data: domesticAccess })
    const store = useDispatchWarehouseStore()
    await store.loadWarehouseAccess()
    store.selectWarehouse(9)

    await store.loadWarehouseAccess()

    expect(accessMock).toHaveBeenCalledTimes(1)
    expect(store.selectedWarehouseId).toBe(9)
  })

  it('marks a failed load and allows the same action to retry', async () => {
    accessMock
      .mockRejectedValueOnce(new Error('network'))
      .mockResolvedValueOnce({ isSuccess: true, code: 0, errorMessage: '', data: domesticAccess })
    const store = useDispatchWarehouseStore()

    await store.loadWarehouseAccess()
    expect(store.loadError).toBe(true)
    expect(store.warehouseOptions).toEqual([])
    expect(store.selectedWarehouseId).toBeNull()

    await store.loadWarehouseAccess()
    expect(store.loadError).toBe(false)
    expect(store.selectedWarehouseId).toBe(320118)
    expect(accessMock).toHaveBeenCalledTimes(2)
  })

  it('updates the selection and resets the whole state', () => {
    const store = useDispatchWarehouseStore()
    store.selectWarehouse(320118)
    expect(store.selectedWarehouseId).toBe(320118)

    store.reset()
    expect(store.selectedWarehouseId).toBeNull()
    expect(store.warehouseOptions).toEqual([])
    expect(store.loadError).toBe(false)
    expect(store.initialized).toBe(false)
  })

  it('ignores a stale response that resolves after logout reset', async () => {
    let resolveAccess: (value: unknown) => void = () => undefined
    accessMock.mockImplementation(() => new Promise((resolve) => { resolveAccess = resolve }))
    const store = useDispatchWarehouseStore()

    const loading = store.loadWarehouseAccess()
    store.reset()
    resolveAccess({ isSuccess: true, code: 0, errorMessage: '', data: domesticAccess })
    await loading

    expect(store.warehouseOptions).toEqual([])
    expect(store.selectedWarehouseId).toBeNull()
    expect(store.initialized).toBe(false)
  })
})
