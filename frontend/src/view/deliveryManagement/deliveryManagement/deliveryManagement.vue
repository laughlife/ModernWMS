<template>
  <div class="container">
    <v-tabs v-model="activeTab" stacked @update:model-value="changeTab">
      <v-tab value="tabFbaShipment">
        <v-icon>mdi-truck-fast-outline</v-icon>
        <p class="tabItemTitle">{{ $t('wms.deliveryManagement.fbaShipment') }}</p>
      </v-tab>
      <v-tab value="tabGoodsToBePicked">
        <v-icon>mdi-dolly</v-icon>
        <p class="tabItemTitle">{{ $t('wms.deliveryManagement.goodsToBePicked') }}</p>
      </v-tab>
      <v-tab value="tabPicked">
        <v-icon>mdi-human-dolly</v-icon>
        <p class="tabItemTitle">{{ $t('wms.deliveryManagement.picked') }}</p>
      </v-tab>
      <v-tab value="tabWeighed">
        <v-icon>mdi-basket-fill</v-icon>
        <p class="tabItemTitle">{{ $t('wms.deliveryManagement.weighed') }}</p>
      </v-tab>
      <v-tab value="tabDelivered">
        <v-icon>mdi-send-outline</v-icon>
        <p class="tabItemTitle">{{ $t('wms.deliveryManagement.toBeDelivered') }}</p>
      </v-tab>
      <v-tab value="tabCompleted">
        <v-icon>mdi-check-circle</v-icon>
        <p class="tabItemTitle">{{ $t('wms.deliveryManagement.deliveryReady') }}</p>
      </v-tab>
    </v-tabs>

    <v-card class="mt-5">
      <v-card-text>
        <v-window v-model="activeTab">
          <v-window-item value="tabFbaShipment">
            <FbaShipmentList ref="fbaShipmentRef" />
          </v-window-item>
          <v-window-item value="tabGoodsToBePicked">
            <TabGoodsToBePicked ref="goodsToBePickedRef" />
          </v-window-item>
          <v-window-item value="tabPicked">
            <TabPicked ref="pickedRef" @go-to-weighing="handleGoToWeighing" @go-to-picking="handleGoToPicking" />
          </v-window-item>
          <v-window-item value="tabWeighed">
            <TabWeighed ref="weighedRef" @go-to-delivery="handleGoToDelivery" />
          </v-window-item>
          <v-window-item value="tabDelivered">
            <TabDelivered ref="deliveredRef" @go-to-weighing="handleGoToWeighing" />
          </v-window-item>
          <v-window-item value="tabCompleted">
            <TabCompleted ref="completedRef" />
          </v-window-item>
        </v-window>
      </v-card-text>
    </v-card>
  </div>
</template>

<script lang="ts" setup>
import { nextTick, ref } from 'vue'
import FbaShipmentList from './fba-shipment-list.vue'
import TabDelivered from './tabDelivered.vue'
import TabGoodsToBePicked from './tabGoodsToBePicked.vue'
import TabPicked from './tabPicked.vue'
import TabCompleted from './tabCompleted.vue'
import TabWeighed from './tabWeighed.vue'
import type { DeliveryFlowTab } from './deliveryFlow'

const activeTab = ref('tabFbaShipment')
const fbaShipmentRef = ref<InstanceType<typeof FbaShipmentList>>()
const goodsToBePickedRef = ref<InstanceType<typeof TabGoodsToBePicked>>()
const pickedRef = ref<InstanceType<typeof TabPicked>>()
const weighedRef = ref<InstanceType<typeof TabWeighed>>()
const deliveredRef = ref<InstanceType<typeof TabDelivered>>()
const completedRef = ref<InstanceType<typeof TabCompleted>>()

const handleGoToPicking = (): void => {
  activeTab.value = 'tabGoodsToBePicked'
  nextTick(() => {
    goodsToBePickedRef.value?.getGoodsToBePicked()
  })
}

const handleGoToWeighing = (): void => {
  activeTab.value = 'tabWeighed'
  nextTick(() => {
    weighedRef.value?.getWeighed()
  })
}

const handleGoToDelivery = (targetTab: DeliveryFlowTab): void => {
  activeTab.value = targetTab
  nextTick(() => {
    deliveredRef.value?.getDelivery()
  })
}

const changeTab = (tab: unknown): void => {
  nextTick(() => {
    switch (tab) {
      case 'tabFbaShipment':
        fbaShipmentRef.value?.getFbaShipment()
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
</script>
