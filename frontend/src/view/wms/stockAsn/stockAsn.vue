<!-- Warehouse Setting -->
<template>
  <div class="container">
    <div>
      <v-tabs v-model="data.activeTab" stacked @update:model-value="method.changeTabs">
        <v-tab v-for="(item, index) of tabsConfig" :key="index" :value="item.value">
          <v-icon>{{ item.icon }}</v-icon>
          <p class="tabItemTitle">{{ item.tabName }}</p>
        </v-tab>
      </v-tabs>

      <!-- Main Content -->
      <v-card class="mt-5">
        <v-card-text>
          <v-window v-model="data.activeTab">
            <v-window-item value="tabNotice">
              <ErpReceiptShipmentList ref="tabNoticeRef" list-type="arrived" />
            </v-window-item>
            <v-window-item value="tabToDoArrival">
              <ErpReceiptShipmentList ref="tabToDoArrivalRef" list-type="pending" />
            </v-window-item>
            <v-window-item value="tabReceiptDetails">
              <tabReceiptDetails ref="tabReceiptDetailsRef" />
            </v-window-item>
          </v-window>
        </v-card-text>
      </v-card>
    </div>
  </div>
</template>

<script lang="ts" setup>
import { ref, reactive, nextTick } from 'vue'
import i18n from '@/languages/i18n'
import ErpReceiptShipmentList from './erp-receipt-shipment-list.vue'
import tabReceiptDetails from './tabReceiptDetails.vue'

const tabNoticeRef = ref()
const tabToDoArrivalRef = ref()
const tabReceiptDetailsRef = ref()

const tabsConfig = [
  {
    value: 'tabNotice',
    icon: 'mdi-checkbox-blank-badge',
    tabName: i18n.global.t('wms.stockAsn.tabNotice')
  },
  {
    value: 'tabToDoArrival',
    icon: 'mdi-truck-cargo-container',
    tabName: i18n.global.t('wms.stockAsn.tabToDoArrival')
  },
  {
    value: 'tabReceiptDetails',
    icon: 'mdi-file-cabinet',
    tabName: i18n.global.t('wms.stockAsn.tabReceiptDetails')
  }
]

const data = reactive({
  activeTab: '',
  isLoadNotice: false,
  isLoadToDoArrival: false,
  isLoadReceiptDetails: false
})

const method = reactive({
  changeTabs: (e: any): void => {
    nextTick(() => {
      switch (e) {
        case 'tabNotice':
          // Tips：Must be write the nextTick so that can get DOM!!
          if (tabNoticeRef?.value?.getStockAsnList) {
            tabNoticeRef.value.getStockAsnList()
          }
          break
        case 'tabToDoArrival':
          if (tabToDoArrivalRef?.value?.getStockAsnList) {
            tabToDoArrivalRef.value.getStockAsnList()
          }
          break
        case 'tabReceiptDetails':
          if (tabReceiptDetailsRef?.value?.getStockAsnList) {
            tabReceiptDetailsRef.value.getStockAsnList()
          }
          break
      }
    })
  }
})

</script>

<style scoped lang="less">
.operateArea {
  width: 100%;
  min-width: 760px;
  display: flex;
  align-items: center;
  border-radius: 10px;
  padding: 0 10px;
}

.col {
  display: flex;
  align-items: center;
}
</style>
