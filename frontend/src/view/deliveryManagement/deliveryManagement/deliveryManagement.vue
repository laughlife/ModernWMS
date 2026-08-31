<template>
  <div class="container">
    <v-alert v-if="dispatchWarehouseStore.loadError" type="error" variant="tonal" class="mb-3">
      {{ $t('wms.deliveryManagement.warehouseLoadFailed') }}
      <template #append>
        <v-btn variant="text" :loading="dispatchWarehouseStore.loading" @click="dispatchWarehouseStore.loadWarehouseAccess">
          {{ $t('wms.deliveryManagement.retry') }}
        </v-btn>
      </template>
    </v-alert>
    <v-tabs v-model="activeTab" class="delivery-status-tabs" stacked @update:model-value="changeTab">
      <v-tab value="tabFbaShipment" data-status-tab="tabFbaShipment">
        <v-badge class="status-count-badge" color="primary" text-color="on-primary" :content="String(statusCounts.tabFbaShipment)" location="top end">
          <v-icon>mdi-truck-fast-outline</v-icon>
        </v-badge>
        <p class="tabItemTitle">{{ $t('wms.deliveryManagement.packingTask') }}</p>
      </v-tab>
      <v-tab value="tabGoodsToBePicked" data-status-tab="tabGoodsToBePicked">
        <v-badge class="status-count-badge" color="primary" text-color="on-primary" :content="String(statusCounts.tabGoodsToBePicked)" location="top end">
          <v-icon>mdi-dolly</v-icon>
        </v-badge>
        <p class="tabItemTitle">{{ $t('wms.deliveryManagement.goodsToBePicked') }}</p>
      </v-tab>
      <v-tab value="tabPicked" data-status-tab="tabPicked">
        <v-badge class="status-count-badge" color="primary" text-color="on-primary" :content="String(statusCounts.tabPicked)" location="top end">
          <v-icon>mdi-human-dolly</v-icon>
        </v-badge>
        <p class="tabItemTitle">{{ $t('wms.deliveryManagement.picked') }}</p>
      </v-tab>
      <v-tab value="tabWeighed" data-status-tab="tabWeighed">
        <v-badge class="status-count-badge" color="primary" text-color="on-primary" :content="String(statusCounts.tabWeighed)" location="top end">
          <v-icon>mdi-basket-fill</v-icon>
        </v-badge>
        <p class="tabItemTitle">{{ $t('wms.deliveryManagement.weighed') }}</p>
      </v-tab>
      <v-tab value="tabDelivered" data-status-tab="tabDelivered">
        <v-badge class="status-count-badge" color="primary" text-color="on-primary" :content="String(statusCounts.tabDelivered)" location="top end">
          <v-icon>mdi-send-outline</v-icon>
        </v-badge>
        <p class="tabItemTitle">{{ $t('wms.deliveryManagement.toBeDelivered') }}</p>
      </v-tab>
      <v-tab value="tabCompleted" data-status-tab="tabCompleted">
        <v-badge class="status-count-badge" color="primary" text-color="on-primary" :content="String(statusCounts.tabCompleted)" location="top end">
          <v-icon>mdi-check-circle</v-icon>
        </v-badge>
        <p class="tabItemTitle">{{ $t('wms.deliveryManagement.deliveryReady') }}</p>
      </v-tab>
    </v-tabs>

    <v-card class="mt-5">
      <v-card-text>
        <v-window v-model="activeTab">
          <!-- Shared page contract: every Task10-15 page receives the same backend-authorized warehouse id. -->
          <v-window-item value="tabFbaShipment">
            <PackingTaskList
              ref="packingTaskRef"
              :warehouse-id="selectedWarehouseId"
              @orders-created="handleOrdersCreated"
              @status-changed="refreshStatusCounts"
            />
          </v-window-item>
          <v-window-item value="tabGoodsToBePicked">
            <TabGoodsToBePicked ref="goodsToBePickedRef" :warehouse-id="selectedWarehouseId" @status-changed="refreshStatusCounts" />
          </v-window-item>
          <v-window-item value="tabPicked">
            <TabPicked ref="pickedRef" :warehouse-id="selectedWarehouseId" @go-to-weighing="handleGoToWeighing" @go-to-picking="handleGoToPicking" @status-changed="refreshStatusCounts" />
          </v-window-item>
          <v-window-item value="tabWeighed">
            <TabWeighed ref="weighedRef" :warehouse-id="selectedWarehouseId" @go-to-picked="handleGoToPicked" @go-to-delivery="handleGoToDelivery" @status-changed="refreshStatusCounts" />
          </v-window-item>
          <v-window-item value="tabDelivered">
            <TabDelivered ref="deliveredRef" :warehouse-id="selectedWarehouseId" @go-to-weighing="handleGoToWeighing"
              @go-to-completed="handleGoToCompleted" @status-changed="refreshStatusCounts" />
          </v-window-item>
          <v-window-item value="tabCompleted">
            <TabCompleted ref="completedRef" :warehouse-id="selectedWarehouseId" @status-changed="refreshStatusCounts" />
          </v-window-item>
        </v-window>
      </v-card-text>
    </v-card>
  </div>
</template>

<script lang="ts" setup>
import { computed, nextTick, onMounted, reactive, ref, watch } from 'vue'
import {
  getDispatchStatusCounts,
  getWorkflowPackingTaskPage
} from '@/api/wms/dispatchWorkflow'
import { useDispatchWarehouseStore } from '@/store/module/dispatchWarehouse'
import PackingTaskList from './packing-task-list.vue'
import TabDelivered from './tabDelivered.vue'
import TabGoodsToBePicked from './tabGoodsToBePicked.vue'
import TabPicked from './tabPicked.vue'
import TabCompleted from './tabCompleted.vue'
import TabWeighed from './tabWeighed.vue'
import type { DeliveryFlowTab } from './deliveryFlow'
import { loadDeliveryStatusCounts, type DeliveryStatusCounts } from './deliveryStatusCounts'

const activeTab = ref('tabFbaShipment')
const packingTaskRef = ref<InstanceType<typeof PackingTaskList>>()
const goodsToBePickedRef = ref<InstanceType<typeof TabGoodsToBePicked>>()
const pickedRef = ref<InstanceType<typeof TabPicked>>()
const weighedRef = ref<InstanceType<typeof TabWeighed>>()
const deliveredRef = ref<InstanceType<typeof TabDelivered>>()
const completedRef = ref<InstanceType<typeof TabCompleted>>()
const statusCounts = reactive<DeliveryStatusCounts>({
  tabFbaShipment: 0,
  tabGoodsToBePicked: 0,
  tabPicked: 0,
  tabWeighed: 0,
  tabDelivered: 0,
  tabCompleted: 0
})
// 仓库选择已上移到顶部导航栏（homeHeader），页面只读取共享的选中仓库。
const dispatchWarehouseStore = useDispatchWarehouseStore()
const selectedWarehouseId = computed(() => dispatchWarehouseStore.selectedWarehouseId)
let statusCountRequestId = 0

const refreshStatusCounts = async (): Promise<void> => {
  if (selectedWarehouseId.value === null) return
  const requestId = ++statusCountRequestId
  try {
    const warehouseId = selectedWarehouseId.value
    const counts = await loadDeliveryStatusCounts({
      loadWorkflowCounts: async () => {
        const result = await getDispatchStatusCounts(warehouseId)
        if (!result.isSuccess) throw new Error(result.errorMessage)
        return result.data
      },
      loadPackingTaskCount: async () => {
        const result = await getWorkflowPackingTaskPage({
          pageIndex: 1,
          pageSize: 1,
          searchObjects: [{ name: 'warehouse_id', operator: 1, text: String(warehouseId), value: String(warehouseId) }]
        })
        if (!result.isSuccess) throw new Error(result.errorMessage)
        return result.data.totals
      },
      fallbackCounts: { ...statusCounts }
    })
    if (requestId === statusCountRequestId) Object.assign(statusCounts, counts)
  } catch {
    // Keep the last successful counters when one source is temporarily unavailable.
  }
}

const handleOrdersCreated = (count: number): void => {
  const createdCount = Number.isFinite(count) ? Math.max(0, Math.trunc(count)) : 0
  if (createdCount === 0) return
  // 使建单前发出的旧计数请求失效，避免其返回后覆盖逐单更新的角标。
  statusCountRequestId++
  statusCounts.tabFbaShipment = Math.max(0, statusCounts.tabFbaShipment - createdCount)
  statusCounts.tabGoodsToBePicked += createdCount
}

onMounted(() => {
  // 兜底触发仓库权限加载（导航栏已在进入页面时加载，此处由 store 去重）。
  void dispatchWarehouseStore.loadWarehouseAccess()
})

const handleGoToPicking = (): void => {
  activeTab.value = 'tabGoodsToBePicked'
  nextTick(() => {
    goodsToBePickedRef.value?.getGoodsToBePicked()
  })
  refreshStatusCounts()
}

const handleGoToWeighing = (): void => {
  activeTab.value = 'tabWeighed'
  nextTick(() => {
    weighedRef.value?.getWeighed()
  })
  refreshStatusCounts()
}

const handleGoToPicked = (): void => {
  activeTab.value = 'tabPicked'
  nextTick(() => {
    pickedRef.value?.getPicked()
  })
  refreshStatusCounts()
}

const handleGoToDelivery = (targetTab: DeliveryFlowTab): void => {
  activeTab.value = targetTab
  nextTick(() => {
    deliveredRef.value?.getDelivery()
  })
  refreshStatusCounts()
}

const handleGoToCompleted = (): void => {
  activeTab.value = 'tabCompleted'
  nextTick(() => {
    completedRef.value?.getCompleted()
  })
  refreshStatusCounts()
}

const changeTab = (tab: unknown): void => {
  refreshStatusCounts()
  nextTick(() => {
    switch (tab) {
      case 'tabFbaShipment':
        packingTaskRef.value?.getPackingTask()
        break
      case 'tabGoodsToBePicked':
        goodsToBePickedRef.value?.getGoodsToBePicked()
        break
      case 'tabPicked':
        pickedRef.value?.getPicked()
        break
      case 'tabWeighed':
        weighedRef.value?.getWeighed()
        break
      case 'tabDelivered':
        deliveredRef.value?.getDelivery()
        break
      case 'tabCompleted':
        completedRef.value?.getCompleted()
        break
    }
  })
}

// 顶部导航栏切换仓库后，重新加载当前页签数据与状态数量。
watch(
  () => dispatchWarehouseStore.selectedWarehouseId,
  (warehouseId) => {
    if (warehouseId === null) return
    changeTab(activeTab.value)
  },
  { immediate: true }
)
</script>

<style lang="less" scoped>
.delivery-status-tabs {
  margin-top: 12px;
}

.delivery-status-tabs :deep(.v-btn__content) {
  padding-top: 15px;
}

.status-count-badge :deep(.v-badge__badge) {
  transform: translateX(10px);
}
</style>
