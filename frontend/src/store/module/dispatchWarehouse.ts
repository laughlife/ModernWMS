import { defineStore } from 'pinia'
import { getDispatchWarehouseAccess } from '@/api/wms/dispatchWorkflow'
import type { WarehouseOption } from '@/types/DeliveryManagement/DispatchWorkflow'
import {
  loadWarehouseAccessSafely,
  resolveDefaultWarehouseId
} from '@/view/deliveryManagement/deliveryManagement/dispatchWorkflowPolicy'

interface DispatchWarehouseState {
  warehouseOptions: WarehouseOption[]
  selectedWarehouseId: number | null
  loading: boolean
  loadError: boolean
  initialized: boolean
  /** 内部请求版本号：logout 重置后，过期响应不再回填状态。 */
  loadVersion: number
}

const initialState = (): DispatchWarehouseState => ({
  warehouseOptions: [],
  selectedWarehouseId: null,
  loading: false,
  loadError: false,
  initialized: false,
  loadVersion: 0
})

/**
 * 全局发货仓库选择状态。
 * 顶部导航栏（homeHeader）与发货管理页共享同一份仓库选项和选中项，
 * 避免每个页面各自加载一份仓库权限数据。
 */
export const useDispatchWarehouseStore = defineStore('dispatchWarehouse', {
  state: initialState,
  actions: {
    /**
     * 加载当前用户可用的仓库选项（后端只返回国内仓），并回填默认仓库。
     * 已成功加载过则不重复请求；加载失败后允许重试。
     */
    async loadWarehouseAccess(): Promise<void> {
      if (this.loading) return
      if (this.initialized && !this.loadError) return
      this.loading = true
      this.loadError = false
      this.selectedWarehouseId = null
      this.warehouseOptions = []
      const requestVersion = this.loadVersion
      try {
        const result = await loadWarehouseAccessSafely(getDispatchWarehouseAccess)
        if (requestVersion !== this.loadVersion) return
        this.loadError = result.hasError
        if (!result.access) return
        this.warehouseOptions = result.access.warehouses
        this.selectedWarehouseId = resolveDefaultWarehouseId(result.access)
      } catch {
        if (requestVersion === this.loadVersion) this.loadError = true
      } finally {
        if (requestVersion === this.loadVersion) {
          this.loading = false
          this.initialized = true
        }
      }
    },
    selectWarehouse(warehouseId: number | null): void {
      this.selectedWarehouseId = warehouseId
    },
    reset(): void {
      const nextVersion = this.loadVersion + 1
      this.$patch(initialState())
      this.loadVersion = nextVersion
    }
  }
})
