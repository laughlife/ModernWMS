<template>
  <div class="operateArea">
    <v-row no-gutters>
      <v-col cols="3" class="col">
        <BtnGroup :authority-list="data.authorityList" :btn-list="data.btnList" />
      </v-col>

      <v-col cols="9">
        <v-row no-gutters @keyup.enter="method.sureSearch">
          <v-col cols="4"></v-col>
          <v-col cols="4">
            <v-text-field
              v-model="data.searchForm.supplier_name"
              clearable
              hide-details
              density="comfortable"
              class="searchInput ml-5 mt-1"
              :label="$t('wms.erpPendingReceipt.supplier_name')"
              variant="solo"
            ></v-text-field>
          </v-col>
          <v-col cols="4">
            <v-text-field
              v-model="data.searchForm.product_keyword"
              clearable
              hide-details
              density="comfortable"
              class="searchInput ml-5 mt-1"
              :label="$t('wms.erpPendingReceipt.product_keyword')"
              variant="solo"
            ></v-text-field>
          </v-col>
        </v-row>
      </v-col>
    </v-row>
  </div>

  <div class="mt-5" :style="{ height: cardHeight }">
    <vxe-table ref="xTable" :column-config="{ minWidth: '120px' }" :data="data.tableData" :height="tableHeight" align="center">
      <template #empty>
        {{ i18n.global.t('system.page.noData') }}
      </template>
      <vxe-column type="seq" width="60"></vxe-column>
      <vxe-column type="expand" width="60">
        <template #content="{ row }">
          <div class="productDetail">
            <v-table density="compact">
              <thead>
                <tr>
                  <th class="productInfoHeader">{{ $t('wms.erpPendingReceipt.product_info') }}</th>
                  <th>{{ $t('wms.erpPendingReceipt.quantity') }}</th>
                  <th>{{ $t('wms.erpPendingReceipt.order_user_name') }}</th>
                  <th>{{ $t('wms.erpPendingReceipt.dept_name') }}</th>
                </tr>
              </thead>
              <tbody>
                <tr v-for="(product, index) in row.product_list" :key="`${row.id}-${product.task_item_id ?? index}`">
                  <td class="productInfoCell">
                    <div class="productInfoName">{{ product.product_name || '-' }}</div>
                    <div class="productInfoSku">{{ product.sku || '-' }}</div>
                  </td>
                  <td>{{ product.quantity ?? 0 }}</td>
                  <td>{{ product.order_user_name || '-' }}</td>
                  <td>{{ product.dept_name || '-' }}</td>
                </tr>
                <tr v-if="row.product_list.length === 0">
                  <td colspan="4">{{ $t('system.page.noData') }}</td>
                </tr>
              </tbody>
              <tfoot>
                <tr>
                  <td colspan="4" class="supplierFooter">
                    {{ $t('wms.erpPendingReceipt.supplier_name') }}：{{ row.supplier_name || '-' }}
                  </td>
                </tr>
              </tfoot>
            </v-table>
          </div>
        </template>
      </vxe-column>
      <vxe-column width="96">
        <template #default="{ row }">
          <div class="shipmentProductImages">
            <ProductImage
              v-for="(product, index) in row.product_list"
              :key="product.task_item_id ?? index"
              :src="product.main_image"
              :alt="product.product_name || product.sku || $t('wms.erpPendingReceipt.product_info')"
              :width="64"
              :height="64"
            />
          </div>
        </template>
      </vxe-column>
      <vxe-column :title="$t('wms.erpPendingReceipt.shipment_info')" min-width="190">
        <template #default="{ row }">
          <div class="shipmentInfoCell">
            <div class="shipmentBatchNo">{{ row.shipment_batch_no || '-' }}</div>
            <div class="shipmentPurchaseNo">{{ row.purchase_no || '-' }}</div>
          </div>
        </template>
      </vxe-column>
      <vxe-column :title="$t('wms.erpPendingReceipt.product_summary')" min-width="280">
        <template #default="{ row }">
          <div class="productSummaryList">
            <div v-for="(product, index) in row.product_list" :key="product.task_item_id ?? index" class="productSummaryItem">
              <div class="productSummaryTitle">{{ buildReceiptProductDisplay(product).title }}</div>
              <div class="productSummarySku">{{ buildReceiptProductDisplay(product).sku }}</div>
            </div>
            <div v-if="row.product_list.length === 0">{{ row.product_summary || '-' }}</div>
          </div>
        </template>
      </vxe-column>
      <vxe-column field="shipment_qty" :title="$t('wms.erpPendingReceipt.shipment_qty')" width="110"></vxe-column>
      <vxe-column :title="$t('wms.erpPendingReceipt.logistics_detail_title')" min-width="200">
        <template #default="{ row }">
          <div class="logisticsInfoCell">
            <div class="logisticsPrimaryLine">{{ row.logistics_name || '-' }}</div>
            <div class="logisticsSecondaryLine">{{ row.tracking_no || '-' }}</div>
            <div class="logisticsSecondaryLine">{{ method.displayTrackingStatus(row.tracking_status_name) }}</div>
          </div>
        </template>
      </vxe-column>
      <vxe-column field="latest_event_desc" :title="$t('wms.erpPendingReceipt.latest_event_desc')" min-width="240"></vxe-column>
      <vxe-column field="shipment_time" :title="$t('wms.erpPendingReceipt.shipment_time')" min-width="170"></vxe-column>
      <vxe-column field="warehouse_name" :title="$t('wms.erpPendingReceipt.warehouse_name')" min-width="150"></vxe-column>
      <vxe-column field="order_user_text" :title="$t('wms.erpPendingReceipt.order_user_text')" min-width="150"></vxe-column>
      <vxe-column fixed="right" :title="$t('system.page.operate')" width="190">
        <template #default="{ row }">
          <v-btn color="info" size="small" variant="text" @click="method.openLogisticsDialog(row)">
            {{ $t('wms.erpPendingReceipt.view_logistics') }}
          </v-btn>
          <v-btn color="primary" size="small" variant="text" @click="method.openReceiptDialog(row)">
            {{ $t('wms.erpPendingReceipt.receipt_action') }}
          </v-btn>
        </template>
      </vxe-column>
    </vxe-table>
    <custom-pager
      :current-page="data.tablePage.pageIndex"
      :page-size="data.tablePage.pageSize"
      perfect
      :total="data.tablePage.total"
      :page-sizes="PAGE_SIZE"
      :layouts="PAGE_LAYOUT"
      @page-change="method.handlePageChange"
    ></custom-pager>
  </div>

  <ErpLogisticsDetail ref="logisticsDetailRef" />
  <ErpReceiptConfirm ref="receiptConfirmRef" @saved="method.getStockAsnList" />
</template>

<script lang="ts" setup>
import { computed, onMounted, reactive, ref, watch } from 'vue'
import type { VxePagerEvents } from 'vxe-table'
import { getErpArrivedReceiptList, getErpPendingReceiptList, getErpToShipReceiptList } from '@/api/wms/stockAsn'
import { hookComponent } from '@/components/system'
import BtnGroup from '@/components/system/btnGroup.vue'
import customPager from '@/components/custom-pager.vue'
import ProductImage from '@/components/system/product-image.vue'
import ErpLogisticsDetail from './erp-logistics-detail.vue'
import ErpReceiptConfirm from './erp-receipt-confirm.vue'
import { computedCardHeight, computedTableHeight } from '@/constant/style'
import { DEFAULT_PAGE_SIZE, PAGE_LAYOUT, PAGE_SIZE } from '@/constant/vxeTable'
import { DEBOUNCE_TIME } from '@/constant/system'
import i18n from '@/languages/i18n'
import { SearchOperator, type btnGroupItem, type SearchObject } from '@/types/System/Form'
import type { ErpPendingReceiptVO } from '@/types/WMS/StockAsn'
import { getMenuAuthorityList, setSearchObject } from '@/utils/common'
import { exportData } from '@/utils/exportTable'
import { buildReceiptProductDisplay } from '@/utils/receiptProductDisplay'

type ReceiptListType = 'to-ship' | 'pending' | 'arrived'

const props = defineProps<{
  listType: ReceiptListType
  warehouseId: number | null
}>()

const LIST_API: Record<ReceiptListType, typeof getErpPendingReceiptList> = {
  'to-ship': getErpToShipReceiptList,
  pending: getErpPendingReceiptList,
  arrived: getErpArrivedReceiptList
}

const LIST_TITLE: Record<ReceiptListType, string> = {
  'to-ship': '待发货',
  pending: i18n.global.t('wms.stockAsn.tabToDoArrival'),
  arrived: i18n.global.t('wms.stockAsn.tabNotice')
}

const xTable = ref()
const logisticsDetailRef = ref<InstanceType<typeof ErpLogisticsDetail>>()
const receiptConfirmRef = ref<InstanceType<typeof ErpReceiptConfirm>>()

const data = reactive({
  searchForm: {
    supplier_name: '',
    product_keyword: ''
  },
  tableData: ref<ErpPendingReceiptVO[]>([]),
  tablePage: reactive({
    total: 0,
    pageIndex: 1,
    pageSize: DEFAULT_PAGE_SIZE,
    searchObjects: ref<Array<SearchObject>>([])
  }),
  timer: ref<ReturnType<typeof setTimeout> | null>(null),
  btnList: [] as btnGroupItem[],
  authorityList: getMenuAuthorityList()
})

const buildSearchObjects = (): SearchObject[] => {
  const searchObjects = setSearchObject(data.searchForm)
  if (props.warehouseId !== null) {
    searchObjects.push({
      name: 'warehouse_id',
      operator: SearchOperator.EQUAL,
      text: String(props.warehouseId),
      value: String(props.warehouseId)
    })
  }
  return searchObjects
}

const method = reactive({
  displayTrackingStatus: (value?: string | null): string => {
    const normalized = (value ?? '').trim()
    if (normalized === '') return '-'
    return normalized.toUpperCase() === 'UNKNOWN' ? '未知' : value as string
  },
  refresh: () => {
    method.getStockAsnList()
  },
  getStockAsnList: async () => {
    if (props.warehouseId === null) {
      data.tableData = []
      data.tablePage.total = 0
      return
    }
    data.tablePage.searchObjects = buildSearchObjects()
    const request = LIST_API[props.listType]
    const { data: res } = await request(data.tablePage)
    if (!res.isSuccess) {
      hookComponent.$message({ type: 'error', content: res.errorMessage })
      return
    }
    data.tableData = res.data.rows
    data.tablePage.total = res.data.totals
  },
  handlePageChange: ref<VxePagerEvents.PageChange>(({ currentPage, pageSize }) => {
    data.tablePage.pageIndex = currentPage
    data.tablePage.pageSize = pageSize
    method.getStockAsnList()
  }),
  exportTable: () => {
    exportData({
      table: xTable.value,
      filename: LIST_TITLE[props.listType],
      columnFilterMethod({ column }: any) {
        return !['expand'].includes(column?.type) && !['operate'].includes(column?.field)
      }
    })
  },
  openLogisticsDialog: (row: ErpPendingReceiptVO) => {
    logisticsDetailRef.value?.openDialog(row)
  },
  openReceiptDialog: (row: ErpPendingReceiptVO) => {
    receiptConfirmRef.value?.openDialog(row)
  },
  sureSearch: () => {
    data.tablePage.pageIndex = 1
    method.getStockAsnList()
  }
})

onMounted(() => {
  data.btnList = [
    {
      name: i18n.global.t('system.page.refresh'),
      icon: 'mdi-refresh',
      code: '',
      click: method.refresh
    },
    {
      name: i18n.global.t('system.page.export'),
      icon: 'mdi-export-variant',
      code: props.listType === 'arrived' ? 'notice-export' : 'delivered-export',
      click: method.exportTable
    }
  ]
})

const cardHeight = computed(() => computedCardHeight({}))
const tableHeight = computed(() => computedTableHeight({}))

defineExpose({
  getStockAsnList: method.getStockAsnList
})

watch(
  () => data.searchForm,
  () => {
    if (data.timer) clearTimeout(data.timer)
    data.timer = setTimeout(() => {
      data.timer = null
      method.sureSearch()
    }, DEBOUNCE_TIME)
  },
  { deep: true }
)

watch(
  () => props.warehouseId,
  () => {
    data.tablePage.pageIndex = 1
    method.getStockAsnList()
  }
)
</script>

<style lang="less" scoped>
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

.productDetail {
  padding: 12px 72px;
}

.productInfoHeader,
.productInfoCell {
  min-width: 220px;
  text-align: left !important;
}

.productInfoName {
  color: rgba(var(--v-theme-on-surface), 0.87);
  font-weight: 500;
}

.productInfoSku {
  margin-top: 4px;
  color: rgba(var(--v-theme-on-surface), 0.6);
  font-size: 12px;
}

.productSummaryList {
  display: flex;
  flex-direction: column;
  gap: 8px;
  text-align: left;
}

.productSummaryItem + .productSummaryItem {
  padding-top: 8px;
  border-top: 1px solid rgba(var(--v-border-color), var(--v-border-opacity));
}

.productSummaryTitle {
  color: rgba(var(--v-theme-on-surface), 0.87);
  font-weight: 500;
}

.productSummarySku {
  margin-top: 3px;
  color: rgba(var(--v-theme-on-surface), 0.6);
  font-size: 12px;
}

.shipmentProductImages {
  display: flex;
  flex-direction: column;
  gap: 6px;
  justify-content: center;
  align-items: center;
  padding: 6px 0;
}

.shipmentInfoCell {
  text-align: left;
}

.shipmentBatchNo {
  color: rgba(var(--v-theme-on-surface), 0.87);
  font-weight: 500;
}

.shipmentPurchaseNo {
  margin-top: 4px;
  color: rgba(var(--v-theme-on-surface), 0.6);
  font-size: 12px;
}

.supplierFooter {
  text-align: left !important;
  padding-top: 10px !important;
  border-top: 1px solid rgba(var(--v-border-color), var(--v-border-opacity));
  font-weight: 500;
}

.logisticsInfoCell {
  text-align: left;
}

.logisticsPrimaryLine {
  color: rgba(var(--v-theme-on-surface), 0.87);
  font-weight: 500;
}

.logisticsSecondaryLine {
  margin-top: 4px;
  color: rgba(var(--v-theme-on-surface), 0.6);
  font-size: 12px;
}
</style>
