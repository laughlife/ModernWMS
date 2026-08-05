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

function selectPersistentState<T extends StateTree>(storeId: string, state: Partial<T>): Partial<T> {
  const keys = persistentKeys[storeId] ?? []
  return Object.fromEntries(
    keys
      .filter((key) => Object.prototype.hasOwnProperty.call(state, key))
      .map((key) => [key, state[key]])
  ) as Partial<T>
}

function migrateLegacyState() {
  const legacyValue = localStorage.getItem('vuex')
  if (!legacyValue) return

  try {
    const legacyState = JSON.parse(legacyValue) as Record<string, StateTree>
    for (const storeId of Object.keys(persistentKeys)) {
      const storageKey = `${storagePrefix}${storeId}`
      if (!localStorage.getItem(storageKey) && legacyState[storeId]) {
        localStorage.setItem(storageKey, JSON.stringify(selectPersistentState(storeId, legacyState[storeId])))
      }
    }
  } catch {
    // Ignore malformed legacy data and remove it below.
  } finally {
    localStorage.removeItem('vuex')
  }
}

export function loadPersistedState<T extends StateTree>(storeId: string): Partial<T> | undefined {
  if (typeof localStorage === 'undefined') return undefined

  migrateLegacyState()
  const value = localStorage.getItem(`${storagePrefix}${storeId}`)
  if (!value) return undefined

  try {
    return selectPersistentState(storeId, JSON.parse(value) as Partial<T>)
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
      const value = selectPersistentState(store.$id, state)
      localStorage.setItem(`${storagePrefix}${store.$id}`, JSON.stringify(value))
    },
    { detached: true, flush: 'sync' }
  )
}
