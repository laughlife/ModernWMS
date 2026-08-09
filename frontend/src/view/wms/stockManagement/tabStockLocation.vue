<template>
  <div class="operateArea">
    <v-row no-gutters>
      <!-- Operate Btn -->
      <v-col cols="3" class="col">
        <!-- <tooltip-btn icon="mdi-refresh" :tooltip-text="$t('system.page.refresh')" @click="method.refresh"></tooltip-btn>
        <tooltip-btn icon="mdi-export-variant" :tooltip-text="$t('system.page.export')" @click="method.exportTable"> </tooltip-btn> -->
        <BtnGroup :authority-list="data.authorityList" :btn-list="data.btnList" />
      </v-col>

      <!-- Search Input -->
      <v-col cols="9">
        <v-row no-gutters @keyup.enter="method.sureSearch">
          <v-col cols="4">
            <v-select
              v-model="data.searchForm.warehouse_id"
              :items="data.warehouseOptions"
              item-title="name"
              item-value="value"
              clearable
              hide-details
              density="comfortable"
              class="searchInput ml-5 mt-1"
              :label="$t('wms.stockLocation.warehouse_name')"
              variant="solo"
            ></v-select>
          </v-col>
          <v-col cols="4">
            <v-text-field
              v-model="data.searchForm.location_name"
              clearable
              hide-details
              density="comfortable"
              class="searchInput ml-5 mt-1"
              :label="$t('wms.stockLocation.location_name')"
              variant="solo"
            >
            </v-text-field>
          </v-col>
          <v-col cols="4"></v-col>
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
    <vxe-table ref="xTableStockLocation" :column-config="{ minWidth: '100px' }" :row-config="{ height: 76 }" :data="data.tableData" :height="tableHeight" align="center">
      <template #empty>
        {{ i18n.global.t('system.page.noData') }}
      </template>
      <vxe-column type="seq" width="60"></vxe-column>
      <vxe-column type="checkbox" width="50"></vxe-column>
      <vxe-column field="product_image" :title="$t('wms.stockLocation.image')" width="92">
        <template #default="{ row }">
          <product-image :src="row.product_image" :alt="row.spu_name" :width="56" :height="56" class="product-img" />
        </template>
      </vxe-column>
      <vxe-column field="warehouse_name" :title="$t('wms.stockLocation.warehouse_location')" min-width="150">
        <template #default="{ row }">
          <div class="cell-wh">
            <div class="cell-line">{{ row.warehouse_name }}</div>
            <div class="cell-line cell-sub">{{ row.location_name }}</div>
          </div>
        </template>
      </vxe-column>
      <vxe-column field="spu_name" :title="$t('wms.stockLocation.product')" min-width="240">
        <template #default="{ row }">
          <div class="cell-product" @click="method.showSkuInfo(row)">
            <div class="cell-line">{{ row.spu_name }}</div>
            <div class="cell-line cell-sub">SKU：{{ row.sku_code }}</div>
          </div>
        </template>
      </vxe-column>
      <vxe-column field="qty" :title="$t('wms.stockLocation.qty')"></vxe-column>
      <vxe-column field="qty_available" :title="$t('wms.stockLocation.qty_available')"></vxe-column>
      <vxe-column field="qty_locked" :title="$t('wms.stockLocation.qty_locked')"></vxe-column>
      <vxe-column field="price" :title="$t('wms.stockAsnInfo.price')"></vxe-column>
      <vxe-date-column field="expiry_date" :title="$t('wms.stockAsnInfo.expiry_date')"> </vxe-date-column>
      <vxe-date-column field="putaway_date" :title="$t('wms.stockAsnInfo.putaway_date')"> </vxe-date-column>
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
import { StockLocationVO } from '@/types/WMS/StockManagement'
import { PAGE_SIZE, PAGE_LAYOUT, DEFAULT_PAGE_SIZE } from '@/constant/vxeTable'
import { hookComponent } from '@/components/system'
import { DEBOUNCE_TIME } from '@/constant/system'
import { setSearchObject, getMenuAuthorityList } from '@/utils/common'
import { SearchObject, btnGroupItem } from '@/types/System/Form'
import { getStockLocationList } from '@/api/wms/stockManagement'
import { getWarehouseSelect } from '@/api/base/warehouseSetting'
import i18n from '@/languages/i18n'
import customPager from '@/components/custom-pager.vue'
import skuInfo from './sku-info.vue'
import ProductImage from '@/components/system/product-image.vue'
import { exportData } from '@/utils/exportTable'
import BtnGroup from '@/components/system/btnGroup.vue'

const xTableStockLocation = ref()

interface WarehouseOption {
  value: string
  name: string
  is_default: boolean
}

const data = reactive({
  sku_id: 0,
  showDialog: false,
  showDialogShowInfo: false,
  warehouseOptions: [] as WarehouseOption[],
  warehouseOptionsLoaded: false,
  searchForm: {
    warehouse_id: '',
    location_name: ''
  },
  activeTab: null,
  tableData: ref<StockLocationVO[]>([]),
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
  showSkuInfo(row: StockLocationVO) {
    data.sku_id = row.sku_id
    data.showDialogShowInfo = true
  },
  loadWarehouseOptions: async () => {
    const { data: res } = await getWarehouseSelect()
    if (!res.isSuccess) {
      hookComponent.$message({
        type: 'error',
        content: res.errorMessage
      })
      data.warehouseOptionsLoaded = true
      method.getStockLocationList()
      return
    }
    data.warehouseOptions = res.data.map((item: any) => ({
      value: item.value,
      name: item.name,
      is_default: item.is_default === true
    }))
    data.warehouseOptionsLoaded = true
    const currentWarehouse = data.warehouseOptions.find(item => item.is_default)
    if (currentWarehouse) {
      data.searchForm.warehouse_id = currentWarehouse.value
    } else {
      method.getStockLocationList()
    }
  },
  // Refresh data
  refresh: () => {
    method.getStockLocationList()
  },
  getStockLocationList: async () => {
    if (!data.warehouseOptionsLoaded) {
      return
    }
    const { data: res } = await getStockLocationList(data.tablePage)
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

    method.getStockLocationList()
  }),
  exportTable: () => {
    const $table = xTableStockLocation.value
    exportData({
      table: $table,
      filename: i18n.global.t('wms.stockManagement.stockLocation'),
      columnFilterMethod({ column }: any) {
        return !['checkbox'].includes(column?.type) && !['operate'].includes(column?.field)
      }
    })
  },
  sureSearch: () => {
    data.tablePage.searchObjects = setSearchObject(data.searchForm, ['warehouse_id'])
    method.getStockLocationList()
  }
})

onMounted(() => {
  method.loadWarehouseOptions()
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
      code: 'stock-export',
      click: method.exportTable
    }
  ]
})

const cardHeight = computed(() => computedCardHeight({}))
const tableHeight = computed(() => computedTableHeight({}))

defineExpose({
  getStockLocationList: method.getStockLocationList
})
watch(
  () => data.searchForm,
  () => {
    if (!data.warehouseOptionsLoaded) {
      return
    }
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

.cell-wh,
.cell-product {
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  width: 100%;
}

.cell-product {
  cursor: pointer;
}

.cell-line {
  width: 100%;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.cell-sub {
  color: rgba(var(--v-theme-on-surface), 0.62);
  font-size: 12px;
}

.product-img {
  margin: 0 auto;
}
</style>
