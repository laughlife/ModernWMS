<template>
  <div class="operateArea">
    <v-row no-gutters>
      <v-col cols="3" class="col">
        <BtnGroup :authority-list="data.authorityList" :btn-list="data.btnList" />
      </v-col>
      <v-col cols="9">
        <v-row no-gutters @keyup.enter="method.sureSearch">
          <v-col cols="4">
            <v-text-field
              v-model="data.searchForm.dept_name"
              clearable
              hide-details
              density="comfortable"
              class="searchInput ml-5 mt-1"
              :label="$t('wms.erpPendingReceipt.dept_name')"
              variant="solo"
            ></v-text-field>
          </v-col>
          <v-col cols="4">
            <v-text-field
              v-model="data.searchForm.order_user_name"
              clearable
              hide-details
              density="comfortable"
              class="searchInput ml-5 mt-1"
              :label="$t('wms.erpPendingReceipt.order_user_name')"
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
              :label="$t('wms.erpPendingReceipt.receipt_detail_keyword')"
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
      <vxe-column field="shipment_batch_no" :title="$t('wms.erpPendingReceipt.shipment_batch_no')" min-width="180"></vxe-column>
      <vxe-column field="commodity_sku" :title="$t('wms.erpPendingReceipt.sku')" min-width="150"></vxe-column>
      <vxe-column field="commodity_name" :title="$t('wms.erpPendingReceipt.product_name')" min-width="220"></vxe-column>
      <vxe-column field="warehouse_area_name" :title="$t('wms.erpPendingReceipt.warehouse_area_name')" min-width="150"></vxe-column>
      <vxe-column field="dept_name" :title="$t('wms.erpPendingReceipt.dept_name')" min-width="140"></vxe-column>
      <vxe-column field="order_user_name" :title="$t('wms.erpPendingReceipt.order_user_name')" min-width="120"></vxe-column>
      <vxe-column field="receipt_time" :title="$t('wms.erpPendingReceipt.receipt_time')" min-width="180"></vxe-column>
      <vxe-column field="actual_receipt_qty" :title="$t('wms.erpPendingReceipt.actual_receipt_qty')" width="130"></vxe-column>
      <vxe-column field="loss_qty" :title="$t('wms.erpPendingReceipt.loss_qty')" width="110"></vxe-column>
      <vxe-column field="inbound_qty" :title="$t('wms.erpPendingReceipt.inbound_qty')" width="110"></vxe-column>
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
</template>

<script lang="ts" setup>
import { computed, onMounted, reactive, ref, watch } from 'vue'
import type { VxePagerEvents } from 'vxe-table'
import { getErpReceiptDetailList } from '@/api/wms/stockAsn'
import { hookComponent } from '@/components/system'
import BtnGroup from '@/components/system/btnGroup.vue'
import customPager from '@/components/custom-pager.vue'
import { computedCardHeight, computedTableHeight } from '@/constant/style'
import { DEFAULT_PAGE_SIZE, PAGE_LAYOUT, PAGE_SIZE } from '@/constant/vxeTable'
import { DEBOUNCE_TIME } from '@/constant/system'
import i18n from '@/languages/i18n'
import type { btnGroupItem, SearchObject } from '@/types/System/Form'
import type { ErpReceiptDetailVO } from '@/types/WMS/StockAsn'
import { getMenuAuthorityList, setSearchObject } from '@/utils/common'
import { exportData } from '@/utils/exportTable'

const xTable = ref()

const data = reactive({
  searchForm: {
    dept_name: '',
    order_user_name: '',
    product_keyword: ''
  },
  tableData: ref<ErpReceiptDetailVO[]>([]),
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
    const { data: res } = await getErpReceiptDetailList(data.tablePage)
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
      filename: i18n.global.t('wms.stockAsn.tabReceiptDetails')
    })
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
      code: 'detail-export',
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
</style>
