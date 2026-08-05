import { createApp } from 'vue'
import { createPinia, setActivePinia } from 'pinia'
import { beforeEach, describe, expect, it } from 'vitest'
import { useSystemStore } from './module/system'
import { useUserStore } from './module/user'
import { persistStorePlugin } from './persistence'

describe('Pinia store behavior', () => {
  beforeEach(() => {
    localStorage.clear()
    const pinia = createPinia()
    pinia.use(persistStorePlugin)
    createApp({}).use(pinia)
    setActivePinia(pinia)
  })

  it('persists the login token', () => {
    const userStore = useUserStore()
    userStore.setToken('access-token')

    const persisted = JSON.parse(localStorage.getItem('modernwms:user') ?? '{}')
    expect(persisted.token).toBe('access-token')
  })

  it('persists the selected language', () => {
    const systemStore = useSystemStore()
    systemStore.setLanguage('zh')

    const persisted = JSON.parse(localStorage.getItem('modernwms:system') ?? '{}')
    expect(persisted.language).toBe('zh')
  })

  it('keeps opened menu names unique', () => {
    const systemStore = useSystemStore()
    systemStore.addOpenedMenu('stockManagement')
    systemStore.addOpenedMenu('stockManagement')

    expect(systemStore.openedMenus).toEqual(['stockManagement'])
  })

  it('clears session and navigation state on logout', () => {
    const userStore = useUserStore()
    const systemStore = useSystemStore()
    userStore.setToken('access-token')
    systemStore.addOpenedMenu('stockManagement')
    systemStore.setCurrentRouterPath('stockManagement')

    userStore.clearSession()
    systemStore.clearOpenedMenu()
    systemStore.setCurrentRouterPath('')

    expect(userStore.token).toBe('')
    expect(systemStore.openedMenus).toEqual([])
    expect(systemStore.currentRouterPath).toBe('')
  })

  it('migrates only approved legacy fields and removes the old vuex payload', () => {
    localStorage.setItem('vuex', JSON.stringify({
      user: {
        token: 'legacy-token',
        refreshToken: 'legacy-refresh-token',
        isRefreshingToken: true
      },
      system: {
        language: 'zh',
        refreshFlag: true
      }
    }))

    const userStore = useUserStore()
    const systemStore = useSystemStore()

    expect(userStore.token).toBe('legacy-token')
    expect(userStore.isRefreshingToken).toBe(false)
    expect(systemStore.language).toBe('zh')
    expect(systemStore.refreshFlag).toBe(false)
    expect(localStorage.getItem('vuex')).toBeNull()
  })
})
