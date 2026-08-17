import { defineStore } from 'pinia'
import type { MenuItem, UserStateProps } from '@/types/System/Store'

const initialState = (): UserStateProps => ({
  userInfo: {},
  token: '',
  refreshToken: '',
  expirationTime: 0,
  effectiveMinutes: 0,
  isRefreshingToken: false,
  menulist: []
})

export const useUserStore = defineStore('user', {
  state: initialState,
  actions: {
    setUserInfo(userInfo: unknown) {
      this.userInfo = userInfo
    },
    resetUserInfo(userInfo: Record<string, unknown> = {}) {
      this.userInfo = { ...this.userInfo, ...userInfo }
    },
    setToken(token: string) {
      this.token = token
    },
    setExpirationTime(expirationTime: number) {
      this.expirationTime = expirationTime
    },
    setIsRefreshingToken(isRefreshingToken: boolean) {
      this.isRefreshingToken = isRefreshingToken
    },
    setRefreshToken(refreshToken: string) {
      this.refreshToken = refreshToken
    },
    setEffectiveMinutes(effectiveMinutes: number) {
      this.effectiveMinutes = effectiveMinutes
    },
    setUserMenuList(menulist: MenuItem[]) {
      this.menulist = menulist
    },
    clearSession() {
      this.$patch(initialState())
      if (typeof localStorage !== 'undefined') localStorage.removeItem('vuex')
      // 清除顶部仓库选择状态，避免下一个登录用户看到上一个用户的仓库缓存。
      // 动态导入避免 user -> dispatchWarehouse -> api -> http -> user 的循环依赖。
      void import('./dispatchWarehouse').then(({ useDispatchWarehouseStore }) => {
        useDispatchWarehouseStore().reset()
      })
    }
  }
})
