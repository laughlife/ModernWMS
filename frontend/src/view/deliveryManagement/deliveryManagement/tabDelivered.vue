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
      <vxe-column type="checkbox" width="50" fixed="left" />
      <vxe-column :title="$t('wms.deliveryManagement.state')" width="250" align="left" header-align="left">
        <template #default="{ row }">
          <div class="outbound-status-cell">
            <div :class="row.volume_divisor ? 'status-ready' : 'status-missing'">
              {{ row.volume_divisor ? `材积比：${row.volume_divisor}` : '未指定材积比' }}
            </div>
            <div :class="row.carrier_unit ? 'status-ready' : 'status-missing'">
              {{ row.carrier_unit ? `承运单位：${row.carrier_unit}` : '未指定承运单位' }}
            </div>
          </div>
        </template>
      </vxe-column>
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
      <vxe-column field="weight" title="重量(kg)" width="120">
        <template #default="{ row }">{{ formatMeasurement(row.weight, 'kg') }}</template>
      </vxe-column>
      <vxe-column field="volume" title="体积(cm³)" width="130">
        <template #default="{ row }">{{ formatMeasurement(row.volume, 'cm³') }}</template>
      </vxe-column>
      <vxe-column field="weighing_weight" :title="$t('wms.deliveryManagement.weighing_weight')" width="130">
        <template #default="{ row }">{{ formatMeasurement(row.weighing_weight, 'kg') }}</template>
      </vxe-column>
      <vxe-column field="creator" :title="$t('wms.deliveryManagement.creator')" width="140" />
      <vxe-column field="operate" :title="$t('system.page.operate')" width="150" fixed="right" :resizable="false">
        <template #default="{ row }">
          <div class="row-actions">
            <TooltipBtn :flat="true" icon="mdi-calculator-variant" tooltip-text="设置材积比"
              :disabled="!row.fba_shipment_id || !data.authorityList.includes('delivered-setCarrier')"
              @click="volumeDivisorDialogRef?.openDialog(row)" />
            <TooltipBtn :flat="true" icon="mdi-warehouse" tooltip-text="设置承运单位"
              :disabled="!data.authorityList.includes('delivered-setCarrier')"
              @click="carrierDialogRef?.openDialog(row)" />
            <TooltipBtn :flat="true" icon="mdi-send-outline" tooltip-text="出库"
              :disabled="!data.authorityList.includes('delivered-delivery')" @click="method.deliverRow(row)" />
          </div>
        </template>
      </vxe-column>
    </vxe-table>
    <custom-pager :current-page="data.tablePage.pageIndex" :page-size="data.tablePage.pageSize" perfect
      :total="data.tablePage.total" :page-sizes="PAGE_SIZE" :layouts="PAGE_LAYOUT" @page-change="method.handlePageChange" />
    <ToBeFreightfee :show-dialog="data.showSetFreight" @close="method.freightfeeClose" @submit="method.freightfeeSubmit" />
    <OutboundVolumeDivisorDialog ref="volumeDivisorDialogRef" @saved="method.refresh" />
    <OutboundCarrierDialog ref="carrierDialogRef" @saved="method.refresh" />
  </div>
</template>

<script lang="ts" setup>
import { computed, onMounted, reactive, ref, watch } from 'vue'
import type { VxePagerEvents } from 'vxe-table'
import { getToBeDelivery, handleDelivery, setCarrier } from '@/api/wms/deliveryManagement'
import { hookComponent } from '@/components/system'
import BtnGroup from '@/components/system/btnGroup.vue'
import ProductImage from '@/components/system/product-image.vue'
import TooltipBtn from '@/components/tooltip-btn.vue'
import customPager from '@/components/custom-pager.vue'
import { computedCardHeight, computedTableHeight } from '@/constant/style'
import { DEBOUNCE_TIME } from '@/constant/system'
import { DEFAULT_PAGE_SIZE, PAGE_LAYOUT, PAGE_SIZE } from '@/constant/vxeTable'
import i18n from '@/languages/i18n'
import type { DeliveryManagementDetailVO, SetCarrierVO } from '@/types/DeliveryManagement/DeliveryManagement'
import type { btnGroupItem, TablePage } from '@/types/System/Form'
import { getMenuAuthorityList, setSearchObject } from '@/utils/common'
import { buildDeliveryPayload, buildSingleDeliveryPayload } from '@/utils/outboundFlow'
import { exportData } from '@/utils/exportTable'
import ToBeFreightfee from './to-be-freightfee.vue'
import OutboundVolumeDivisorDialog from './outbound-volume-divisor-dialog.vue'
import OutboundCarrierDialog from './outbound-carrier-dialog.vue'

const xTable = ref()
const volumeDivisorDialogRef = ref<InstanceType<typeof OutboundVolumeDivisorDialog>>()
const carrierDialogRef = ref<InstanceType<typeof OutboundCarrierDialog>>()
const data = reactive({
  searchForm: { dispatch_no: '', spu_name: '' },
  showSetFreight: false,
  timer: ref<ReturnType<typeof setTimeout> | null>(null),
  tableData: [] as DeliveryManagementDetailVO[],
  tablePage: { total: 0, pageIndex: 1, pageSize: DEFAULT_PAGE_SIZE, searchObjects: [] } as TablePage,
  btnList: [] as btnGroupItem[],
  authorityList: getMenuAuthorityList()
})

const formatMeasurement = (value: number | undefined, unit: string) => Number(value) > 0 ? `${value} ${unit}` : '-'

const method = reactive({
  refresh: () => method.getDelivery(),
  getDelivery: async () => {
    const { data: res } = await getToBeDelivery(data.tablePage)
    if (!res.isSuccess) { hookComponent.$message({ type: 'error', content: res.errorMessage }); return }
    data.tableData = res.data.rows
    data.tablePage.total = res.data.totals
  },
  deliverRow: (row: DeliveryManagementDetailVO) => {
    hookComponent.$dialog({
      content: `${i18n.global.t('wms.deliveryManagement.irreversible')}, ${i18n.global.t('wms.deliveryManagement.confirmDelivery')}?`,
      handleConfirm: async () => {
        const { data: res } = await handleDelivery(buildSingleDeliveryPayload(row))
        if (!res.isSuccess) { hookComponent.$message({ type: 'error', content: res.errorMessage }); return }
        hookComponent.$message({ type: 'success', content: res.data })
        method.refresh()
      }
    })
  },
  deliverSelected: () => {
    const selectedRows = xTable.value?.getCheckboxRecords() as DeliveryManagementDetailVO[] | undefined
    if (!selectedRows?.length) {
      hookComponent.$message({ type: 'error', content: i18n.global.t('base.userManagement.checkboxIsNull') })
      return
    }
    hookComponent.$dialog({
      content: `${i18n.global.t('wms.deliveryManagement.irreversible')}, ${i18n.global.t('wms.deliveryManagement.confirmDelivery')}?`,
      handleConfirm: async () => {
        const { data: res } = await handleDelivery(buildDeliveryPayload(selectedRows))
        if (!res.isSuccess) { hookComponent.$message({ type: 'error', content: res.errorMessage }); return }
        hookComponent.$message({ type: 'success', content: res.data })
        method.refresh()
      }
    })
  },
  setFreight: () => {
    const selectedRows = xTable.value?.getCheckboxRecords() as DeliveryManagementDetailVO[] | undefined
    if (selectedRows?.length) data.showSetFreight = true
    else hookComponent.$message({ type: 'error', content: i18n.global.t('base.userManagement.checkboxIsNull') })
  },
  freightfeeClose: () => { data.showSetFreight = false },
  freightfeeSubmit: async (form: { carrier: string; freightfee_id: number; waybill_no: string }) => {
    const selectedRows = xTable.value?.getCheckboxRecords() as DeliveryManagementDetailVO[] | undefined
    if (!selectedRows?.length) { data.showSetFreight = false; return }
    const payload: SetCarrierVO[] = selectedRows.map(row => ({
      id: row.id,
      dispatch_no: row.dispatch_no || '',
      dispatch_status: row.dispatch_status || 0,
      freightfee_id: form.freightfee_id,
      carrier: form.carrier,
      waybill_no: form.waybill_no
    }))
    const { data: res } = await setCarrier(payload)
    if (!res.isSuccess) { hookComponent.$message({ type: 'error', content: res.errorMessage }); return }
    hookComponent.$message({ type: 'success', content: res.data })
    data.showSetFreight = false
    method.refresh()
  },
  exportTable: () => {
    exportData({
      table: xTable.value,
      filename: i18n.global.t('wms.deliveryManagement.toBeDelivered'),
      columnFilterMethod({ column }: any) {
        return !['checkbox'].includes(column?.type) && !['operate'].includes(column?.field)
      }
    })
  },
  handlePageChange: ref<VxePagerEvents.PageChange>(({ currentPage, pageSize }) => {
    data.tablePage.pageIndex = currentPage
    data.tablePage.pageSize = pageSize
    method.getDelivery()
  }),
  sureSearch: () => {
    data.tablePage.searchObjects = setSearchObject(data.searchForm)
    data.tablePage.pageIndex = 1
    method.getDelivery()
  }
})

onMounted(() => {
  data.btnList = [
    { name: i18n.global.t('system.page.refresh'), icon: 'mdi-refresh', code: '', click: method.refresh },
    { name: i18n.global.t('system.page.export'), icon: 'mdi-export-variant', code: 'delivered-export', click: method.exportTable },
    { name: i18n.global.t('wms.deliveryManagement.delivery'), icon: 'mdi-cube-send', code: 'delivered-delivery', click: method.deliverSelected },
    { name: i18n.global.t('wms.deliveryManagement.setFreight'), icon: 'mdi-car-cog', code: 'delivered-setCarrier', click: method.setFreight }
  ]
})

const cardHeight = computed(() => computedCardHeight({}))
const tableHeight = computed(() => computedTableHeight({}))
watch(() => data.searchForm, () => {
  if (data.timer) clearTimeout(data.timer)
  data.timer = setTimeout(() => { data.timer = null; method.sureSearch() }, DEBOUNCE_TIME)
}, { deep: true })

defineExpose({ getDelivery: method.getDelivery })
</script>

<style lang="less" scoped>
.operateArea { width: 100%; min-width: 760px; display: flex; align-items: center; border-radius: 10px; padding: 0 10px; }
.col { display: flex; align-items: center; }
.product-info-cell, .quantity-info-cell { line-height: 22px; }
.primary-text { font-weight: 600; color: rgba(var(--v-theme-on-surface), 0.9); }
.secondary-text { margin-top: 2px; color: rgba(var(--v-theme-on-surface), 0.62); }
.outbound-status-cell { line-height: 24px; white-space: normal; }
.status-ready { color: rgba(var(--v-theme-on-surface), 0.85); }
.status-missing { color: rgb(var(--v-theme-error)); }
.row-actions { display: flex; justify-content: center; gap: 4px; }
</style>
