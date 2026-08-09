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
                  <th>{{ $t('wms.erpPendingReceipt.sku') }}</th>
                  <th>{{ $t('wms.erpPendingReceipt.product_name') }}</th>
                  <th>{{ $t('wms.erpPendingReceipt.quantity') }}</th>
                  <th>{{ $t('wms.erpPendingReceipt.order_user_name') }}</th>
                  <th>{{ $t('wms.erpPendingReceipt.dept_name') }}</th>
                </tr>
              </thead>
              <tbody>
                <tr v-for="(product, index) in row.product_list" :key="`${row.id}-${product.task_item_id ?? index}`">
                  <td>{{ product.sku || '-' }}</td>
                  <td>{{ product.product_name || '-' }}</td>
                  <td>{{ product.quantity ?? 0 }}</td>
                  <td>{{ product.order_user_name || '-' }}</td>
                  <td>{{ product.dept_name || '-' }}</td>
                </tr>
                <tr v-if="row.product_list.length === 0">
                  <td colspan="5">{{ $t('system.page.noData') }}</td>
                </tr>
              </tbody>
            </v-table>
          </div>
        </template>
      </vxe-column>
      <vxe-column field="shipment_batch_no" :title="$t('wms.erpPendingReceipt.shipment_batch_no')" min-width="180"></vxe-column>
      <vxe-column field="purchase_no" :title="$t('wms.erpPendingReceipt.purchase_no')" min-width="150"></vxe-column>
      <vxe-column field="supplier_name" :title="$t('wms.erpPendingReceipt.supplier_name')" min-width="150"></vxe-column>
      <vxe-column field="product_summary" :title="$t('wms.erpPendingReceipt.product_summary')" min-width="280"></vxe-column>
      <vxe-column field="shipment_qty" :title="$t('wms.erpPendingReceipt.shipment_qty')" width="110"></vxe-column>
      <vxe-column field="tracking_no" :title="$t('wms.erpPendingReceipt.tracking_no')" min-width="180"></vxe-column>
      <vxe-column field="logistics_name" :title="$t('wms.erpPendingReceipt.logistics_name')" min-width="130"></vxe-column>
      <vxe-column field="tracking_status_name" :title="$t('wms.erpPendingReceipt.tracking_status')" min-width="130"></vxe-column>
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
import { getErpArrivedReceiptList, getErpPendingReceiptList } from '@/api/wms/stockAsn'
import { hookComponent } from '@/components/system'
import BtnGroup from '@/components/system/btnGroup.vue'
import customPager from '@/components/custom-pager.vue'
import ErpLogisticsDetail from './erp-logistics-detail.vue'
import ErpReceiptConfirm from './erp-receipt-confirm.vue'
import { computedCardHeight, computedTableHeight } from '@/constant/style'
import { DEFAULT_PAGE_SIZE, PAGE_LAYOUT, PAGE_SIZE } from '@/constant/vxeTable'
import { DEBOUNCE_TIME } from '@/constant/system'
import i18n from '@/languages/i18n'
import type { btnGroupItem, SearchObject } from '@/types/System/Form'
import type { ErpPendingReceiptVO } from '@/types/WMS/StockAsn'
import { getMenuAuthorityList, setSearchObject } from '@/utils/common'
import { exportData } from '@/utils/exportTable'

const props = defineProps<{
  listType: 'pending' | 'arrived'
}>()

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

const method = reactive({
  refresh: () => {
    method.getStockAsnList()
  },
  getStockAsnList: async () => {
    const request = props.listType === 'arrived' ? getErpArrivedReceiptList : getErpPendingReceiptList
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
      filename: i18n.global.t(props.listType === 'arrived' ? 'wms.stockAsn.tabNotice' : 'wms.stockAsn.tabToDoArrival'),
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
    data.tablePage.searchObjects = setSearchObject(data.searchForm)
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
</style>
