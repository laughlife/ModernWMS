<!-- Warehouse Setting -->
<template>
  <div class="container">
    <div class="warehouse-toolbar">
      <v-select
        v-model="selectedWarehouseId"
        :items="warehouseOptions"
        item-title="name"
        item-value="id"
        label="仓库"
        variant="outlined"
        density="compact"
        hide-details
        @update:model-value="handleWarehouseChange"
      ></v-select>
    </div>

    <div>
      <v-tabs v-model="data.activeTab" class="receipt-status-tabs" stacked @update:model-value="method.changeTabs">
        <v-tab v-for="(item, index) of tabsConfig" :key="index" :value="item.value">
          <v-badge
            class="status-count-badge"
            color="primary"
            text-color="on-primary"
            :content="String(statusCounts[item.value])"
            location="top end"
          >
            <v-icon>{{ item.icon }}</v-icon>
          </v-badge>
          <p class="tabItemTitle">{{ item.tabName }}</p>
        </v-tab>
      </v-tabs>

      <!-- Main Content -->
      <v-card class="mt-5">
        <v-card-text>
          <v-window v-model="data.activeTab">
            <v-window-item value="tabNotice">
              <ErpReceiptShipmentList ref="tabNoticeRef" list-type="arrived" :warehouse-id="selectedWarehouseId" />
            </v-window-item>
            <v-window-item value="tabToShip">
              <ErpReceiptShipmentList ref="tabToShipRef" list-type="to-ship" :warehouse-id="selectedWarehouseId" />
            </v-window-item>
            <v-window-item value="tabToDoArrival">
              <ErpReceiptShipmentList ref="tabToDoArrivalRef" list-type="pending" :warehouse-id="selectedWarehouseId" />
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
import { onMounted, reactive, ref, nextTick } from 'vue'
import i18n from '@/languages/i18n'
import { getDispatchWarehouseAccess } from '@/api/wms/dispatchWorkflow'
import {
  getErpArrivedReceiptList,
  getErpPendingReceiptList,
  getErpReceiptDetailList,
  getErpToShipReceiptList
} from '@/api/wms/stockAsn'
import type { WarehouseOption } from '@/types/DeliveryManagement/DispatchWorkflow'
import type { PageConfigProps } from '@/types/System/Form'
import ErpReceiptShipmentList from './erp-receipt-shipment-list.vue'
import tabReceiptDetails from './tabReceiptDetails.vue'

const tabNoticeRef = ref()
const tabToShipRef = ref()
const tabToDoArrivalRef = ref()
const tabReceiptDetailsRef = ref()

const warehouseOptions = ref<WarehouseOption[]>([])
const selectedWarehouseId = ref<number | null>(null)

const statusCounts = reactive<Record<string, number>>({
  tabNotice: 0,
  tabToShip: 0,
  tabToDoArrival: 0,
  tabReceiptDetails: 0
})
let statusCountRequestId = 0

const tabsConfig = [
  {
    value: 'tabNotice',
    icon: 'mdi-checkbox-blank-badge',
    tabName: i18n.global.t('wms.stockAsn.tabNotice')
  },
  {
    value: 'tabToShip',
    icon: 'mdi-truck-outline',
    tabName: '待发货'
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
        case 'tabToShip':
          if (tabToShipRef?.value?.getStockAsnList) {
            tabToShipRef.value.getStockAsnList()
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

const handleWarehouseChange = (): void => {
  method.changeTabs(data.activeTab)
  refreshStatusCounts()
}

const loadListTotal = async (
  loader: (data: PageConfigProps) => Promise<{ data: any }>,
  pageData: PageConfigProps
): Promise<number> => {
  try {
    const { data: res } = await loader(pageData)
    return res?.isSuccess ? res.data?.totals ?? 0 : 0
  } catch {
    return 0
  }
}

const refreshStatusCounts = async (): Promise<void> => {
  const warehouseId = selectedWarehouseId.value
  if (warehouseId === null) {
    statusCounts.tabNotice = 0
    statusCounts.tabToShip = 0
    statusCounts.tabToDoArrival = 0
    statusCounts.tabReceiptDetails = 0
    return
  }

  const requestId = ++statusCountRequestId
  const warehouseSearchObjects = [{
    name: 'warehouse_id',
    operator: 1,
    text: String(warehouseId),
    value: String(warehouseId)
  }]
  const [arrived, toShip, pending, details] = await Promise.all([
    loadListTotal(getErpArrivedReceiptList, { pageIndex: 1, pageSize: 1, searchObjects: warehouseSearchObjects }),
    loadListTotal(getErpToShipReceiptList, { pageIndex: 1, pageSize: 1, searchObjects: warehouseSearchObjects }),
    loadListTotal(getErpPendingReceiptList, { pageIndex: 1, pageSize: 1, searchObjects: warehouseSearchObjects }),
    loadListTotal(getErpReceiptDetailList, { pageIndex: 1, pageSize: 1, searchObjects: [] })
  ])
  if (requestId !== statusCountRequestId) return

  statusCounts.tabNotice = arrived
  statusCounts.tabToShip = toShip
  statusCounts.tabToDoArrival = pending
  statusCounts.tabReceiptDetails = details
}

const initializeWarehouse = async (): Promise<void> => {
  try {
    const result = await getDispatchWarehouseAccess()
    if (!result.isSuccess) return
    warehouseOptions.value = result.data.warehouses
    selectedWarehouseId.value = result.data.default_warehouse_id
    await refreshStatusCounts()
  } catch {
    // Keep the last successful warehouse options when the access endpoint is unavailable.
  }
}

onMounted(initializeWarehouse)
</script>

<style scoped lang="less">
.warehouse-toolbar {
  width: min(360px, 100%);
}

.receipt-status-tabs {
  margin-top: 12px;
}

.receipt-status-tabs :deep(.v-btn__content) {
  padding-top: 15px;
}

.status-count-badge :deep(.v-badge__badge) {
  transform: translateX(10px);
}

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
