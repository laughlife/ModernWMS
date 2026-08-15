<template>
  <div class="flowTip">
    <v-icon icon="mdi-information-outline" size="20"></v-icon>
    <span>{{ $t('wms.deliveryManagement.packingTaskFlowTip') }}</span>
  </div>

  <div class="operateArea">
    <v-row no-gutters>
      <v-col cols="3" class="col">
        <BtnGroup :authority-list="data.authorityList" :btn-list="data.btnList" />
      </v-col>
      <v-col cols="9" @keyup.enter="method.sureSearch">
        <v-text-field
          v-model="data.searchForm.keyword"
          clearable hide-details density="comfortable"
          class="searchInput ml-5 mt-1"
          :label="$t('wms.deliveryManagement.packingTaskKeyword')" variant="solo"
        ></v-text-field>
      </v-col>
    </v-row>
  </div>

  <div class="mt-5" :style="{ height: cardHeight }">
    <vxe-table ref="xTable" :column-config="{ minWidth: '120px' }" :data="data.tableData" :height="tableHeight" align="center">
      <template #empty>
        <div class="emptyState">
          <v-icon icon="mdi-package-variant-closed" size="38"></v-icon>
          <div>{{ data.errorMessage || $t('wms.deliveryManagement.noPackingTask') }}</div>
        </div>
      </template>
      <vxe-column type="seq" width="60"></vxe-column>
      <vxe-column type="expand" width="60">
        <template #content="{ row }">
          <div class="productDetail">
            <v-table density="compact">
              <thead>
                <tr>
                  <th></th>
                  <th>{{ $t('wms.deliveryManagement.productInfo') }}</th>
                  <th>SKU</th>
                  <th>FNSKU / MSKU</th>
                  <th>{{ $t('wms.deliveryManagement.packingTaskQty') }}</th>
                  <th>{{ $t('wms.deliveryManagement.packedQty') }}</th>
                  <th>{{ $t('wms.deliveryManagement.availableQty') }}</th>
                </tr>
              </thead>
              <tbody>
                <tr v-for="item in row.item_list" :key="item.id">
                  <td class="detailImageCell">
                    <ProductImage :src="item.main_image || ''" :alt="item.commodity_name || item.commodity_sku || ''" :width="56" :height="56" />
                  </td>
                  <td class="detailProductCell">
                    <div class="primaryText">{{ method.displayValue(item.commodity_name) }}</div>
                    <div class="secondaryText">{{ method.displayValue(item.commodity_id) }}</div>
                  </td>
                  <td>{{ method.displayValue(item.commodity_sku || item.sku) }}</td>
                  <td>
                    <div>{{ method.displayValue(item.fn_sku) }}</div>
                    <div class="secondaryText">{{ method.displayValue(item.msku) }}</div>
                  </td>
                  <td>{{ method.displayValue(item.task_num) }}</td>
                  <td>{{ method.displayValue(item.quantity_shipped) }}</td>
                  <td>{{ method.displayValue(item.stock_available) }}</td>
                </tr>
              </tbody>
            </v-table>
          </div>
        </template>
      </vxe-column>
      <vxe-column width="96">
        <template #default="{ row }">
          <ProductImage
            :src="row.item_list[0]?.main_image || ''"
            :alt="row.item_list[0]?.commodity_name || row.packing_task_sn"
            :width="64" :height="64"
          />
        </template>
      </vxe-column>
      <vxe-column field="packing_task_sn" :title="$t('wms.deliveryManagement.packingTaskNo')" min-width="210"></vxe-column>
      <vxe-column :title="$t('wms.deliveryManagement.shopInfo')" min-width="180">
        <template #default="{ row }">
          <div class="leftCell">
            <div class="primaryText">{{ method.displayValue(row.shop_name) }}</div>
            <div class="secondaryText">{{ method.displayValue(row.marketplace_name) }}</div>
          </div>
        </template>
      </vxe-column>
      <vxe-column :title="$t('wms.deliveryManagement.packingProgress')" min-width="150">
        <template #default="{ row }">
          {{ method.displayValue(row.complete_num) }} / {{ method.displayValue(row.task_num) }}
        </template>
      </vxe-column>
      <vxe-column field="warehouse_name" :title="$t('wms.deliveryManagement.warehouseName')" min-width="150"></vxe-column>
      <vxe-column field="source_create_time" :title="$t('wms.deliveryManagement.packingCreatedTime')" min-width="170"></vxe-column>
      <vxe-column field="create_name" :title="$t('wms.deliveryManagement.creator')" min-width="140"></vxe-column>
    </vxe-table>
    <custom-pager
      :current-page="data.tablePage.pageIndex" :page-size="data.tablePage.pageSize"
      perfect :total="data.tablePage.total" :page-sizes="PAGE_SIZE" :layouts="PAGE_LAYOUT"
      @page-change="method.handlePageChange"
    ></custom-pager>
  </div>
</template>

<script lang="ts" setup>
import { computed, onMounted, reactive, ref, watch } from 'vue'
import type { VxePagerEvents } from 'vxe-table'
import { getPackingTaskPage } from '@/api/wms/packingTask'
import BtnGroup from '@/components/system/btnGroup.vue'
import ProductImage from '@/components/system/product-image.vue'
import customPager from '@/components/custom-pager.vue'
import { computedCardHeight, computedTableHeight } from '@/constant/style'
import { DEFAULT_PAGE_SIZE, PAGE_LAYOUT, PAGE_SIZE } from '@/constant/vxeTable'
import { DEBOUNCE_TIME } from '@/constant/system'
import i18n from '@/languages/i18n'
import type { PackingTaskVO } from '@/types/DeliveryManagement/PackingTask'
import type { btnGroupItem, SearchObject } from '@/types/System/Form'
import { getMenuAuthorityList, setSearchObject } from '@/utils/common'
import { exportData } from '@/utils/exportTable'

const xTable = ref()
const data = reactive({
  searchForm: { keyword: '' },
  tableData: ref<PackingTaskVO[]>([]),
  errorMessage: '',
  tablePage: reactive({
    total: 0,
    pageIndex: 1,
    pageSize: DEFAULT_PAGE_SIZE,
    searchObjects: ref<SearchObject[]>([])
  }),
  timer: ref<ReturnType<typeof setTimeout> | null>(null),
  btnList: [] as btnGroupItem[],
  authorityList: getMenuAuthorityList()
})

const method = reactive({
  displayValue: (value: unknown): string | number => value === null || value === undefined ? '' : value as string | number,
  getPage: async () => {
    const { data: res } = await getPackingTaskPage(data.tablePage)
    if (!res.isSuccess) {
      data.tableData = []
      data.tablePage.total = 0
      data.errorMessage = res.errorMessage
      return
    }
    data.errorMessage = ''
    data.tableData = res.data.rows
    data.tablePage.total = res.data.totals
  },
  refresh: () => method.getPage(),
  handlePageChange: ref<VxePagerEvents.PageChange>(({ currentPage, pageSize }) => {
    data.tablePage.pageIndex = currentPage
    data.tablePage.pageSize = pageSize
    method.getPage()
  }),
  sureSearch: () => {
    data.tablePage.pageIndex = 1
    data.tablePage.searchObjects = setSearchObject(data.searchForm)
    method.getPage()
  },
  exportTable: () => {
    exportData({
      table: xTable.value,
      filename: i18n.global.t('wms.deliveryManagement.packingTask'),
      columnFilterMethod({ column }: any) {
        return column?.type !== 'expand'
      }
    })
  }
})

onMounted(() => {
  data.btnList = [
    { name: i18n.global.t('system.page.refresh'), icon: 'mdi-refresh', code: '', click: method.refresh },
    { name: i18n.global.t('system.page.export'), icon: 'mdi-export-variant', code: 'invoice-export', click: method.exportTable }
  ]
  method.getPage()
})

const cardHeight = computed(() => computedCardHeight({ hasTab: false, hasOperateBtn: false }))
const tableHeight = computed(() => computedTableHeight({ hasTab: false, hasOperateBtn: false }))

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

defineExpose({ getPackingTask: method.getPage })
</script>

<style lang="less" scoped>
.flowTip {
  display: flex;
  gap: 8px;
  align-items: center;
  margin-bottom: 12px;
  padding: 10px 14px;
  border-radius: 6px;
  background: rgba(var(--v-theme-primary), 0.08);
}

.leftCell,
.detailProductCell {
  text-align: left;
}

.primaryText {
  color: rgba(var(--v-theme-on-surface), 0.87);
  font-weight: 500;
}

.secondaryText {
  margin-top: 4px;
  color: rgba(var(--v-theme-on-surface), 0.6);
  font-size: 12px;
}

.productDetail {
  padding: 12px 72px;
}

.detailImageCell {
  width: 76px;
  padding: 6px 10px !important;
}

.emptyState {
  display: flex;
  flex-direction: column;
  gap: 8px;
  align-items: center;
  padding: 28px;
  color: rgba(var(--v-theme-on-surface), 0.55);
}
</style>
