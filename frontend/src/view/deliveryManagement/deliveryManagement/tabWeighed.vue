<template>
  <div class="operateArea">
    <v-row no-gutters>
      <v-col cols="3" class="col"><BtnGroup :authority-list="data.authorityList" :btn-list="data.btnList" /></v-col>
      <v-col cols="9">
        <v-row no-gutters @keyup.enter="method.sureSearch">
          <v-col cols="4">
            <v-text-field v-model="data.searchForm.dispatch_no" clearable hide-details density="comfortable" class="searchInput ml-5 mt-1"
              :label="$t('wms.deliveryManagement.dispatch_no')" variant="solo" />
          </v-col>
          <v-col cols="4">
            <v-text-field v-model="data.searchForm.spu_name" clearable hide-details density="comfortable" class="searchInput ml-5 mt-1"
              :label="$t('wms.deliveryManagement.spu_name')" variant="solo" />
          </v-col>
        </v-row>
      </v-col>
    </v-row>
  </div>

  <div class="mt-5" :style="{ height: cardHeight }">
    <vxe-table ref="xTable" :column-config="{ minWidth: '100px' }" :data="data.tableData" :height="tableHeight" align="center"
      @toggle-row-expand="handleToggleRowExpand">
      <template #empty>{{ i18n.global.t('system.page.noData') }}</template>
      <vxe-column type="seq" width="60" />
      <vxe-column type="expand" width="54">
        <template #content="{ row }">
          <div class="box-detail">
            <div v-if="row.boxes_loading" class="box-detail-loading">
              <v-progress-circular indeterminate color="primary" size="24" />
            </div>
            <v-table v-else density="compact">
              <thead>
                <tr>
                  <th>{{ $t('wms.deliveryManagement.boxNo') }}</th>
                  <th>{{ $t('wms.deliveryManagement.weighingWeightKg') }}</th>
                  <th>{{ $t('wms.deliveryManagement.weighingLength') }}</th>
                  <th>{{ $t('wms.deliveryManagement.weighingWidth') }}</th>
                  <th>{{ $t('wms.deliveryManagement.weighingHeight') }}</th>
                  <th>{{ $t('wms.deliveryManagement.volumeCm3') }}</th>
                </tr>
              </thead>
              <tbody>
                <tr v-for="box in row.box_list" :key="box.erp_box_id">
                  <td class="box-no-cell">
                    <div>{{ box.box_no || '-' }}</div>
                  </td>
                  <td>{{ formatMeasurement(box.weighing_weight, 'kg') }}</td>
                  <td>{{ formatMeasurement(box.weighing_length, 'cm') }}</td>
                  <td>{{ formatMeasurement(box.weighing_width, 'cm') }}</td>
                  <td>{{ formatMeasurement(box.weighing_height, 'cm') }}</td>
                  <td>{{ formatMeasurement(boxVolume(box), 'cm³') }}</td>
                </tr>
                <tr v-if="row.box_list.length === 0">
                  <td colspan="6" class="box-detail-empty">{{ $t('system.page.noData') }}</td>
                </tr>
              </tbody>
            </v-table>
          </div>
        </template>
      </vxe-column>
      <vxe-column :title="$t('wms.deliveryManagement.state')" width="100">
        <template #default="{ row }">
          <v-chip size="small" :color="row.is_todo ? 'warning' : 'success'" variant="tonal">
            {{ row.is_todo ? $t('wms.deliveryManagement.weighTodo') : $t('wms.deliveryManagement.weighReady') }}
          </v-chip>
        </template>
      </vxe-column>
      <vxe-column field="main_image" :title="$t('wms.deliveryManagement.productImage')" width="92">
        <template #default="{ row }"><ProductImage :src="row.main_image" :alt="row.commodity_name" :width="56" :height="56" /></template>
      </vxe-column>
      <vxe-column field="commodity_name" :title="$t('wms.deliveryManagement.productInfo')" min-width="300" align="left" header-align="left">
        <template #default="{ row }">
          <div class="product-info-cell">
            <div class="primary-text">{{ row.commodity_name || '-' }}/{{ row.fba_no || '-' }}</div>
            <div class="secondary-text">{{ $t('wms.deliveryManagement.fnSku') }}：{{ row.fba_sku || '-' }}</div>
            <div class="secondary-text">{{ row.shop_name || '-' }}</div>
          </div>
        </template>
      </vxe-column>
      <vxe-column field="dept_name" :title="$t('wms.deliveryManagement.shippingPersonnel')" min-width="180">
        <template #default="{ row }"><div class="primary-text">{{ row.dept_name || '-' }}</div><div class="secondary-text">{{ row.order_user_name || '-' }}</div></template>
      </vxe-column>
      <vxe-column field="shipment_total_qty" :title="$t('wms.deliveryManagement.quantityVariant')" width="145">
        <template #default="{ row }">
          <div class="primary-text">{{ row.shipment_total_qty }}{{ $t('wms.deliveryManagement.pieceUnit') }}/{{ row.box_count }}{{ $t('wms.deliveryManagement.boxUnit') }}</div>
          <div class="secondary-text">{{ row.variant_qty }} {{ $t('wms.deliveryManagement.variantLabel') }}</div>
        </template>
      </vxe-column>
      <vxe-column field="weighing_weight" :title="$t('wms.deliveryManagement.weighing_weight')" width="130">
        <template #default="{ row }">
          <v-chip size="small" :color="weightStatus(row).color" variant="tonal">{{ weightStatus(row).label }}</v-chip>
        </template>
      </vxe-column>
      <vxe-column :title="$t('wms.deliveryManagement.dimensionsCm')" width="130">
        <template #default="{ row }">
          <v-chip size="small" :color="dimensionStatus(row).color" variant="tonal">{{ dimensionStatus(row).label }}</v-chip>
        </template>
      </vxe-column>
      <vxe-column field="operate" :title="$t('system.page.operate')" width="190" :resizable="false">
        <template #default="{ row }">
          <div class="row-actions">
            <v-btn size="small" color="primary" variant="tonal" :disabled="!data.authorityList.includes('weighed-weigh')" @click="method.weighRow(row)">
              {{ $t('wms.deliveryManagement.weigh') }}
            </v-btn>
            <v-btn size="small" color="warning" variant="tonal" :disabled="!data.authorityList.includes('weighed-revoke')" @click="method.backToThePreviousStep(row)">
              {{ $t('wms.deliveryManagement.returnLabel') }}
            </v-btn>
          </div>
        </template>
      </vxe-column>
    </vxe-table>
    <custom-pager :current-page="data.tablePage.pageIndex" :page-size="data.tablePage.pageSize" perfect :total="data.tablePage.total"
      :page-sizes="PAGE_SIZE" :layouts="PAGE_LAYOUT" @page-change="method.handlePageChange" />
    <ShipmentBoxWeighDialog ref="boxDialogRef" @saved="method.refresh" />
  </div>
</template>

<script lang="ts" setup>
import { computed, onMounted, reactive, ref, watch } from 'vue'
import type { VxePagerEvents, VxeTableEvents } from 'vxe-table'
import { getWeighed, getWeighingBoxes, undoWeighing } from '@/api/wms/deliveryManagement'
import { hookComponent } from '@/components/system'
import BtnGroup from '@/components/system/btnGroup.vue'
import ProductImage from '@/components/system/product-image.vue'
import customPager from '@/components/custom-pager.vue'
import { computedCardHeight, computedTableHeight } from '@/constant/style'
import { DEBOUNCE_TIME } from '@/constant/system'
import { DEFAULT_PAGE_SIZE, PAGE_LAYOUT, PAGE_SIZE } from '@/constant/vxeTable'
import i18n from '@/languages/i18n'
import type { DispatchWeighingBoxVO, DispatchWeighingShipmentVO } from '@/types/DeliveryManagement/DeliveryManagement'
import type { btnGroupItem, TablePage } from '@/types/System/Form'
import { getMenuAuthorityList, setSearchObject } from '@/utils/common'
import ShipmentBoxWeighDialog from './shipment-box-weigh-dialog.vue'

type WeighingTableRow = DispatchWeighingShipmentVO & {
  box_list: DispatchWeighingBoxVO[]
  boxes_loaded: boolean
  boxes_loading: boolean
}

type CompletionStatus = { color: 'error' | 'warning' | 'success'; label: string }

const xTable = ref()
const boxDialogRef = ref<InstanceType<typeof ShipmentBoxWeighDialog>>()
const data = reactive({
  searchForm: { dispatch_no: '', spu_name: '' },
  timer: ref<ReturnType<typeof setTimeout> | null>(null),
  tableData: [] as WeighingTableRow[],
  tablePage: { total: 0, pageIndex: 1, pageSize: DEFAULT_PAGE_SIZE, searchObjects: [] } as TablePage,
  btnList: [] as btnGroupItem[],
  authorityList: getMenuAuthorityList()
})

const resolveStatus = (completed: number, started: number, total: number, labels: [string, string, string]): CompletionStatus => {
  if (total > 0 && completed >= total) return { color: 'success', label: labels[2] }
  if (started > 0) return { color: 'warning', label: labels[1] }
  return { color: 'error', label: labels[0] }
}

const weightStatus = (row: WeighingTableRow) => resolveStatus(
  row.weighed_box_count,
  row.weighed_box_count,
  row.box_count,
  [
    i18n.global.t('wms.deliveryManagement.weighTodo'),
    i18n.global.t('wms.deliveryManagement.weighPartial'),
    i18n.global.t('wms.deliveryManagement.weighReady')
  ]
)

const dimensionStatus = (row: WeighingTableRow) => resolveStatus(
  row.dimension_measured_box_count,
  row.dimension_started_box_count,
  row.box_count,
  [
    i18n.global.t('wms.deliveryManagement.measureTodo'),
    i18n.global.t('wms.deliveryManagement.measurePartial'),
    i18n.global.t('wms.deliveryManagement.measureReady')
  ]
)

const formatMeasurement = (value: number, unit: string) => Number(value) > 0 ? `${value} ${unit}` : '-'

const boxVolume = (box: DispatchWeighingBoxVO) => {
  if (Number(box.weighing_volume) > 0) return Number(box.weighing_volume)
  const volume = Number(box.weighing_length) * Number(box.weighing_width) * Number(box.weighing_height)
  return Number.isFinite(volume) && volume > 0 ? Number(volume.toFixed(2)) : 0
}

const handleToggleRowExpand: VxeTableEvents.ToggleRowExpand<WeighingTableRow> = async ({ row, expanded }) => {
  if (!expanded || row.boxes_loaded || row.boxes_loading) return
  row.boxes_loading = true
  try {
    const { data: res } = await getWeighingBoxes(row.dispatch_no, row.fba_shipment_id)
    if (!res.isSuccess) {
      hookComponent.$message({ type: 'error', content: res.errorMessage })
      return
    }
    row.box_list = res.data
    row.boxes_loaded = true
  } finally {
    row.boxes_loading = false
  }
}

const method = reactive({
  refresh: () => method.getWeighed(),
  weighRow: (row: DispatchWeighingShipmentVO) => boxDialogRef.value?.openDialog(row),
  backToThePreviousStep: (row: DispatchWeighingShipmentVO) => {
    hookComponent.$dialog({
      content: `${i18n.global.t('wms.deliveryManagement.confirmBack')}?`,
      handleConfirm: async () => {
        const { data: res } = await undoWeighing(row.id)
        if (!res.isSuccess) { hookComponent.$message({ type: 'error', content: res.errorMessage }); return }
        hookComponent.$message({ type: 'success', content: res.data })
        method.refresh()
      }
    })
  },
  getWeighed: async () => {
    const { data: res } = await getWeighed(data.tablePage)
    if (!res.isSuccess) { hookComponent.$message({ type: 'error', content: res.errorMessage }); return }
    data.tableData = res.data.rows.map((row: DispatchWeighingShipmentVO) => ({
      ...row,
      box_list: [],
      boxes_loaded: false,
      boxes_loading: false
    }))
    data.tablePage.total = res.data.totals
  },
  handlePageChange: ref<VxePagerEvents.PageChange>(({ currentPage, pageSize }) => {
    data.tablePage.pageIndex = currentPage
    data.tablePage.pageSize = pageSize
    method.getWeighed()
  }),
  sureSearch: () => { data.tablePage.searchObjects = setSearchObject(data.searchForm); data.tablePage.pageIndex = 1; method.getWeighed() }
})

onMounted(() => {
  data.btnList = [{ name: i18n.global.t('system.page.refresh'), icon: 'mdi-refresh', code: '', click: method.refresh }]
})

const cardHeight = computed(() => computedCardHeight({}))
const tableHeight = computed(() => computedTableHeight({}))
watch(() => data.searchForm, () => {
  if (data.timer) clearTimeout(data.timer)
  data.timer = setTimeout(() => { data.timer = null; method.sureSearch() }, DEBOUNCE_TIME)
}, { deep: true })

defineExpose({ getWeighed: method.getWeighed })
</script>

<style lang="less" scoped>
.operateArea { width: 100%; min-width: 760px; display: flex; align-items: center; border-radius: 10px; padding: 0 10px; }
.col { display: flex; align-items: center; }
.product-info-cell { line-height: 22px; }
.primary-text { font-weight: 600; color: rgba(var(--v-theme-on-surface), 0.9); }
.secondary-text { margin-top: 2px; color: rgba(var(--v-theme-on-surface), 0.62); }
.row-actions { display: flex; justify-content: center; gap: 8px; }
.box-detail { padding: 14px 72px; background: rgba(var(--v-theme-surface-variant), 0.18); }
.box-detail-loading { min-height: 88px; display: flex; align-items: center; justify-content: center; }
.box-detail th { white-space: nowrap; font-weight: 600; }
.box-no-cell { text-align: left; font-weight: 600; }
.box-detail-empty { padding: 24px !important; text-align: center !important; opacity: 0.62; }
</style>
