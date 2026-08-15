<template>
  <div class="container">
    <v-tabs v-model="activeTab" class="delivery-status-tabs" stacked @update:model-value="changeTab">
      <v-tab value="tabFbaShipment" data-status-tab="tabFbaShipment">
        <v-badge class="status-count-badge" color="primary" :content="statusCounts.tabFbaShipment" location="top end">
          <v-icon>mdi-truck-fast-outline</v-icon>
        </v-badge>
        <p class="tabItemTitle">{{ $t(packingTaskEnabled ? 'wms.deliveryManagement.packingTask' : 'wms.deliveryManagement.fbaShipment') }}</p>
      </v-tab>
      <v-tab value="tabGoodsToBePicked" data-status-tab="tabGoodsToBePicked">
        <v-badge class="status-count-badge" color="primary" :content="statusCounts.tabGoodsToBePicked" location="top end">
          <v-icon>mdi-dolly</v-icon>
        </v-badge>
        <p class="tabItemTitle">{{ $t('wms.deliveryManagement.goodsToBePicked') }}</p>
      </v-tab>
      <v-tab value="tabPicked" data-status-tab="tabPicked">
        <v-badge class="status-count-badge" color="primary" :content="statusCounts.tabPicked" location="top end">
          <v-icon>mdi-human-dolly</v-icon>
        </v-badge>
        <p class="tabItemTitle">{{ $t('wms.deliveryManagement.picked') }}</p>
      </v-tab>
      <v-tab value="tabWeighed" data-status-tab="tabWeighed">
        <v-badge class="status-count-badge" color="primary" :content="statusCounts.tabWeighed" location="top end">
          <v-icon>mdi-basket-fill</v-icon>
        </v-badge>
        <p class="tabItemTitle">{{ $t('wms.deliveryManagement.weighed') }}</p>
      </v-tab>
      <v-tab value="tabDelivered" data-status-tab="tabDelivered">
        <v-badge class="status-count-badge" color="primary" :content="statusCounts.tabDelivered" location="top end">
          <v-icon>mdi-send-outline</v-icon>
        </v-badge>
        <p class="tabItemTitle">{{ $t('wms.deliveryManagement.toBeDelivered') }}</p>
      </v-tab>
      <v-tab value="tabCompleted" data-status-tab="tabCompleted">
        <v-icon>mdi-check-circle</v-icon>
        <p class="tabItemTitle">{{ $t('wms.deliveryManagement.deliveryReady') }}</p>
      </v-tab>
    </v-tabs>

    <v-card class="mt-5">
      <v-card-text>
        <v-window v-model="activeTab">
          <v-window-item value="tabFbaShipment">
            <PackingTaskList v-if="packingTaskEnabled" ref="packingTaskRef" />
            <FbaShipmentList v-else ref="fbaShipmentRef" @status-changed="refreshStatusCounts" />
          </v-window-item>
          <v-window-item value="tabGoodsToBePicked">
            <TabGoodsToBePicked ref="goodsToBePickedRef" @status-changed="refreshStatusCounts" />
          </v-window-item>
          <v-window-item value="tabPicked">
            <TabPicked ref="pickedRef" @go-to-weighing="handleGoToWeighing" @go-to-picking="handleGoToPicking" />
          </v-window-item>
          <v-window-item value="tabWeighed">
            <TabWeighed ref="weighedRef" @go-to-delivery="handleGoToDelivery" @status-changed="refreshStatusCounts" />
          </v-window-item>
          <v-window-item value="tabDelivered">
            <TabDelivered ref="deliveredRef" @go-to-weighing="handleGoToWeighing"
              @go-to-completed="handleGoToCompleted" @status-changed="refreshStatusCounts" />
          </v-window-item>
          <v-window-item value="tabCompleted">
            <TabCompleted ref="completedRef" @status-changed="refreshStatusCounts" />
          </v-window-item>
        </v-window>
      </v-card-text>
    </v-card>
  </div>
</template>

<script lang="ts" setup>
import { nextTick, onMounted, reactive, ref } from 'vue'
import { getGoodsToBePicked, getPicked, getToBeDelivery, getWeighed } from '@/api/wms/deliveryManagement'
import { getFbaShipmentPage } from '@/api/wms/fbaShipment'
import { getPackingTaskPage } from '@/api/wms/packingTask'
import { loadPackingTaskFirstStep, PACKING_TASK_FIRST_STEP_ENABLED } from '@/config/packingTaskFeature'
import FbaShipmentList from './fba-shipment-list.vue'
import PackingTaskList from './packing-task-list.vue'
import TabDelivered from './tabDelivered.vue'
import TabGoodsToBePicked from './tabGoodsToBePicked.vue'
import TabPicked from './tabPicked.vue'
import TabCompleted from './tabCompleted.vue'
import TabWeighed from './tabWeighed.vue'
import type { DeliveryFlowTab } from './deliveryFlow'
import { loadDeliveryStatusCounts, type DeliveryStatusCounts } from './deliveryStatusCounts'

const activeTab = ref('tabFbaShipment')
const packingTaskEnabled = PACKING_TASK_FIRST_STEP_ENABLED
const packingTaskRef = ref<InstanceType<typeof PackingTaskList>>()
const fbaShipmentRef = ref<InstanceType<typeof FbaShipmentList>>()
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
  tabDelivered: 0
})
let statusCountRequestId = 0

const emptyCountPage = () => ({ pageIndex: 1, pageSize: 1, searchObjects: [] })
const readTotal = async (request: Promise<any>): Promise<number> => {
  const { data: res } = await request
  if (!res.isSuccess) throw new Error(res.errorMessage || 'status count request failed')
  return Number(res.data?.totals) || 0
}

const refreshStatusCounts = async (): Promise<void> => {
  const requestId = ++statusCountRequestId
  const counts = await loadDeliveryStatusCounts({
    tabFbaShipment: () => readTotal(loadPackingTaskFirstStep(
      packingTaskEnabled,
      () => getPackingTaskPage(emptyCountPage()),
      () => getFbaShipmentPage(emptyCountPage())
    )),
    tabGoodsToBePicked: () => readTotal(getGoodsToBePicked(emptyCountPage())),
    tabPicked: () => readTotal(getPicked(emptyCountPage())),
    tabWeighed: () => readTotal(getWeighed(emptyCountPage())),
    tabDelivered: () => readTotal(getToBeDelivery(emptyCountPage()))
  })
  if (requestId === statusCountRequestId) Object.assign(statusCounts, counts)
}

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
  nextTick(() => {
    switch (tab) {
      case 'tabFbaShipment':
        if (packingTaskEnabled) packingTaskRef.value?.getPackingTask()
        else fbaShipmentRef.value?.getFbaShipment()
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

onMounted(refreshStatusCounts)
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
