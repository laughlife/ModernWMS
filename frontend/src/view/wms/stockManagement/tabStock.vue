<template>
  <div class="operateArea">
    <v-row no-gutters>
      <!-- Operate Btn -->
      <v-col cols="3" class="col">
        <!-- <tooltip-btn icon="mdi-refresh" :tooltip-text="$t('system.page.refresh')" @click="method.refresh"></tooltip-btn>
        <tooltip-btn icon="mdi-export-variant" :tooltip-text="$t('system.page.export')" @click="method.exportTable"> </tooltip-btn> -->
        <!-- new version -->
        <BtnGroup :authority-list="data.authorityList" :btn-list="data.btnList" />
      </v-col>

      <!-- Search Input -->
      <v-col cols="9">
        <v-row no-gutters @keyup.enter="method.sureSearch">
          <v-col cols="4"></v-col>
          <v-col cols="4"></v-col>
          <v-col cols="4">
            <v-text-field
              v-model="data.searchForm.product_keyword"
              clearable
              hide-details
              density="comfortable"
              class="searchInput ml-5 mt-1"
              :label="$t('wms.stockList.product_keyword')"
              variant="solo"
            >
            </v-text-field>
          </v-col>
        </v-row>
      </v-col>
    </v-row>
  </div>

  <!-- Table -->
  <div
    class="mt-5"
    :style="{
      height: cardHeight
    }"
  >
    <vxe-table ref="xTableWarehouse" :column-config="{ minWidth: '100px' }" :data="data.tableData" :height="tableHeight" align="center">
      <template #empty>
        {{ i18n.global.t('system.page.noData') }}
      </template>
      <vxe-column type="seq" width="60"></vxe-column>
      <vxe-column type="checkbox" width="50"></vxe-column>
      <vxe-column field="product_image" :title="$t('wms.stockList.image')" width="92">
        <template #default="{ row }">
          <product-image :src="row.product_image" :alt="row.spu_name" :width="56" :height="56" class="product-img" />
        </template>
      </vxe-column>
      <vxe-column field="spu_name" :title="$t('wms.stockList.product_info')" min-width="360" align="left" header-align="left" :show-overflow="false">
        <template #default="{ row }">
          <div class="product-info" @click="method.showSkuInfo(row)">
            <div class="product-info__name">{{ row.spu_name }}</div>
            <div class="product-info__sku">SKU：{{ row.sku_code }}</div>
          </div>
        </template>
      </vxe-column>
      <vxe-column field="qty" :title="$t('wms.stockList.qty')"></vxe-column>
      <vxe-column field="qty_available" :title="$t('wms.stockList.qty_available')"></vxe-column>
      <vxe-column field="qty_locked" :title="$t('wms.stockList.qty_locked')"></vxe-column>
      <vxe-column field="qty_pending_location" title="待确认库位"></vxe-column>
      <vxe-column field="erp_total_qty" title="ERP总库存"></vxe-column>
      <vxe-column field="erp_available_qty" title="ERP可用库存"></vxe-column>
      <vxe-column field="erp_occupied_qty" title="ERP占用库存"></vxe-column>
      <vxe-column field="allocation_consistent" title="分配校验" width="120">
        <template #default="{ row }">
          <v-chip :color="row.allocation_consistent ? 'success' : 'error'" size="x-small" variant="tonal">
            {{ row.allocation_consistent ? '一致' : '不一致' }}
          </v-chip>
        </template>
      </vxe-column>
      <vxe-column field="qty_to_sort" :title="$t('wms.stockList.qty_to_sort')"></vxe-column>
      <vxe-column field="qty_sorted" :title="$t('wms.stockList.qty_sorted')"></vxe-column>
    </vxe-table>
    <custom-pager
      :current-page="data.tablePage.pageIndex"
      :page-size="data.tablePage.pageSize"
      perfect
      :total="data.tablePage.total"
      :page-sizes="PAGE_SIZE"
      :layouts="PAGE_LAYOUT"
      @page-change="method.handlePageChange"
    >
    </custom-pager>
  </div>
  <skuInfo :show-dialog="data.showDialogShowInfo" :sku_id="data.sku_id" @close="method.closeDialogShowInfo" />
</template>

<script lang="ts" setup>
import { computed, ref, reactive, watch, onMounted } from 'vue'
import { VxePagerEvents } from 'vxe-table'
import { computedCardHeight, computedTableHeight } from '@/constant/style'
import { StockVO } from '@/types/WMS/StockManagement'
import { PAGE_SIZE, PAGE_LAYOUT, DEFAULT_PAGE_SIZE } from '@/constant/vxeTable'
import { hookComponent } from '@/components/system'
import { DEBOUNCE_TIME } from '@/constant/system'
import { setSearchObject, getMenuAuthorityList } from '@/utils/common'
import { SearchObject, btnGroupItem } from '@/types/System/Form'
import { getStockList } from '@/api/wms/stockManagement'
import i18n from '@/languages/i18n'
import customPager from '@/components/custom-pager.vue'
import skuInfo from './sku-info.vue'
import ProductImage from '@/components/system/product-image.vue'
import { exportData } from '@/utils/exportTable'
import BtnGroup from '@/components/system/btnGroup.vue'

const xTableWarehouse = ref()

const data = reactive({
  sku_id: 0,
  showDialog: false,
  showDialogShowInfo: false,
  searchForm: {
    product_keyword: ''
  },
  activeTab: null,
  tableData: ref<StockVO[]>([]),
  tablePage: reactive({
    total: 0,
    pageIndex: 1,
    pageSize: DEFAULT_PAGE_SIZE,
    searchObjects: ref<Array<SearchObject>>([])
  }),
  timer: ref<any>(null),
  btnList: [] as btnGroupItem[],
  // Menu operation permissions
  authorityList: getMenuAuthorityList()
})

const method = reactive({
  closeDialogShowInfo: () => {
    data.showDialogShowInfo = false
  },
  showSkuInfo(row: StockVO) {
    data.sku_id = row.sku_id
    data.showDialogShowInfo = true
  },
  sumNum: (list: any[], field: string) => {
    let count = 0
    list.forEach((item) => {
      count += Number(item[field])
    })
    return count
  },
  // footerMethod:ref<VxeTablePropTypes.FooterMethod>({ columns, data }) => {
  //   columns.map((column, columnIndex) => {
  //     if (columnIndex === 0) {
  //       return '合计'
  //     }
  //     if (['qty', 'qty_available'].includes(column.field)) {
  //       return method.sumNum(data, column.field)
  //     }
  //     return null
  //   })
  // },
  // Refresh data
  refresh: () => {
    method.getStockList()
  },
  getStockList: async () => {
    const { data: res } = await getStockList(data.tablePage)
    if (!res.isSuccess) {
      hookComponent.$message({
        type: 'error',
        content: res.errorMessage
      })
      return
    }
    data.tableData = res.data.rows
    data.tablePage.total = res.data.totals
  },
  handlePageChange: ref<VxePagerEvents.PageChange>(({ currentPage, pageSize }) => {
    data.tablePage.pageIndex = currentPage
    data.tablePage.pageSize = pageSize

    method.getStockList()
  }),
  exportTable: () => {
    const $table = xTableWarehouse.value
    exportData({
      table: $table,
      filename: i18n.global.t('wms.stockManagement.stock'),
      columnFilterMethod({ column }: any) {
        return !['checkbox'].includes(column?.type) && !['operate'].includes(column?.field)
      }
    })
  },
  sureSearch: () => {
    data.tablePage.searchObjects = setSearchObject(data.searchForm)
    method.getStockList()
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
      code: 'area-export',
      click: method.exportTable
    }
  ]
})

const cardHeight = computed(() => computedCardHeight({}))
const tableHeight = computed(() => computedTableHeight({}))

defineExpose({
  getStockList: method.getStockList
})
watch(
  () => data.searchForm,
  () => {
    // debounce
    if (data.timer) {
      clearTimeout(data.timer)
    }
    data.timer = setTimeout(() => {
      data.timer = null
      method.sureSearch()
    }, DEBOUNCE_TIME)
  },
  {
    deep: true
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

.product-img {
  margin: 0 auto;
}

.product-info {
  padding: 10px 0;
  text-align: left;
  white-space: normal;
  overflow-wrap: anywhere;
  cursor: pointer;
}

.product-info__name,
.product-info__sku {
  line-height: 22px;
}

.product-info__sku {
  color: rgba(0, 0, 0, 0.6);
}
</style>
