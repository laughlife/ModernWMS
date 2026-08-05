import type { PiniaPluginContext, StateTree } from 'pinia'

const storagePrefix = 'modernwms:'

const persistentKeys: Record<string, string[]> = {
  user: [
    'userInfo',
    'token',
    'refreshToken',
    'expirationTime',
    'effectiveMinutes',
    'menulist'
  ],
  system: ['language', 'openedMenus', 'currentRouterPath']
}

export function loadPersistedState<T extends StateTree>(storeId: string): Partial<T> | undefined {
  if (typeof localStorage === 'undefined') return undefined

  const value = localStorage.getItem(`${storagePrefix}${storeId}`)
  const legacyValue = localStorage.getItem('vuex')
  if (!value && !legacyValue) return undefined

  try {
    if (value) return JSON.parse(value) as Partial<T>

    const legacyState = JSON.parse(legacyValue ?? '{}') as Record<string, Partial<T>>
    return legacyState[storeId]
  } catch {
    localStorage.removeItem(`${storagePrefix}${storeId}`)
    return undefined
  }
}

export function persistStorePlugin({ store }: PiniaPluginContext) {
  const keys = persistentKeys[store.$id]
  if (!keys || typeof localStorage === 'undefined') return

  const persistedState = loadPersistedState(store.$id)
  if (persistedState) store.$patch(persistedState)

  store.$subscribe(
    (_mutation, state) => {
      const value = Object.fromEntries(keys.map((key) => [key, state[key]]))
      localStorage.setItem(`${storagePrefix}${store.$id}`, JSON.stringify(value))
    },
    { detached: true, flush: 'sync' }
  )
}
