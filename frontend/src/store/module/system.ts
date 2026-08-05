import { defineStore } from 'pinia'
import type { StateProps } from '@/types/System/Store'

const initialState = (): StateProps => ({
  openedMenus: [],
  clientWidth: 0,
  clientHeight: 0,
  currentRouterPath: ''
})

export const useSystemStore = defineStore('system', {
  state: initialState,
  actions: {
    setCurrentRouterPath(path: string) {
      this.currentRouterPath = path
    },
    addOpenedMenu(menuName: string) {
      if (!this.openedMenus.includes(menuName)) this.openedMenus.push(menuName)
    },
    delOpenedMenu(menuName: string) {
      const menuIndex = this.openedMenus.indexOf(menuName)
      if (menuIndex > -1) this.openedMenus.splice(menuIndex, 1)
    },
    clearOpenedMenu() {
      this.openedMenus = []
    },
    setClientWidth(clientWidth: number) {
      this.clientWidth = clientWidth
    },
    setClientHeight(clientHeight: number) {
      this.clientHeight = clientHeight
    }
  }
})
