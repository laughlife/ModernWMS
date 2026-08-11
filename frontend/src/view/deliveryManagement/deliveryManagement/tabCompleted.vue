<template>
  <div class="operateArea">
    <v-row no-gutters>
      <v-col cols="3" class="col">
        <BtnGroup :authority-list="data.authorityList" :btn-list="data.btnList" />
      </v-col>
      <v-col cols="9">
        <v-row no-gutters @keyup.enter="method.sureSearch">
          <v-col cols="4">
            <v-text-field v-model="data.searchForm.dispatch_no" clearable hide-details density="comfortable"
              class="searchInput ml-5 mt-1" :label="$t('wms.deliveryManagement.dispatch_no')" variant="solo" />
          </v-col>
          <v-col cols="4">
            <v-text-field v-model="data.searchForm.spu_name" clearable hide-details density="comfortable"
              class="searchInput ml-5 mt-1" :label="$t('wms.deliveryManagement.spu_name')" variant="solo" />
          </v-col>
        </v-row>
      </v-col>
    </v-row>
  </div>

  <div class="mt-5" :style="{ height: cardHeight }">
    <vxe-table ref="xTable" :column-config="{ minWidth: '100px' }" :data="data.tableData" :height="tableHeight" align="center">
      <template #empty>{{ i18n.global.t('system.page.noData') }}</template>
      <vxe-column type="seq" width="60" />
      <vxe-column field="main_image" :title="$t('wms.deliveryManagement.productImage')" width="92">
        <template #default="{ row }">
          <ProductImage :src="row.main_image" :alt="row.commodity_name || row.spu_name" :width="56" :height="56" />
        </template>
      </vxe-column>
      <vxe-column :title="$t('wms.deliveryManagement.productInfo')" min-width="300" align="left" header-align="left">
        <template #default="{ row }">
          <div class="product-info-cell">
            <div class="primary-text">{{ row.commodity_name || row.spu_name || '-' }}</div>
            <div class="secondary-text">SKU：{{ row.fba_sku || row.sku_code || '-' }}</div>
          </div>
        </template>
      </vxe-column>
      <vxe-column :title="$t('wms.deliveryManagement.productQuantity')" min-width="190" align="left" header-align="left">
        <template #default="{ row }">
          <div class="quantity-info-cell">
            <div class="primary-text">1种商品 / {{ row.qty || 0 }}件</div>
            <div class="secondary-text">{{ row.variant_qty || 1 }}变体 / {{ row.box_count || 0 }}箱</div>
          </div>
        </template>
      </vxe-column>
      <vxe-column title="所属信息" min-width="190" align="left" header-align="left">
        <template #default="{ row }">
          <div class="ownership-info-cell">{{ formatOwnership(row) }}</div>
        </template>
      </vxe-column>
      <vxe-column field="volume" title="体积(m³)" width="130">
        <template #default="{ row }">{{ formatCubicMeters(row.volume) }}</template>
      </vxe-column>
      <vxe-column field="weighing_weight" :title="$t('wms.deliveryManagement.weighing_weight')" width="130">
        <template #default="{ row }">{{ formatMeasurement(row.weighing_weight, 'kg') }}</template>
      </vxe-column>
      <vxe-column field="creator" :title="$t('wms.deliveryManagement.creator')" width="140" />
      <vxe-column field="operate" :title="$t('system.page.operate')" width="120" fixed="right" :resizable="false">
        <template #default="{ row }">
          <div class="row-actions">
            <TooltipBtn :flat="true" icon="mdi-eye-outline" :tooltip-text="$t('system.page.view')" @click="method.viewRow(row)" />
            <TooltipBtn :flat="true" icon="mdi-arrow-left" tooltip-text="撤回到待出库"
              :disabled="!data.authorityList.includes('delivered-delivery')" @click="method.undoRow(row)" />
          </div>
        </template>
      </vxe-column>
    </vxe-table>
    <custom-pager :current-page="data.tablePage.pageIndex" :page-size="data.tablePage.pageSize" perfect
      :total="data.tablePage.total" :page-sizes="PAGE_SIZE" :layouts="PAGE_LAYOUT" @page-change="method.handlePageChange" />
    <SearchDeliveredDetail :id="data.showDeliveredDetailID" :show-dialog="data.showDeliveredDetail" @close="method.closeDeliveredDetail" />
  </div>
</template>

<script lang="ts" setup>
import { computed, onMounted, reactive, ref, watch } from 'vue'
import type { VxePagerEvents } from 'vxe-table'
import { getDelivery, undoDelivery } from '@/api/wms/deliveryManagement'
import { hookComponent } from '@/components/system'
import BtnGroup from '@/components/system/btnGroup.vue'
import ProductImage from '@/components/system/product-image.vue'
import TooltipBtn from '@/components/tooltip-btn.vue'
import customPager from '@/components/custom-pager.vue'
import { computedCardHeight, computedTableHeight } from '@/constant/style'
import { DEBOUNCE_TIME } from '@/constant/system'
import { DEFAULT_PAGE_SIZE, PAGE_LAYOUT, PAGE_SIZE } from '@/constant/vxeTable'
import i18n from '@/languages/i18n'
import type { DeliveryManagementDetailVO } from '@/types/DeliveryManagement/DeliveryManagement'
import type { btnGroupItem, TablePage } from '@/types/System/Form'
import { getMenuAuthorityList, setSearchObject } from '@/utils/common'
import { exportData } from '@/utils/exportTable'
import SearchDeliveredDetail from './search-delivered-detail.vue'

const xTable = ref()
const emit = defineEmits<{ statusChanged: [] }>()

const data = reactive({
  showDeliveredDetailID: 0,
  showDeliveredDetail: false,
  searchForm: { dispatch_no: '', spu_name: '' },
  timer: ref<ReturnType<typeof setTimeout> | null>(null),
  tableData: [] as DeliveryManagementDetailVO[],
  tablePage: { total: 0, pageIndex: 1, pageSize: DEFAULT_PAGE_SIZE, searchObjects: [] } as TablePage,
  btnList: [] as btnGroupItem[],
  authorityList: getMenuAuthorityList()
})

const formatMeasurement = (value: number | undefined, unit: string) => Number(value) > 0 ? `${value} ${unit}` : '-'
const formatCubicMeters = (value: number | undefined) => Number(value) > 0 ? `${Number(value).toFixed(2)} m³` : '-'
const formatOwnership = (row: DeliveryManagementDetailVO) =>
  [row.dept_name, row.order_user_name].filter(Boolean).join(' | ') || '-'

const method = reactive({
  closeDeliveredDetail: () => { data.showDeliveredDetail = false },
  viewRow: (row: DeliveryManagementDetailVO) => {
    data.showDeliveredDetailID = row.id
    data.showDeliveredDetail = true
  },
  undoRow: (row: DeliveryManagementDetailVO) => {
    hookComponent.$dialog({
      content: '确认将该数据撤回到待出库吗？库存将同步恢复为锁定状态。',
      handleConfirm: async () => {
        const { data: res } = await undoDelivery(row.id)
        if (!res.isSuccess) { hookComponent.$message({ type: 'error', content: res.errorMessage }); return }
        hookComponent.$message({ type: 'success', content: res.data })
        method.refresh()
        emit('statusChanged')
      }
    })
  },
  refresh: () => method.getCompleted(),
  getCompleted: async () => {
    const { data: res } = await getDelivery(data.tablePage)
    if (!res.isSuccess) { hookComponent.$message({ type: 'error', content: res.errorMessage }); return }
    data.tableData = res.data.rows
    data.tablePage.total = res.data.totals
  },
  handlePageChange: ref<VxePagerEvents.PageChange>(({ currentPage, pageSize }) => {
    data.tablePage.pageIndex = currentPage
    data.tablePage.pageSize = pageSize
    method.getCompleted()
  }),
  exportTable: () => {
    exportData({
      table: xTable.value,
      filename: i18n.global.t('wms.deliveryManagement.deliveryReady'),
      columnFilterMethod({ column }: any) { return !['operate'].includes(column?.field) }
    })
  },
  sureSearch: () => {
    data.tablePage.searchObjects = setSearchObject(data.searchForm)
    data.tablePage.pageIndex = 1
    method.getCompleted()
  }
})

onMounted(() => {
  data.btnList = [
    { name: i18n.global.t('system.page.refresh'), icon: 'mdi-refresh', code: '', click: method.refresh },
    { name: i18n.global.t('system.page.export'), icon: 'mdi-export-variant', code: 'signedIn-export', click: method.exportTable }
  ]
})

const cardHeight = computed(() => computedCardHeight({}))
const tableHeight = computed(() => computedTableHeight({}))
watch(() => data.searchForm, () => {
  if (data.timer) clearTimeout(data.timer)
  data.timer = setTimeout(() => { data.timer = null; method.sureSearch() }, DEBOUNCE_TIME)
}, { deep: true })

defineExpose({ getCompleted: method.getCompleted })
</script>

<style lang="less" scoped>
.operateArea { width: 100%; min-width: 760px; display: flex; align-items: center; border-radius: 10px; padding: 0 10px; }
.col { display: flex; align-items: center; }
.product-info-cell, .quantity-info-cell { line-height: 22px; }
.primary-text { font-weight: 600; color: rgba(var(--v-theme-on-surface), 0.9); }
.secondary-text { margin-top: 2px; color: rgba(var(--v-theme-on-surface), 0.62); }
.ownership-info-cell { color: rgba(var(--v-theme-on-surface), 0.82); }
.row-actions { display: flex; justify-content: center; gap: 8px; }
</style>
