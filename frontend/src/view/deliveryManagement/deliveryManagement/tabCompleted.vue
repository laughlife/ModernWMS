<template>
  <div class="operateArea">
    <v-row no-gutters>
      <v-col cols="3" class="col">
        <BtnGroup :authority-list="state.authorityList" :btn-list="state.btnList" />
      </v-col>
      <v-col cols="9">
        <v-text-field
          v-model="state.keyword"
          clearable
          hide-details
          density="comfortable"
          class="searchInput ml-5 mt-1"
          label="WMS拣货单号或装箱任务号"
          variant="solo"
          @keyup.enter="search"
        />
      </v-col>
    </v-row>
  </div>

  <v-alert v-if="props.warehouseId === null" type="warning" variant="tonal" class="mt-4">
    请先选择仓库后查看已出库主单。
  </v-alert>

  <div v-else class="mt-5" :style="{ height: cardHeight }">
    <vxe-table
      ref="xTable"
      :column-config="{ minWidth: '100px' }"
      :data="state.tableData"
      :height="tableHeight"
      :loading="state.loading"
      row-id="id"
      align="center"
      @toggle-row-expand="handleToggleRowExpand"
    >
      <template #empty>{{ i18n.global.t('system.page.noData') }}</template>
      <vxe-column type="seq" width="60" />
      <vxe-column type="expand" width="54">
        <template #content="{ row }">
          <div class="order-detail">
            <div v-if="row.detail_loading" class="detail-loading">
              <v-progress-circular indeterminate color="primary" size="28" />
            </div>
            <v-alert v-else-if="row.detail_error" type="error" variant="tonal">
              {{ row.detail_error }}
              <template #append><v-btn variant="text" @click="loadDetail(row, true)">重试</v-btn></template>
            </v-alert>
            <template v-else>
              <section v-for="task in row.tasks" :key="task.id" class="task-section">
                <div class="task-title">
                  <strong>装箱任务：{{ task.source_task_no }}</strong>
                  <span>箱数 {{ task.measured_box_count }}/{{ task.expected_box_count }}</span>
                </div>
                <v-table density="compact" class="detail-table">
                  <thead>
                    <tr><th>商品</th><th>SKU</th><th>FNSKU / MSKU</th><th>任务量</th></tr>
                  </thead>
                  <tbody>
                    <tr v-for="item in task.items" :key="item.id">
                      <td>{{ item.commodity_name || '-' }}</td>
                      <td>{{ item.commodity_sku || '-' }}</td>
                      <td>{{ [item.fn_sku, item.msku].filter(Boolean).join(' / ') || '-' }}</td>
                      <td>{{ item.required_qty ?? '-' }}</td>
                    </tr>
                    <tr v-if="task.items.length === 0"><td colspan="4" class="empty-cell">暂无商品</td></tr>
                  </tbody>
                </v-table>
                <v-table density="compact" class="detail-table box-table">
                  <thead>
                    <tr><th>物理箱</th><th>箱序</th><th>重量(kg)</th><th>长(cm)</th><th>宽(cm)</th><th>高(cm)</th><th>测量状态</th></tr>
                  </thead>
                  <tbody>
                    <tr v-for="box in task.boxes" :key="box.id">
                      <td>{{ box.source_box_identity }}</td>
                      <td>{{ box.box_sequence }}</td>
                      <td>{{ displayNumber(box.weight) }}</td>
                      <td>{{ displayNumber(box.length) }}</td>
                      <td>{{ displayNumber(box.width) }}</td>
                      <td>{{ displayNumber(box.height) }}</td>
                      <td><v-chip size="x-small" color="success" variant="tonal">{{ box.measurement_status }}</v-chip></td>
                    </tr>
                    <tr v-if="task.boxes.length === 0"><td colspan="7" class="empty-cell">暂无箱测量数据</td></tr>
                  </tbody>
                </v-table>
              </section>
              <div v-if="row.tasks.length === 0" class="empty-detail">暂无装箱任务明细</div>
            </template>
          </div>
        </template>
      </vxe-column>
      <vxe-column field="dispatch_no" title="WMS拣货单号" min-width="180" align="left" header-align="left" />
      <vxe-column title="装箱任务号" min-width="260" align="left" header-align="left">
        <template #default="{ row }">
          <div class="task-number-list">
            <v-chip v-for="taskNo in row.packing_task_nos" :key="taskNo" size="small" variant="tonal">{{ taskNo }}</v-chip>
          </div>
        </template>
      </vxe-column>
      <vxe-column title="状态" width="190" align="left" header-align="left">
        <template #default="{ row }">
          <v-chip size="small" color="success" variant="tonal">已出库</v-chip>
          <v-alert v-if="row.outbound_source_anomaly" type="warning" variant="tonal" density="compact" class="source-warning">
            来源已变化；保留已出库事实
            <div v-if="row.outbound_source_anomaly_snapshot" class="source-snapshot">
              {{ row.outbound_source_anomaly_snapshot }}
            </div>
          </v-alert>
        </template>
      </vxe-column>
      <vxe-column title="签收 / 通知" min-width="190" align="left" header-align="left">
        <template #default="{ row }">
          <div v-if="row.signed_at">
            <div>已签收 {{ row.signed_qty ?? '-' }}，破损 {{ row.damaged_qty ?? 0 }}</div>
            <v-chip size="x-small" :color="notificationColor(row.notification_status)" variant="tonal">
              {{ notificationLabel(row.notification_status) }}
            </v-chip>
            <div v-if="notificationCanRetry(row.notification_status)" class="notification-error">
              {{ row.notification_last_error || '下游签收通知未成功' }}
            </div>
          </div>
          <span v-else class="muted-text">待人工签收</span>
        </template>
      </vxe-column>
      <vxe-column field="creator" :title="$t('wms.deliveryManagement.creator')" width="130" />
      <vxe-column field="create_time" title="创建时间" width="175" />
      <vxe-column field="operate" :title="$t('system.page.operate')" width="180" fixed="right" :resizable="false">
        <template #default="{ row }">
          <div class="row-actions">
            <TooltipBtn
              :flat="true"
              icon="mdi-arrow-left"
              tooltip-text="整单撤回到待出库"
              :disabled="!canOperate || !canCancelOutbound(row)"
              @click="cancelRow(row)"
            />
            <TooltipBtn
              :flat="true"
              :icon="notificationCanRetry(row.notification_status) ? 'mdi-refresh' : 'mdi-check-decagram-outline'"
              :tooltip-text="notificationCanRetry(row.notification_status) ? '重试签收通知' : '整单签收'"
              :disabled="!canOperate || (Boolean(row.signed_at) && !notificationCanRetry(row.notification_status))"
              @click="openSignDialog(row)"
            />
          </div>
        </template>
      </vxe-column>
    </vxe-table>
    <custom-pager
      :current-page="state.pageIndex"
      :page-size="state.pageSize"
      perfect
      :total="state.total"
      :page-sizes="PAGE_SIZE"
      :layouts="PAGE_LAYOUT"
      @page-change="handlePageChange"
    />
  </div>

  <v-dialog v-model="signDialog.visible" max-width="520" persistent>
    <v-card>
      <v-card-title>{{ signDialog.retry ? '重试签收通知' : '整单签收' }}</v-card-title>
      <v-card-text>
        <div class="mb-3">{{ signDialog.row?.dispatch_no }}，出库数量：{{ signDialog.shippedQty }}</div>
        <v-text-field
          v-model.number="signDialog.damagedQty"
          type="number"
          min="0"
          :max="signDialog.shippedQty"
          label="破损数量"
          variant="outlined"
          density="comfortable"
          :readonly="signDialog.retry"
          :error-messages="signDialog.error ? [signDialog.error] : []"
        />
        <v-alert v-if="signDialog.retry" type="info" variant="tonal" density="compact">
          签收事实不变，仅重试下游通知。
        </v-alert>
      </v-card-text>
      <v-card-actions>
        <v-spacer />
        <v-btn :disabled="signDialog.loading" @click="closeSignDialog">取消</v-btn>
        <v-btn color="primary" :loading="signDialog.loading" @click="submitSign">确认</v-btn>
      </v-card-actions>
    </v-card>
  </v-dialog>
</template>

<script lang="ts" setup>
import { computed, onMounted, reactive, ref, watch } from 'vue'
import type { VxePagerEvents, VxeTableEvents } from 'vxe-table'
import {
  cancelDispatchOutbound,
  getDispatchOrder,
  getDispatchOrderPage,
  getDispatchTaskBoxes,
  signDispatchOrder
} from '@/api/wms/dispatchWorkflow'
import { hookComponent } from '@/components/system'
import BtnGroup from '@/components/system/btnGroup.vue'
import TooltipBtn from '@/components/tooltip-btn.vue'
import customPager from '@/components/custom-pager.vue'
import { computedCardHeight, computedTableHeight } from '@/constant/style'
import { DEBOUNCE_TIME } from '@/constant/system'
import { DEFAULT_PAGE_SIZE, PAGE_LAYOUT, PAGE_SIZE } from '@/constant/vxeTable'
import i18n from '@/languages/i18n'
import type {
  DispatchOrderDetail,
  DispatchSignNotificationStatus
} from '@/types/DeliveryManagement/DispatchWorkflow'
import type { btnGroupItem } from '@/types/System/Form'
import { getMenuAuthorityList } from '@/utils/common'
import { exportData } from '@/utils/exportTable'
import {
  buildCancelOutboundCommand,
  buildCompletedPageRequest,
  buildSignCommand,
  canCancelOutbound,
  emptyCompletedPage,
  groupCompletedOrderDetails,
  isCompletedPageRequestCurrent,
  isCompletedRowContextCurrent,
  notificationCanRetry,
  type CompletedOrderRow,
  type CompletedTaskDetail
} from './completedOutboundPolicy'

type CompletedTableRow = CompletedOrderRow & {
  tasks: CompletedTaskDetail[]
  detail_loaded: boolean
  detail_loading: boolean
  detail_error: string
}

const props = defineProps<{ warehouseId: number | null }>()
const emit = defineEmits<{ statusChanged: [] }>()
const xTable = ref()
const state = reactive({
  keyword: '',
  tableData: [] as CompletedTableRow[],
  total: 0,
  pageIndex: 1,
  pageSize: DEFAULT_PAGE_SIZE,
  loading: false,
  requestSeq: 0,
  timer: null as ReturnType<typeof setTimeout> | null,
  btnList: [] as btnGroupItem[],
  authorityList: getMenuAuthorityList()
})
const signDialog = reactive({
  visible: false,
  loading: false,
  retry: false,
  row: null as CompletedTableRow | null,
  damagedQty: 0,
  shippedQty: 0,
  error: ''
})

const canOperate = computed(() =>
  props.warehouseId !== null && !state.loading && state.authorityList.includes('delivered-delivery')
)
const cardHeight = computed(() => computedCardHeight({}))
const tableHeight = computed(() => computedTableHeight({}))
const displayNumber = (value: number | null): string => value === null ? '-' : String(value)
const requestId = (operation: string, orderId: number): string =>
  `${operation}-${orderId}-${globalThis.crypto?.randomUUID?.() ?? `${Date.now()}-${Math.random().toString(16).slice(2)}`}`

const notificationLabel = (status: DispatchSignNotificationStatus): string => ({
  SENT: '通知成功', FAILED: '通知失败，可重试', PENDING: '待通知', SENDING: '通知中', NONE: '未通知'
}[status])
const notificationColor = (status: DispatchSignNotificationStatus): string => ({
  SENT: 'success', FAILED: 'error', PENDING: 'warning', SENDING: 'info', NONE: 'default'
}[status])

const signingFacts = (value: DispatchOrderDetail | CompletedOrderRow): Partial<CompletedOrderRow> => {
  return {
    signed_at: value.signed_at,
    signed_qty: value.signed_qty,
    damaged_qty: value.damaged_qty,
    notification_status: value.notification_status,
    notification_last_error: value.notification_last_error
  }
}

const loadDetail = async (row: CompletedTableRow, force = false): Promise<void> => {
  if (row.detail_loading || (row.detail_loaded && !force)) return
  row.detail_loading = true
  row.detail_error = ''
  try {
    const result = await getDispatchOrder(row.id)
    if (!result.isSuccess) throw new Error(result.errorMessage)
    const detail = result.data
    const boxes = await Promise.all(detail.packing_tasks.map(async task => {
      const boxResult = await getDispatchTaskBoxes(row.id, task.id)
      if (!boxResult.isSuccess) throw new Error(boxResult.errorMessage)
      return [task.id, boxResult.data] as const
    }))
    row.tasks = groupCompletedOrderDetails(detail, new Map(boxes))
    row.detail_loaded = true
    Object.assign(row, signingFacts(detail))
  } catch (error) {
    row.detail_error = error instanceof Error ? error.message : '加载已出库明细失败'
  } finally {
    row.detail_loading = false
  }
}

const handleToggleRowExpand: VxeTableEvents.ToggleRowExpand<CompletedTableRow> = ({ row, expanded }) => {
  if (expanded) void loadDetail(row)
}

const resetRequestView = (): void => {
  const empty = emptyCompletedPage()
  state.tableData = empty.rows as CompletedTableRow[]
  state.total = empty.total
  signDialog.visible = false
  signDialog.loading = false
  signDialog.retry = false
  signDialog.row = null
  signDialog.damagedQty = 0
  signDialog.shippedQty = 0
  signDialog.error = ''
}

const getCompleted = async (): Promise<void> => {
  const warehouseId = props.warehouseId
  const sequence = ++state.requestSeq
  resetRequestView()
  if (warehouseId === null) {
    state.loading = false
    return
  }
  const token = { sequence, warehouseId }
  state.loading = true
  try {
    const result = await getDispatchOrderPage(buildCompletedPageRequest(
      warehouseId, state.keyword, state.pageIndex, state.pageSize
    ))
    if (!isCompletedPageRequestCurrent(token, state.requestSeq, props.warehouseId)) return
    if (!result.isSuccess) {
      hookComponent.$message({ type: 'error', content: result.errorMessage })
      return
    }
    state.tableData = result.data.rows.map(row => ({
      ...row,
      tasks: [],
      detail_loaded: false,
      detail_loading: false,
      detail_error: ''
    }))
    state.total = result.data.totals
  } catch (error) {
    if (!isCompletedPageRequestCurrent(token, state.requestSeq, props.warehouseId)) return
    hookComponent.$message({ type: 'error', content: error instanceof Error ? error.message : '已出库主单加载失败' })
  } finally {
    if (isCompletedPageRequestCurrent(token, state.requestSeq, props.warehouseId)) state.loading = false
  }
}

const search = (): void => {
  state.pageIndex = 1
  void getCompleted()
}

const handlePageChange: VxePagerEvents.PageChange = ({ currentPage, pageSize }) => {
  state.pageIndex = currentPage
  state.pageSize = pageSize
  void getCompleted()
}

const cancelRow = (row: CompletedTableRow): void => {
  if (!canCancelOutbound(row)) return
  const warehouseId = props.warehouseId
  if (warehouseId === null) return
  const context = { sequence: state.requestSeq, warehouseId, orderId: row.id, rowVersion: row.row_version }
  hookComponent.$dialog({
    content: `确认整单撤回 ${row.dispatch_no} 到待出库吗？库存扣减将整单回滚。`,
    handleConfirm: async () => {
      if (!isCompletedRowContextCurrent(context, state.requestSeq, props.warehouseId, state.tableData)) {
        hookComponent.$message({ type: 'warning', content: '仓库或列表数据已变化，请重新操作' })
        return
      }
      const command = buildCancelOutboundCommand(row, requestId('cancel-outbound', row.id))
      const result = await cancelDispatchOutbound(command.orderId, command.request)
      if (!isCompletedRowContextCurrent(context, state.requestSeq, props.warehouseId, state.tableData)) return
      if (!result.isSuccess) {
        hookComponent.$message({ type: 'error', content: result.errorMessage })
        return
      }
      hookComponent.$message({ type: 'success', content: '已整单撤回到待出库' })
      await getCompleted()
      emit('statusChanged')
    }
  })
}

const rowShippedQty = (row: CompletedTableRow): number =>
  row.tasks.flatMap(task => task.items).reduce((total, item) => total + (item.required_qty ?? 0), 0)

const openSignDialog = async (row: CompletedTableRow): Promise<void> => {
  const warehouseId = props.warehouseId
  if (warehouseId === null) return
  const context = { sequence: state.requestSeq, warehouseId, orderId: row.id, rowVersion: row.row_version }
  if (!row.detail_loaded) await loadDetail(row)
  if (!isCompletedRowContextCurrent(context, state.requestSeq, props.warehouseId, state.tableData)) return
  if (row.detail_error) return
  signDialog.row = row
  signDialog.retry = notificationCanRetry(row.notification_status)
  signDialog.shippedQty = rowShippedQty(row)
  signDialog.damagedQty = row.damaged_qty ?? 0
  signDialog.error = ''
  signDialog.visible = true
}

const closeSignDialog = (): void => {
  if (signDialog.loading) return
  signDialog.visible = false
  signDialog.row = null
}

const submitSign = async (): Promise<void> => {
  const row = signDialog.row
  if (!row) return
  const damagedQty = Number(signDialog.damagedQty)
  if (!Number.isInteger(damagedQty) || damagedQty < 0 || damagedQty > signDialog.shippedQty) {
    signDialog.error = `请输入 0 到 ${signDialog.shippedQty} 之间的整数`
    return
  }
  signDialog.loading = true
  signDialog.error = ''
  try {
    const command = buildSignCommand(row, damagedQty, requestId('sign', row.id))
    const result = await signDispatchOrder(command.orderId, command.request)
    if (!result.isSuccess) {
      signDialog.error = result.errorMessage
      return
    }
    Object.assign(row, {
      signed_at: row.signed_at ?? new Date().toLocaleString(),
      signed_qty: result.data.signed_qty,
      damaged_qty: result.data.damaged_qty,
      notification_status: result.data.notification_status,
      notification_last_error: result.data.notification_status === 'FAILED' ? '下游签收通知未成功，可重试' : '',
      row_version: result.data.row_version
    })
    signDialog.visible = false
    hookComponent.$message({
      type: result.data.notification_status === 'FAILED' ? 'warning' : 'success',
      content: result.data.notification_status === 'FAILED' ? '签收已保存，通知失败，可在本页重试' : '整单签收完成'
    })
    emit('statusChanged')
  } catch (error) {
    signDialog.error = error instanceof Error ? error.message : '整单签收失败，可重试'
  } finally {
    signDialog.loading = false
  }
}

const exportTable = (): void => {
  exportData({
    table: xTable.value,
    filename: i18n.global.t('wms.deliveryManagement.deliveryReady'),
    columnFilterMethod({ column }: any) { return !['operate'].includes(column?.field) && column?.type !== 'expand' }
  })
}

onMounted(() => {
  state.btnList = [
    { name: i18n.global.t('system.page.refresh'), icon: 'mdi-refresh', code: '', click: getCompleted },
    { name: i18n.global.t('system.page.export'), icon: 'mdi-export-variant', code: 'signedIn-export', click: exportTable }
  ]
})

watch(() => props.warehouseId, () => {
  state.pageIndex = 1
  void getCompleted()
})
watch(() => state.keyword, () => {
  if (state.timer) clearTimeout(state.timer)
  state.timer = setTimeout(() => {
    state.timer = null
    search()
  }, DEBOUNCE_TIME)
})

defineExpose({ getCompleted })
</script>

<style lang="less" scoped>
.operateArea { width: 100%; min-width: 760px; border-radius: 10px; padding: 0 10px; }
.col { display: flex; align-items: center; }
.task-number-list { display: flex; flex-wrap: wrap; gap: 6px; }
.row-actions { display: flex; justify-content: center; gap: 8px; }
.source-warning { margin-top: 6px; font-size: 12px; }
.source-snapshot { max-width: 260px; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; opacity: 0.78; }
.muted-text { opacity: 0.62; }
.notification-error { margin-top: 4px; color: rgb(var(--v-theme-error)); font-size: 12px; }
.order-detail { padding: 16px 60px; background: rgba(var(--v-theme-surface-variant), 0.16); text-align: left; }
.detail-loading { min-height: 100px; display: flex; align-items: center; justify-content: center; }
.task-section + .task-section { margin-top: 18px; padding-top: 18px; border-top: 1px solid rgba(var(--v-border-color), var(--v-border-opacity)); }
.task-title { display: flex; justify-content: space-between; gap: 24px; margin-bottom: 8px; }
.detail-table { margin-top: 8px; }
.detail-table th { white-space: nowrap; font-weight: 600; }
.box-table { background: rgba(var(--v-theme-primary), 0.025); }
.empty-cell, .empty-detail { padding: 20px !important; text-align: center !important; opacity: 0.62; }
</style>
