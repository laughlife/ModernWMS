<template>
  <div class="operateArea">
    <v-row no-gutters>
      <v-col cols="4" class="col"><BtnGroup :authority-list="data.authorityList" :btn-list="data.btnList" /></v-col>
      <v-col cols="8">
        <v-text-field v-model="data.keyword" clearable hide-details density="comfortable" class="searchInput ml-5 mt-1"
          label="WMS拣货单号或装箱任务号" variant="solo" @keyup.enter="method.search" />
      </v-col>
    </v-row>
  </div>

  <v-alert v-if="props.warehouseId === null" type="info" variant="tonal" class="mt-4">请先选择仓库。</v-alert>
  <div v-else class="mt-5" :style="{ height: cardHeight }">
    <vxe-table :column-config="{ minWidth: '110px' }" :data="data.tableData" :height="tableHeight" align="center" row-id="id">
      <template #empty>{{ i18n.global.t('system.page.noData') }}</template>
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
                  <thead><tr><th>商品</th><th>SKU</th><th>FNSKU / MSKU</th><th>任务量</th></tr></thead>
                  <tbody>
                    <tr v-for="item in task.items" :key="item.id">
                      <td>{{ item.commodity_name || '-' }}</td><td>{{ item.commodity_sku || '-' }}</td>
                      <td>{{ item.fn_sku || '-' }} / {{ item.msku || '-' }}</td><td>{{ item.required_qty ?? '-' }}</td>
                    </tr>
                  </tbody>
                </v-table>
                <v-table density="compact" class="box-table">
                  <thead><tr><th>箱号</th><th>重量(kg)</th><th>长(cm)</th><th>宽(cm)</th><th>高(cm)</th><th>测量状态</th></tr></thead>
                  <tbody>
                    <tr v-for="box in row.boxes_by_task[task.id] || []" :key="box.id">
                      <td>{{ box.source_box_identity }}</td><td>{{ formatNumber(box.weight) }}</td>
                      <td>{{ formatNumber(box.length) }}</td><td>{{ formatNumber(box.width) }}</td>
                      <td>{{ formatNumber(box.height) }}</td><td>{{ box.measurement_status || '-' }}</td>
                    </tr>
                    <tr v-if="!(row.boxes_by_task[task.id] || []).length"><td colspan="6" class="empty-cell">暂无箱测量明细</td></tr>
                  </tbody>
                </v-table>
              </section>
            </div>
          </div>
        </template>
      </vxe-column>
      <vxe-column field="dispatch_no" title="WMS拣货单" min-width="170" align="left" header-align="left">
        <template #default="{ row }"><div class="primary-text">{{ row.dispatch_no }}</div><div class="secondary-text">仓库ID：{{ row.warehouse_id }}</div></template>
      </vxe-column>
      <vxe-column title="装箱任务" min-width="230" align="left" header-align="left">
        <template #default="{ row }"><div v-for="taskNo in row.packing_task_nos" :key="taskNo" class="task-no">{{ taskNo }}</div></template>
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
      <vxe-column title="承运信息" min-width="180"><template #default><span class="secondary-text">-（当前流程接口未提供）</span></template></vxe-column>
      <vxe-column title="状态" min-width="170">
        <template #default="{ row }">
          <v-chip v-if="row.source_change_pending" color="error" size="small" variant="tonal">来源变更待裁决</v-chip>
          <v-chip v-else-if="row.detail && isPendingOutboundReady(row.detail)" color="success" size="small" variant="tonal">待出库</v-chip>
          <v-chip v-else color="warning" size="small" variant="tonal">明细校验中</v-chip>
        </template>
      </vxe-column>
      <vxe-column field="creator" :title="$t('wms.deliveryManagement.creator')" width="130" />
      <vxe-column field="operate" :title="$t('system.page.operate')" width="160" fixed="right" :resizable="false">
        <template #default="{ row }">
          <div class="row-actions">
            <TooltipBtn v-if="row.source_change_pending" :flat="true" icon="mdi-alert-decagram-outline" tooltip-text="处理来源变更"
              :disabled="!row.detail || !data.authorityList.includes('delivered-delivery')" @click="openDecision(row)" />
            <TooltipBtn :flat="true" icon="mdi-send-outline" tooltip-text="整单确认出库"
              :disabled="!row.detail || !isPendingOutboundReady(row.detail) || !data.authorityList.includes('delivered-delivery')" @click="method.confirmOutbound(row)" />
          </div>
        </template>
      </vxe-column>
    </vxe-table>
    <custom-pager :current-page="data.pageIndex" :page-size="data.pageSize" perfect :total="data.total"
      :page-sizes="PAGE_SIZE" :layouts="PAGE_LAYOUT" @page-change="method.handlePageChange" />
  </div>

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
import { computed, onMounted, reactive, watch } from 'vue'
import type { VxePagerEvents } from 'vxe-table'
import { confirmDispatchOutbound, decideDispatchSourceChange, getDispatchOrder, getDispatchOrderPage, getDispatchTaskBoxes } from '@/api/wms/dispatchWorkflow'
import { hookComponent } from '@/components/system'
import BtnGroup from '@/components/system/btnGroup.vue'
import TooltipBtn from '@/components/tooltip-btn.vue'
import customPager from '@/components/custom-pager.vue'
import { computedCardHeight, computedTableHeight } from '@/constant/style'
import { DEBOUNCE_TIME } from '@/constant/system'
import { DEFAULT_PAGE_SIZE, PAGE_LAYOUT, PAGE_SIZE } from '@/constant/vxeTable'
import i18n from '@/languages/i18n'
import type { DispatchOrderDetail, DispatchOrderSummary, WeighingBox } from '@/types/DeliveryManagement/DispatchWorkflow'
import type { btnGroupItem } from '@/types/System/Form'
import { getMenuAuthorityList } from '@/utils/common'
import { beginPendingOutboundLoad, buildConfirmOutboundCommand, buildPendingOutboundPageRequest, buildSourceDecisionCommand, createLatestRequestGuard, getPendingOutboundMetrics, isPendingOutboundReady, shouldOpenCompleted } from './pendingOutboundPolicy'

interface PendingOutboundRow extends DispatchOrderSummary {
  detail: DispatchOrderDetail | null
  boxes_by_task: Record<number, WeighingBox[]>
  detail_loading: boolean
  detail_error: string
}

const props = defineProps<{ warehouseId: number | null }>()
const emit = defineEmits<{ goToCompleted: []; statusChanged: [] }>()
let searchTimer: ReturnType<typeof setTimeout> | null = null
const requestGuard = createLatestRequestGuard()
const data = reactive({ keyword: '', tableData: [] as PendingOutboundRow[], total: 0, pageIndex: 1, pageSize: DEFAULT_PAGE_SIZE,
  btnList: [] as btnGroupItem[], authorityList: getMenuAuthorityList() })
const decisionDialog = reactive({ visible: false, submitting: false, reason: '', row: null as PendingOutboundRow | null })

const requestId = (): string => globalThis.crypto?.randomUUID?.() ?? `dispatch-${Date.now()}-${Math.random().toString(16).slice(2)}`
const formatNumber = (value: number | null | undefined): string => Number(value) > 0 ? String(Number(value)) : '-'
const formatVolume = (value: number): string => value > 0 ? `${value.toFixed(3)} m³` : '-'
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
      return [task.id, boxResult.data] as const
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
      const result = await getDispatchOrderPage(buildPendingOutboundPageRequest(props.warehouseId, data.keyword, data.pageIndex, data.pageSize))
      if (!requestGuard.isCurrent(sequence)) return
      if (!result.isSuccess) { hookComponent.$message({ type: 'error', content: result.errorMessage }); return }
      const rows: PendingOutboundRow[] = result.data.rows.map(row => ({ ...row, detail: null, boxes_by_task: {}, detail_loading: false, detail_error: '' }))
      data.tableData = rows
      data.total = result.data.totals
      await Promise.all(rows.map(row => loadRowDetail(row, sequence)))
    } catch (error) {
      if (requestGuard.isCurrent(sequence)) {
        hookComponent.$message({ type: 'error', content: error instanceof Error ? error.message : '待出库列表加载失败' })
      }
    }
  },
  refresh: (): void => { void method.getDelivery() },
  search: (): void => { data.pageIndex = 1; void method.getDelivery() },
  handlePageChange: (({ currentPage, pageSize }) => { data.pageIndex = currentPage; data.pageSize = pageSize; void method.getDelivery() }) as VxePagerEvents.PageChange,
  confirmOutbound: (row: PendingOutboundRow): void => {
    if (!row.detail || !isPendingOutboundReady(row.detail)) return
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
watch(() => data.keyword, () => {
  if (searchTimer) clearTimeout(searchTimer)
  searchTimer = setTimeout(() => { searchTimer = null; method.search() }, DEBOUNCE_TIME)
})
const cardHeight = computed(() => computedCardHeight({}))
const tableHeight = computed(() => computedTableHeight({}))
defineExpose({ getDelivery: method.getDelivery })
</script>

<style lang="less" scoped>
.operateArea { width: 100%; min-width: 760px; display: flex; align-items: center; border-radius: 10px; padding: 0 10px; }
.col { display: flex; align-items: center; }
.primary-text { font-weight: 600; color: rgba(var(--v-theme-on-surface), 0.9); }
.secondary-text { margin-top: 2px; color: rgba(var(--v-theme-on-surface), 0.62); }
.task-no { line-height: 22px; }
.row-actions { display: flex; align-items: center; justify-content: center; gap: 10px; }
.order-detail { padding: 16px 68px; background: rgba(var(--v-theme-surface-variant), 0.18); }
.task-list { display: grid; gap: 14px; }
.task-card { padding: 12px; border: 1px solid rgba(var(--v-border-color), var(--v-border-opacity)); border-radius: 8px; background: rgb(var(--v-theme-surface)); }
.task-title { display: flex; align-items: center; justify-content: space-between; padding: 0 8px 8px; }
.box-table { margin-top: 10px; }
.empty-cell { text-align: center !important; opacity: 0.62; }
.snapshot-label { margin-bottom: 6px; font-weight: 600; }
.source-change-snapshot { max-height: 220px; margin: 0 0 16px; padding: 12px; overflow: auto; white-space: pre-wrap; overflow-wrap: anywhere; border-radius: 6px; background: rgba(var(--v-theme-surface-variant), 0.4); font: inherit; }
</style>
