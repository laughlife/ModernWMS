<template>
  <div class="operateArea">
    <v-row no-gutters>
      <v-col cols="2" class="col"><BtnGroup :authority-list="data.authorityList" :btn-list="data.btnList" /></v-col>
      <v-col cols="2" class="col">
        <v-btn color="primary" prepend-icon="mdi-truck-fast-outline"
          :disabled="data.selectedOrderCount === 0 || !data.authorityList.includes('delivered-setCarrier')"
          @click="method.openBatchCarrier">
          批量设置承运信息（{{ data.selectedOrderCount }}）
        </v-btn>
      </v-col>
      <DispatchSearchFilters
        v-model:keyword="data.keyword"
        v-model:group-id="data.group_id"
        v-model:member-id="data.member_id"
        :cols="8"
        @search="method.search"
      />
    </v-row>
  </div>

  <v-alert v-if="props.warehouseId === null" type="info" variant="tonal" class="mt-4">请先选择仓库。</v-alert>
  <div v-else class="mt-5" :style="{ height: cardHeight }">
    <vxe-table ref="xTable" :column-config="{ minWidth: '110px' }" :row-config="{ keyField: 'id' }"
      :data="data.tableData" :height="tableHeight" :row-class-name="method.rowClassName" align="center" row-id="id"
      @checkbox-change="method.handleSelectionChange" @checkbox-all="method.handleSelectionChange">
      <template #empty>{{ i18n.global.t('system.page.noData') }}</template>
      <vxe-column type="checkbox" width="52" fixed="left" />
      <vxe-column type="seq" width="60" />
      <vxe-column type="expand" width="54">
        <template #content="{ row }">
          <div class="order-detail">
            <v-progress-linear v-if="row.detail_loading" indeterminate color="primary" />
            <v-alert v-else-if="row.detail_error" type="error" variant="tonal" density="compact">{{ row.detail_error }}</v-alert>
            <div v-else-if="row.detail" class="task-list">
              <section v-for="task in row.detail.packing_tasks" :key="task.id" class="task-card">
                <div class="task-title">
                  <strong>装箱任务：{{ task.source_task_no }}</strong>
                  <span>箱数 {{ task.measured_box_count }}/{{ task.expected_box_count }}</span>
                </div>
                <v-table density="compact">
                  <thead><tr><th>图片</th><th>商品信息</th><th>FNSKU / MSKU</th><th>变体</th><th>任务量</th><th>商品需求量</th></tr></thead>
                  <tbody>
                    <tr v-for="item in task.items" :key="item.id">
                      <td><ProductImage :src="item.main_image" :alt="item.commodity_name" :width="48" :height="48" :cover="false" /></td>
                      <td><div>{{ item.commodity_name || '-' }}</div><small>SKU：{{ item.commodity_sku || '-' }}</small></td>
                      <td><div>{{ item.fn_sku || '-' }}</div><small>{{ item.msku || '-' }}</small></td>
                      <td>{{ variantQty(item) || '-' }}</td><td>{{ item.task_qty ?? '-' }}</td><td>{{ item.required_qty ?? '-' }}</td>
                    </tr>
                  </tbody>
                </v-table>
                <div v-for="box in row.boxes_by_task[task.id] || []" :key="box.id" class="box-detail-card">
                  <div class="box-detail-title">
                    <strong>第 {{ box.box_sequence }} 箱：{{ displayBoxIdentity(task, box) }}</strong>
                    <v-chip :color="measurementStatusColor(box.measurement_status)" size="x-small" variant="tonal">{{ measurementStatusText(box.measurement_status) }}</v-chip>
                  </div>
                  <v-table density="compact" class="box-measurement-table">
                    <colgroup><col class="weight-column" /><col class="size-column" /><col class="volume-column" /><col class="ratio-column" /></colgroup>
                    <thead><tr><th>重量(kg)</th><th>尺寸(cm)</th><th>容积(cm³)</th><th>容积比</th></tr></thead>
                    <tbody><tr class="box-measurement-row">
                      <td>{{ formatNumber(box.weight) }}</td>
                      <td>{{ formatBoxSize(box) }}</td>
                      <td>{{ formatCubicCentimeters(boxVolume(box)) }}</td>
                      <td class="volume-ratios">
                        <span>/5000：{{ formatVolumeRatio(box, 5000) }}</span><span>/6000：{{ formatVolumeRatio(box, 6000) }}</span>
                        <span>/7000：{{ formatVolumeRatio(box, 7000) }}</span><span>/8000：{{ formatVolumeRatio(box, 8000) }}</span>
                      </td>
                    </tr></tbody>
                  </v-table>
                  <v-table density="compact" class="box-product-table">
                    <thead><tr><th>图片</th><th>箱内商品信息</th><th>FNSKU / MSKU</th><th>变体</th><th>箱内任务量</th><th>箱内商品数量</th></tr></thead>
                    <tbody>
                      <tr v-for="boxItem in box.items" :key="`${box.id}-${boxItem.packing_task_item_id}`">
                        <td><ProductImage :src="taskItem(task, boxItem.packing_task_item_id)?.main_image" :alt="boxItem.commodity_name" :width="44" :height="44" :cover="false" /></td>
                        <td><div>{{ boxItem.commodity_name || taskItem(task, boxItem.packing_task_item_id)?.commodity_name || '-' }}</div><small>SKU：{{ boxItem.sku_code || taskItem(task, boxItem.packing_task_item_id)?.commodity_sku || '-' }}</small></td>
                        <td><div>{{ taskItem(task, boxItem.packing_task_item_id)?.fn_sku || '-' }}</div><small>{{ taskItem(task, boxItem.packing_task_item_id)?.msku || '-' }}</small></td>
                        <td>{{ variantQty(taskItem(task, boxItem.packing_task_item_id)) || '-' }}</td>
                        <td>{{ boxItem.actual_qty }}</td><td>{{ boxProductQuantity(boxItem) }}</td>
                      </tr>
                      <tr v-if="box.items.length === 0"><td colspan="6" class="empty-cell">该箱暂无商品明细</td></tr>
                    </tbody>
                  </v-table>
                </div>
                <div v-if="!(row.boxes_by_task[task.id] || []).length" class="empty-boxes">暂无箱测量明细</div>
              </section>
            </div>
          </div>
        </template>
      </vxe-column>
      <vxe-column title="单号信息" min-width="270" align="left" header-align="left">
        <template #default="{ row }">
          <div class="number-information">
            <div><span class="number-label">WMS拣货单：</span><strong>{{ row.dispatch_no }}</strong></div>
            <div v-for="taskNo in row.packing_task_nos" :key="taskNo" class="task-no"><span class="number-label">装箱任务：</span>{{ taskNo }}</div>
            <div class="secondary-text"><span class="number-label">仓库：</span>{{ warehouseName(row.warehouse_id) }}</div>
          </div>
        </template>
      </vxe-column>
      <vxe-column title="任务 / SKU / 数量" min-width="180">
        <template #default="{ row }">
          <template v-if="row.detail"><div>{{ metrics(row).taskCount }} 个任务 / {{ metrics(row).skuLineCount }} 个SKU行</div><div class="secondary-text">合计 {{ metrics(row).totalQty }} 件</div></template><span v-else>-</span>
        </template>
      </vxe-column>
      <vxe-column title="箱测量汇总" min-width="190">
        <template #default="{ row }">
          <template v-if="row.detail"><div>{{ metrics(row).measuredBoxCount }}/{{ metrics(row).expectedBoxCount }} 箱</div><div class="secondary-text">{{ formatNumber(metrics(row).totalWeight) }} kg / {{ formatVolume(metrics(row).totalVolumeCubicMeters) }}</div></template><span v-else>-</span>
        </template>
      </vxe-column>
      <vxe-column title="计划装货量" min-width="130">
        <template #default="{ row }">
          <v-chip
            v-if="row.detail"
            :color="metrics(row).loadingQtyMismatch ? 'error' : 'success'"
            :variant="metrics(row).loadingQtyMismatch ? 'flat' : 'tonal'"
            size="small"
          >{{ metrics(row).plannedLoadingQty }} 件</v-chip>
          <span v-else>-</span>
        </template>
      </vxe-column>
      <vxe-column title="实际装货量" min-width="130">
        <template #default="{ row }">
          <v-chip
            v-if="row.detail"
            :color="metrics(row).loadingQtyMismatch ? 'error' : 'success'"
            :variant="metrics(row).loadingQtyMismatch ? 'flat' : 'tonal'"
            size="small"
          >{{ metrics(row).actualLoadingQty }} 件</v-chip>
          <span v-else>-</span>
        </template>
      </vxe-column>
      <vxe-column title="承运信息" min-width="180">
        <template #default="{ row }">
          <span v-if="row.carrier_unit" class="primary-text">{{ row.carrier_unit }}</span>
          <span v-else class="carrier-missing">未设置</span>
        </template>
      </vxe-column>
      <vxe-column title="状态" min-width="170">
        <template #default="{ row }">
          <v-chip v-if="row.source_change_pending" color="error" size="small" variant="tonal">来源变更待裁决</v-chip>
          <v-chip v-else-if="row.detail && isPendingOutboundReady(row.detail)" color="success" size="small" variant="tonal">待出库</v-chip>
          <v-chip v-else color="warning" size="small" variant="tonal">明细校验中</v-chip>
        </template>
      </vxe-column>
      <vxe-column field="creator" :title="$t('wms.deliveryManagement.creator')" width="130" />
      <vxe-column field="operate" :title="$t('system.page.operate')" width="190" fixed="right" :resizable="false">
        <template #default="{ row }">
          <div class="row-actions">
            <TooltipBtn v-if="row.source_change_pending" :flat="true" icon="mdi-alert-decagram-outline" tooltip-text="处理来源变更"
              :disabled="!row.detail || !data.authorityList.includes('delivered-delivery')" @click="openDecision(row)" />
            <TooltipBtn :flat="true" icon="mdi-truck-fast-outline" tooltip-text="设置承运信息"
              :disabled="!data.authorityList.includes('delivered-setCarrier')" @click="method.openCarrier(row)" />
            <TooltipBtn :flat="true" icon="mdi-send-outline" :tooltip-text="method.outboundTooltip(row)"
              :disabled="!row.detail || !isPendingOutboundReady(row.detail) || !method.hasCarrier(row) || !data.authorityList.includes('delivered-delivery')" @click="method.confirmOutbound(row)" />
          </div>
        </template>
      </vxe-column>
    </vxe-table>
    <custom-pager :current-page="data.pageIndex" :page-size="data.pageSize" perfect :total="data.total"
      :page-sizes="PAGE_SIZE" :layouts="PAGE_LAYOUT" @page-change="method.handlePageChange" />
  </div>

  <DispatchCarrierDialog ref="carrierDialog" @saved="method.onCarrierSaved" />

  <v-dialog v-model="decisionDialog.visible" max-width="560" persistent>
    <v-card>
      <v-card-title>处理来源变更</v-card-title>
      <v-card-text>
        <v-alert type="warning" variant="tonal" class="mb-4">装箱任务来源已变化。未人工选择继续或取消前，禁止出库。</v-alert>
        <div class="snapshot-label">来源变更快照</div>
        <pre class="source-change-snapshot">{{ decisionDialog.row?.detail?.source_change_snapshot || '暂无变更快照' }}</pre>
        <v-textarea v-model="decisionDialog.reason" label="处理原因（必填）" rows="3" counter="300" />
      </v-card-text>
      <v-card-actions><v-spacer /><v-btn :disabled="decisionDialog.submitting" @click="closeDecision">返回</v-btn>
        <v-btn color="error" :loading="decisionDialog.submitting" @click="submitDecision('CANCEL')">取消发货</v-btn>
        <v-btn color="primary" :loading="decisionDialog.submitting" @click="submitDecision('CONTINUE')">继续发货</v-btn>
      </v-card-actions>
    </v-card>
  </v-dialog>
</template>

<script lang="ts" setup>
import { computed, onMounted, reactive, ref, watch } from 'vue'
import type { VxePagerEvents } from 'vxe-table'
import { confirmDispatchOutbound, decideDispatchSourceChange, getDispatchOrder, getDispatchOrderPage, getDispatchTaskBoxes } from '@/api/wms/dispatchWorkflow'
import { hookComponent } from '@/components/system'
import BtnGroup from '@/components/system/btnGroup.vue'
import ProductImage from '@/components/system/product-image.vue'
import TooltipBtn from '@/components/tooltip-btn.vue'
import DispatchSearchFilters from './dispatch-search-filters.vue'
import DispatchCarrierDialog from './dispatch-carrier-dialog.vue'
import customPager from '@/components/custom-pager.vue'
import { computedCardHeight, computedTableHeight } from '@/constant/style'
import { DEFAULT_PAGE_SIZE, PAGE_LAYOUT, PAGE_SIZE } from '@/constant/vxeTable'
import i18n from '@/languages/i18n'
import { useDispatchWarehouseStore } from '@/store/module/dispatchWarehouse'
import type { DispatchOrderDetail, DispatchOrderSummary, DispatchPackingTask, DispatchPackingTaskItem, PackingPlanBoxItem, WeighingBox } from '@/types/DeliveryManagement/DispatchWorkflow'
import type { btnGroupItem } from '@/types/System/Form'
import { getMenuAuthorityList } from '@/utils/common'
import { beginPendingOutboundLoad, buildConfirmOutboundCommand, buildPendingOutboundPageRequest, buildSourceDecisionCommand, createLatestRequestGuard, getPendingOutboundMetrics, isPendingOutboundReady, shouldOpenCompleted } from './pendingOutboundPolicy'

interface PendingOutboundRow extends DispatchOrderSummary {
  carrier_warehouse_id: number | null
  carrier_unit: string
  detail: DispatchOrderDetail | null
  boxes_by_task: Record<number, WeighingBoxWithItems[]>
  detail_loading: boolean
  detail_error: string
}

interface WeighingBoxWithItems extends WeighingBox {
  items: PackingPlanBoxItem[]
}

const props = defineProps<{ warehouseId: number | null }>()
const emit = defineEmits<{ goToCompleted: []; statusChanged: [] }>()
const xTable = ref()
const carrierDialog = ref<InstanceType<typeof DispatchCarrierDialog>>()
const dispatchWarehouseStore = useDispatchWarehouseStore()
const requestGuard = createLatestRequestGuard()
const data = reactive({ keyword: '', group_id: null as number | null, member_id: null as number | null,
  tableData: [] as PendingOutboundRow[], total: 0, pageIndex: 1, pageSize: DEFAULT_PAGE_SIZE,
  selectedOrderCount: 0, btnList: [] as btnGroupItem[], authorityList: getMenuAuthorityList() })
const decisionDialog = reactive({ visible: false, submitting: false, reason: '', row: null as PendingOutboundRow | null })

const requestId = (): string => globalThis.crypto?.randomUUID?.() ?? `dispatch-${Date.now()}-${Math.random().toString(16).slice(2)}`
const warehouseName = (warehouseId: number): string =>
  dispatchWarehouseStore.warehouseOptions.find(warehouse => warehouse.id === warehouseId)?.name ?? '-'
const formatNumber = (value: number | null | undefined): string => Number(value) > 0 ? String(Number(value)) : '-'
const formatVolume = (value: number): string => value > 0 ? `${value.toFixed(3)} m³` : '-'
const variantQty = (item: DispatchPackingTaskItem | undefined): number => {
  const taskQty = Number(item?.task_qty)
  const requiredQty = Number(item?.required_qty)
  return taskQty > 0 && requiredQty > 0 ? requiredQty / taskQty : 0
}
const taskItem = (task: DispatchPackingTask, itemId: number | null): DispatchPackingTaskItem | undefined => task.items.find((item) => item.id === itemId)
const boxProductQuantity = (boxItem: PackingPlanBoxItem): number => Number(boxItem.actual_qty)
const boxVolume = (box: WeighingBox): number => {
  const length = Number(box.length); const width = Number(box.width); const height = Number(box.height)
  return length > 0 && width > 0 && height > 0 ? length * width * height : 0
}
const formatBoxSize = (box: WeighingBox): string => {
  const length = Number(box.length); const width = Number(box.width); const height = Number(box.height)
  return length > 0 && width > 0 && height > 0 ? `${formatNumber(length)} × ${formatNumber(width)} × ${formatNumber(height)}` : '-'
}
const displayBoxIdentity = (task: DispatchPackingTask, box: WeighingBox): string => `${task.source_task_no}-箱${box.box_sequence}`
const formatCubicCentimeters = (value: number): string => value > 0 ? value.toLocaleString('zh-CN', { maximumFractionDigits: 2 }) : '-'
const formatVolumeRatio = (box: WeighingBox, divisor: number): string => {
  const volume = boxVolume(box)
  return volume > 0 ? (volume / divisor).toFixed(2) : '-'
}
const measurementStatusTexts: Record<string, string> = { MEASURED: '已测量', UNMEASURED: '未测量', PENDING: '待测量' }
const measurementStatusText = (status: string): string => measurementStatusTexts[status] ?? '未知状态'
const measurementStatusColor = (status: string): string => status === 'MEASURED' ? 'success' : 'warning'
const metrics = (row: PendingOutboundRow) => getPendingOutboundMetrics(row.detail!, row.boxes_by_task)

const loadRowDetail = async (row: PendingOutboundRow, sequence: number): Promise<void> => {
  row.detail_loading = true
  row.detail_error = ''
  try {
    const detailResult = await getDispatchOrder(row.id)
    if (!detailResult.isSuccess) throw new Error(detailResult.errorMessage)
    const detail = detailResult.data
    const boxEntries = await Promise.all(detail.packing_tasks.map(async task => {
      const boxResult = await getDispatchTaskBoxes(detail.id, task.id)
      if (!boxResult.isSuccess) throw new Error(boxResult.errorMessage)
      const boxes: WeighingBoxWithItems[] = boxResult.data.map((box) => ({ ...box, items: (box as WeighingBoxWithItems).items ?? [] }))
      return [task.id, boxes] as const
    }))
    if (!requestGuard.isCurrent(sequence)) return
    row.detail = detail
    row.boxes_by_task = Object.fromEntries(boxEntries)
    row.source_change_pending = detail.source_change_pending
    row.row_version = detail.row_version
  } catch (error) {
    if (requestGuard.isCurrent(sequence)) row.detail_error = error instanceof Error ? error.message : '订单明细加载失败'
  } finally {
    if (requestGuard.isCurrent(sequence)) row.detail_loading = false
  }
}

const closeDecision = (): void => {
  if (decisionDialog.submitting) return
  decisionDialog.visible = false
  decisionDialog.reason = ''
  decisionDialog.row = null
}
const resetDecision = (): void => {
  decisionDialog.visible = false
  decisionDialog.submitting = false
  decisionDialog.reason = ''
  decisionDialog.row = null
}
const openDecision = (row: PendingOutboundRow): void => { decisionDialog.row = row; decisionDialog.reason = ''; decisionDialog.visible = true }
const submitDecision = async (decision: 'CONTINUE' | 'CANCEL'): Promise<void> => {
  const row = decisionDialog.row
  if (!row?.detail) return
  if (!decisionDialog.reason.trim()) { hookComponent.$message({ type: 'error', content: '处理原因不能为空' }); return }
  decisionDialog.submitting = true
  try {
    const result = await decideDispatchSourceChange(row.id, buildSourceDecisionCommand(row.detail, decision, decisionDialog.reason, requestId()))
    if (!result.isSuccess) { hookComponent.$message({ type: 'error', content: result.errorMessage }); return }
    hookComponent.$message({ type: 'success', content: decision === 'CONTINUE' ? '已确认继续发货' : '已取消发货' })
    decisionDialog.submitting = false
    closeDecision()
    await method.getDelivery()
    emit('statusChanged')
  } finally { decisionDialog.submitting = false }
}

const method = reactive({
  getDelivery: async (): Promise<void> => {
    resetDecision()
    const sequence = beginPendingOutboundLoad(data, requestGuard)
    if (props.warehouseId === null) return
    try {
      const request = buildPendingOutboundPageRequest(props.warehouseId, data.keyword, data.pageIndex, data.pageSize)
      request.group_id = data.group_id
      request.member_id = data.member_id
      const result = await getDispatchOrderPage(request)
      if (!requestGuard.isCurrent(sequence)) return
      if (!result.isSuccess) { hookComponent.$message({ type: 'error', content: result.errorMessage }); return }
      const rows: PendingOutboundRow[] = result.data.rows.map(row => {
        const carrierRow = row as DispatchOrderSummary & { carrier_warehouse_id?: number | null; carrier_unit?: string }
        return { ...row, carrier_warehouse_id: carrierRow.carrier_warehouse_id ?? null, carrier_unit: carrierRow.carrier_unit ?? '', detail: null, boxes_by_task: {}, detail_loading: false, detail_error: '' }
      })
      data.tableData = rows
      data.total = result.data.totals
      data.selectedOrderCount = 0
      await Promise.all(data.tableData.map(row => loadRowDetail(row, sequence)))
    } catch (error) {
      if (requestGuard.isCurrent(sequence)) {
        hookComponent.$message({ type: 'error', content: error instanceof Error ? error.message : '待出库列表加载失败' })
      }
    }
  },
  refresh: (): void => { void method.getDelivery() },
  search: (): void => { data.pageIndex = 1; void method.getDelivery() },
  handlePageChange: (({ currentPage, pageSize }) => { data.pageIndex = currentPage; data.pageSize = pageSize; void method.getDelivery() }) as VxePagerEvents.PageChange,
  handleSelectionChange: (): void => { data.selectedOrderCount = (xTable.value?.getCheckboxRecords?.() ?? []).length },
  hasCarrier: (row: PendingOutboundRow): boolean => Boolean(row.carrier_warehouse_id && row.carrier_unit.trim()),
  rowClassName: ({ row }: { row: PendingOutboundRow }): string =>
    !row.source_change_pending && row.detail && isPendingOutboundReady(row.detail) && method.hasCarrier(row)
      ? 'outbound-ready-row'
      : '',
  outboundTooltip: (row: PendingOutboundRow): string => method.hasCarrier(row) ? '整单确认出库' : '请先设置承运信息',
  openCarrier: (row: PendingOutboundRow): void => {
    void carrierDialog.value?.openDialog([row.id], row.carrier_warehouse_id)
  },
  openBatchCarrier: (): void => {
    const rows = (xTable.value?.getCheckboxRecords?.() ?? []) as PendingOutboundRow[]
    if (rows.length === 0) { hookComponent.$message({ type: 'error', content: '请先选择待出库拣货单' }); return }
    void carrierDialog.value?.openDialog(rows.map((row) => row.id))
  },
  onCarrierSaved: (): void => { xTable.value?.clearCheckboxRow?.(); data.selectedOrderCount = 0; void method.getDelivery() },
  confirmOutbound: (row: PendingOutboundRow): void => {
    if (!row.detail || !isPendingOutboundReady(row.detail)) return
    if (!method.hasCarrier(row)) { hookComponent.$message({ type: 'error', content: '请先设置承运信息再确认出库' }); return }
    hookComponent.$dialog({
      content: `确认将 WMS 拣货单 ${row.dispatch_no} 整单出库吗？该操作不可撤销。`,
      handleConfirm: async () => {
        const result = await confirmDispatchOutbound(row.id, buildConfirmOutboundCommand(row.detail!, requestId()))
        if (!result.isSuccess) { hookComponent.$message({ type: 'error', content: result.errorMessage }); await method.getDelivery(); return }
        hookComponent.$message({ type: 'success', content: '整单出库成功' })
        emit('statusChanged')
        if (shouldOpenCompleted(true, result.data.status)) emit('goToCompleted')
      }
    })
  }
})

onMounted(() => {
  data.btnList = [{ name: i18n.global.t('system.page.refresh'), icon: 'mdi-refresh', code: '', click: method.refresh }]
  void method.getDelivery()
})
watch(() => props.warehouseId, () => { data.pageIndex = 1; void method.getDelivery() })
const cardHeight = computed(() => computedCardHeight({}))
const tableHeight = computed(() => computedTableHeight({}))
defineExpose({ getDelivery: method.getDelivery })
</script>

<style lang="less" scoped>
.operateArea { width: 100%; min-width: 760px; display: flex; align-items: center; border-radius: 10px; padding: 0 10px; }
.col { display: flex; align-items: center; }
.primary-text { font-weight: 600; color: rgba(var(--v-theme-on-surface), 0.9); }
.secondary-text { margin-top: 2px; color: rgba(var(--v-theme-on-surface), 0.62); }
.carrier-missing { color: rgb(var(--v-theme-error)); font-weight: 600; }
.task-no { line-height: 22px; }
.number-information { display: grid; gap: 3px; line-height: 20px; }
.number-label { color: rgba(var(--v-theme-on-surface), 0.62); font-weight: 400; }
.row-actions { display: flex; align-items: center; justify-content: center; gap: 10px; }
:deep(.outbound-ready-row),
:deep(.outbound-ready-row > td),
:deep(.vxe-table--fixed-left-wrapper .outbound-ready-row),
:deep(.vxe-table--fixed-left-wrapper .outbound-ready-row > td),
:deep(.vxe-table--fixed-right-wrapper .outbound-ready-row),
:deep(.vxe-table--fixed-right-wrapper .outbound-ready-row > td) { background-color: #e8f5e9 !important; }
.order-detail { padding: 16px 68px; background: #fff; }
.task-list { display: grid; gap: 14px; }
.task-card { padding: 12px; border: 1px solid rgba(var(--v-border-color), var(--v-border-opacity)); border-radius: 8px; background: #fff; }
.task-title { display: flex; align-items: center; justify-content: space-between; padding: 0 8px 8px; font-weight: 700; }
.task-card :deep(th) { font-weight: 700; }
.box-detail-card { margin-top: 12px; padding: 10px; border: 2px solid rgba(var(--v-theme-success), 0.6); border-radius: 7px; background: #fff; }
.box-detail-title { display: flex; align-items: center; justify-content: space-between; padding: 0 6px 8px; font-weight: 700; }
.box-measurement-table, .box-product-table { border-top: 1px solid rgba(var(--v-border-color), var(--v-border-opacity)); }
.box-measurement-table :deep(.v-table__wrapper) { overflow: visible; }
.box-measurement-table :deep(table) { width: 100%; table-layout: fixed; }
.box-measurement-table :deep(.weight-column) { width: 14%; }
.box-measurement-table :deep(.size-column) { width: 22%; }
.box-measurement-table :deep(.volume-column) { width: 18%; }
.box-measurement-table :deep(.ratio-column) { width: 46%; }
.box-measurement-table :deep(th) { padding-top: 10px; padding-bottom: 10px; }
.box-measurement-table :deep(.box-measurement-row td) { padding-top: 12px; padding-bottom: 12px; vertical-align: middle; }
.box-product-table { margin-top: 8px; }
.volume-ratios { display: flex; align-items: center; gap: 18px; white-space: nowrap; }
.empty-boxes { padding: 20px; text-align: center; opacity: 0.62; }
.empty-cell { text-align: center !important; opacity: 0.62; }
.snapshot-label { margin-bottom: 6px; font-weight: 600; }
.source-change-snapshot { max-height: 220px; margin: 0 0 16px; padding: 12px; overflow: auto; white-space: pre-wrap; overflow-wrap: anywhere; border-radius: 6px; background: rgba(var(--v-theme-surface-variant), 0.4); font: inherit; }
</style>
