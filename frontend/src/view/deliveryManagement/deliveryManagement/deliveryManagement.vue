<template>
  <div class="container">
    <v-tabs v-model="activeTab" stacked @update:model-value="changeTab">
      <v-tab value="tabFbaShipment">
        <v-icon>mdi-truck-fast-outline</v-icon>
        <p class="tabItemTitle">{{ $t('wms.deliveryManagement.fbaShipment') }}</p>
      </v-tab>
      <v-tab value="tabWeighed">
        <v-icon>mdi-basket-fill</v-icon>
        <p class="tabItemTitle">{{ $t('wms.deliveryManagement.weighed') }}</p>
      </v-tab>
      <v-tab value="tabDelivered">
        <v-icon>mdi-send-outline</v-icon>
        <p class="tabItemTitle">{{ $t('wms.deliveryManagement.outOfWarehouse') }}</p>
      </v-tab>
      <v-tab value="tabSignIn">
        <v-icon>mdi-check-circle</v-icon>
        <p class="tabItemTitle">{{ $t('wms.deliveryManagement.signedIn') }}</p>
      </v-tab>
    </v-tabs>

    <v-card class="mt-5">
      <v-card-text>
        <v-window v-model="activeTab">
          <v-window-item value="tabFbaShipment">
            <FbaShipmentList ref="fbaShipmentRef" />
          </v-window-item>
          <v-window-item value="tabWeighed">
            <TabWeighed ref="weighedRef" />
          </v-window-item>
          <v-window-item value="tabDelivered">
            <TabDelivered ref="deliveredRef" />
          </v-window-item>
          <v-window-item value="tabSignIn">
            <TabSignIn ref="signInRef" />
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
import TabSignIn from './tabSignIn.vue'
import TabWeighed from './tabWeighed.vue'

const activeTab = ref('tabFbaShipment')
const fbaShipmentRef = ref<InstanceType<typeof FbaShipmentList>>()
const weighedRef = ref<InstanceType<typeof TabWeighed>>()
const deliveredRef = ref<InstanceType<typeof TabDelivered>>()
const signInRef = ref<InstanceType<typeof TabSignIn>>()

const changeTab = (tab: unknown): void => {
  nextTick(() => {
    switch (tab) {
      case 'tabFbaShipment':
        fbaShipmentRef.value?.getFbaShipment()
        break
      case 'tabWeighed':
        weighedRef.value?.getWeighed()
        break
      case 'tabDelivered':
        deliveredRef.value?.getDelivery()
        break
      case 'tabSignIn':
        signInRef.value?.getSignIn()
        break
    }
  })
}
</script>
