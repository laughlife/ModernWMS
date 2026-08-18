<template>
  <div class="operateArea">
    <v-row no-gutters>
      <v-col cols="4" class="col">
        <BtnGroup :authority-list="data.authorityList" :btn-list="data.btnList" />
      </v-col>
      <v-col cols="8">
        <v-row no-gutters @keyup.enter="method.sureSearch">
          <v-col cols="8">
            <v-text-field
              v-model="data.searchForm.keyword"
              clearable hide-details density="comfortable" class="searchInput ml-5 mt-1"
              :label="$t('wms.deliveryManagement.wmsOrder') + ' / ' + $t('wms.deliveryManagement.packingTaskNos')"
              variant="solo"
            />
          </v-col>
        </v-row>
      </v-col>
    </v-row>
  </div>

  <div class="mt-5" :style="{ height: cardHeight }">
    <vxe-table
      ref="xTable" :column-config="{ minWidth: '110px' }" :row-config="{ keyField: 'id' }"
      :data="data.tableData" :height="tableHeight" :loading="data.loading" align="center"
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
            <template v-else>
              <v-alert v-if="row.source_change_pending" type="warning" variant="tonal" class="mb-3">
                <div class="font-weight-bold">{{ $t('wms.deliveryManagement.sourceChangePending') }}</div>
                <pre v-if="row.source_change_snapshot" class="source-diff">{{ formatSourceDiff(row.source_change_snapshot) }}</pre>
              </v-alert>
              <section v-for="task in row.packing_tasks" :key="task.id" class="task-block">
                <div class="task-heading">
                  <strong>装箱任务号：{{ task.source_task_no }}</strong>
                  <span>状态：已拣货</span>
                </div>
                <v-table density="compact">
                  <thead>
                    <tr>
                      <th>图片</th>
                      <th>商品信息</th>
                      <th>FNSKU / MSKU</th>
                      <th>任务量</th>
                      <th>商品需求量</th>
                      <th>可用量快照</th>
                    </tr>
                  </thead>
                  <tbody>
                    <tr v-for="item in task.items" :key="item.id">
                      <td class="detail-image-cell">
                        <ProductImage
                          :src="item.main_image"
                          :alt="item.commodity_name || item.commodity_sku"
                          :width="56"
                          :height="56"
                        />
                      </td>
                      <td class="text-left">
                        <div>{{ displayValue(item.commodity_name) }}</div>
                        <div class="secondary-text">SKU：{{ displayValue(item.commodity_sku) }}</div>
                      </td>
                      <td>
                        <div>{{ displayValue(item.fn_sku) }}</div>
                        <div class="secondary-text">{{ displayValue(item.msku) }}</div>
                      </td>
                      <td>{{ displayValue(item.task_qty) }}</td>
                      <td>{{ displayValue(item.required_qty) }}</td>
                      <td>{{ displayValue(item.source_stock_available) }}</td>
                    </tr>
                    <tr v-if="task.items.length === 0"><td colspan="6" class="detail-empty">{{ $t('system.page.noData') }}</td></tr>
                  </tbody>
                </v-table>
              </section>
              <div v-if="row.packing_tasks.length === 0" class="detail-empty">{{ $t('system.page.noData') }}</div>
            </template>
          </div>
        </template>
      </vxe-column>
      <vxe-column field="dispatch_no" :title="$t('wms.deliveryManagement.wmsOrderNo')" min-width="180" />
      <vxe-column field="packing_task_nos" :title="$t('wms.deliveryManagement.packingTaskNos')" min-width="260" align="left" header-align="left">
        <template #default="{ row }">
          <div class="task-number-list">
            <v-chip v-for="taskNo in row.packing_task_nos" :key="taskNo" size="small" variant="tonal">{{ taskNo }}</v-chip>
          </div>
        </template>
      </vxe-column>
      <vxe-column :title="$t('wms.deliveryManagement.state')" width="190">
        <template #default="{ row }">
          <v-chip v-if="row.source_change_pending" color="warning" size="small" variant="tonal" prepend-icon="mdi-lock-alert">
            {{ $t('wms.deliveryManagement.sourceChangePending') }}
          </v-chip>
          <v-chip v-else color="success" size="small" variant="tonal">{{ $t('wms.deliveryManagement.picked') }}</v-chip>
        </template>
      </vxe-column>
      <vxe-column field="creator" :title="$t('wms.deliveryManagement.creator')" min-width="130" />
      <vxe-column field="create_time" :title="$t('wms.deliveryManagement.create_time')" width="175">
        <template #default="{ row }">{{ formatDateTime(row.create_time) }}</template>
      </vxe-column>
      <vxe-column field="operate" :title="$t('system.page.operate')" width="150" fixed="right" :resizable="false">
        <template #default="{ row }">
          <div class="row-actions">
            <TooltipBtn
              v-if="row.source_change_pending" :flat="true" icon="mdi-account-alert"
              :tooltip-text="$t('wms.deliveryManagement.sourceChangePending')"
              :disabled="!data.authorityList.includes('weighed-weigh')" @click="method.openSourceDecision(row)"
            />
            <TooltipBtn
              :flat="true" icon="mdi-basket-fill" :tooltip-text="$t('wms.deliveryManagement.goToWeighing')"
              :disabled="!data.authorityList.includes('weighed-weigh') || !canStartPickedOrderWeighing(row)"
              @click="method.startWeighingRow(row)"
            />
          </div>
        </template>
      </vxe-column>
    </vxe-table>
    <custom-pager
      :current-page="data.tablePage.pageIndex" :page-size="data.tablePage.pageSize" perfect
      :total="data.tablePage.total" :page-sizes="PAGE_SIZE" :layouts="PAGE_LAYOUT"
      @page-change="method.handlePageChange"
    />
  </div>

  <v-dialog v-model="decision.visible" width="760" persistent>
    <v-card>
      <v-card-title>{{ $t('wms.deliveryManagement.sourceChangePending') }}</v-card-title>
      <v-card-text>
        <v-alert type="warning" variant="tonal" class="mb-4">
          {{ decision.order?.dispatch_no }}：{{ $t('wms.deliveryManagement.sourceChangePending') }}
        </v-alert>
        <pre v-if="decision.order?.source_change_snapshot" class="source-diff dialog-diff">{{ formatSourceDiff(decision.order.source_change_snapshot) }}</pre>
        <v-textarea
          v-model="decision.reason" :label="$t('wms.deliveryManagement.sourceChangeReason')"
          :error="decision.reasonTouched && !isDecisionReasonValid(decision.reason)"
          :error-messages="decision.reasonTouched && !isDecisionReasonValid(decision.reason) ? [$t('wms.deliveryManagement.sourceChangeReason')] : []"
          variant="outlined" rows="3" maxlength="500" counter @blur="decision.reasonTouched = true"
        />
      </v-card-text>
      <v-card-actions>
        <v-spacer />
        <v-btn variant="text" :disabled="decision.submitting" @click="closeDecisionDialog">{{ $t('system.page.close') }}</v-btn>
        <v-btn color="error" variant="tonal" :loading="decision.submitting" @click="method.submitSourceDecision('CANCEL')">
          {{ $t('wms.deliveryManagement.sourceChangeCancel') }}
        </v-btn>
        <v-btn color="primary" :loading="decision.submitting" @click="method.submitSourceDecision('CONTINUE')">
          {{ $t('wms.deliveryManagement.sourceChangeContinue') }}
        </v-btn>
      </v-card-actions>
    </v-card>
  </v-dialog>
</template>

<script lang="ts" setup>
import { computed, onMounted, reactive, ref, watch } from 'vue'
import type { VxePagerEvents, VxeTableEvents } from 'vxe-table'
import { decideDispatchSourceChange, getDispatchOrder, getDispatchOrderPage, startDispatchWeighing } from '@/api/wms/dispatchWorkflow'
import { hookComponent } from '@/components/system'
import BtnGroup from '@/components/system/btnGroup.vue'
import ProductImage from '@/components/system/product-image.vue'
import TooltipBtn from '@/components/tooltip-btn.vue'
import customPager from '@/components/custom-pager.vue'
import { computedCardHeight, computedTableHeight } from '@/constant/style'
import { DEBOUNCE_TIME } from '@/constant/system'
import { DEFAULT_PAGE_SIZE, PAGE_LAYOUT, PAGE_SIZE } from '@/constant/vxeTable'
import i18n from '@/languages/i18n'
import type { DispatchOrderDetail, DispatchOrderSummary, DispatchPackingTask, DispatchSourceDecision } from '@/types/DeliveryManagement/DispatchWorkflow'
import type { btnGroupItem } from '@/types/System/Form'
import { getMenuAuthorityList } from '@/utils/common'
import {
  buildPickedOrderDecisionRequest, buildStartWeighingRequest, canStartPickedOrderWeighing,
  isCurrentPickedPageRequest, isDecisionReasonValid, resolveStartWeighingOutcome
} from './pickedOrderPolicy'
import type { PickedPageRequestIdentity } from './pickedOrderPolicy'

type PickedOrderRow = DispatchOrderSummary & {
  packing_tasks: DispatchPackingTask[]
  detail_loaded: boolean
  detail_loading: boolean
}

const props = defineProps<{ warehouseId: number | null }>()
const emit = defineEmits<{ goToWeighing: []; goToPicking: []; statusChanged: [] }>()
const xTable = ref()
let pageRequestSequence = 0

const data = reactive({
  searchForm: { keyword: '' },
  timer: null as ReturnType<typeof setTimeout> | null,
  loading: false,
  tableData: [] as PickedOrderRow[],
  tablePage: { total: 0, pageIndex: 1, pageSize: DEFAULT_PAGE_SIZE },
  btnList: [] as btnGroupItem[],
  authorityList: getMenuAuthorityList()
})
const decision = reactive({
  visible: false, submitting: false, reason: '', reasonTouched: false,
  order: null as PickedOrderRow | null
})

const createRequestId = (prefix: string): string => {
  const id = globalThis.crypto?.randomUUID?.() ?? `${Date.now()}-${Math.random().toString(16).slice(2)}`
  return `${prefix}-${id}`
}
const toTableRow = (row: DispatchOrderSummary): PickedOrderRow => ({
  ...row, packing_tasks: [], detail_loaded: false, detail_loading: false
})
const displayValue = (value: unknown): string | number =>
  value === null || value === undefined || value === '' ? '-' : value as string | number
const applyDetail = (row: PickedOrderRow, detail: DispatchOrderDetail): void => {
  Object.assign(row, detail, {
    packing_tasks: detail.packing_tasks,
    detail_loaded: true,
    detail_loading: false
  })
}
const loadOrderDetail = async (row: PickedOrderRow): Promise<boolean> => {
  row.detail_loading = true
  try {
    const result = await getDispatchOrder(row.id)
    if (!result.isSuccess) {
      hookComponent.$message({ type: 'error', content: result.errorMessage })
      return false
    }
    applyDetail(row, result.data)
    return true
  } catch (error) {
    hookComponent.$message({ type: 'error', content: error instanceof Error ? error.message : String(error) })
    return false
  } finally {
    row.detail_loading = false
  }
}
const handleToggleRowExpand: VxeTableEvents.ToggleRowExpand<PickedOrderRow> = async ({ row, expanded }) => {
  if (expanded && !row.detail_loaded && !row.detail_loading) await loadOrderDetail(row)
}
const resetDecisionDialog = (): void => {
  decision.visible = false
  decision.order = null
  decision.reason = ''
  decision.reasonTouched = false
}
const closeDecisionDialog = (): void => {
  if (!decision.submitting) resetDecisionDialog()
}
const openDecisionDialog = async (row: PickedOrderRow): Promise<void> => {
  const pageIdentity = currentPageRequestIdentity()
  if (!(await loadOrderDetail(row))) return
  if (!isCurrentPickedPageRequest(pageIdentity, currentPageRequestIdentity())) return
  if (!data.tableData.some((current) => current.id === row.id)) return
  decision.order = row
  decision.reason = ''
  decision.reasonTouched = false
  decision.visible = true
}
const formatDateTime = (value?: string): string => {
  if (!value) return '-'
  const date = new Date(value)
  return Number.isNaN(date.getTime()) ? value : date.toLocaleString('zh-CN', { hour12: false })
}
const formatSourceDiff = (value: string): string => {
  if (!value) return ''
  try { return JSON.stringify(JSON.parse(value), null, 2) } catch { return value }
}
const currentPageRequestIdentity = (sequence = pageRequestSequence): PickedPageRequestIdentity => ({
  sequence,
  warehouseId: props.warehouseId,
  keyword: data.searchForm.keyword.trim(),
  pageIndex: data.tablePage.pageIndex,
  pageSize: data.tablePage.pageSize
})
const clearPageForRequest = (): void => {
  data.tableData = []
  data.tablePage.total = 0
  data.loading = false
  resetDecisionDialog()
}
const invalidatePageRequest = (): void => {
  pageRequestSequence++
  clearPageForRequest()
}

const method = reactive({
  refresh: () => method.getPicked(),
  getPicked: async () => {
    const requestIdentity = currentPageRequestIdentity(++pageRequestSequence)
    clearPageForRequest()
    if (requestIdentity.warehouseId === null) return
    data.loading = true
    try {
      const result = await getDispatchOrderPage({
        status: 'PICKED', warehouse_id: requestIdentity.warehouseId, keyword: requestIdentity.keyword,
        pageIndex: requestIdentity.pageIndex, pageSize: requestIdentity.pageSize
      })
      if (!isCurrentPickedPageRequest(requestIdentity, currentPageRequestIdentity())) return
      if (!result.isSuccess) {
        hookComponent.$message({ type: 'error', content: result.errorMessage })
        return
      }
      data.tableData = result.data.rows.map(toTableRow)
      data.tablePage.total = result.data.totals
    } catch (error) {
      if (isCurrentPickedPageRequest(requestIdentity, currentPageRequestIdentity())) {
        hookComponent.$message({ type: 'error', content: error instanceof Error ? error.message : String(error) })
      }
    } finally {
      if (isCurrentPickedPageRequest(requestIdentity, currentPageRequestIdentity())) data.loading = false
    }
  },
  handlePageChange: ref<VxePagerEvents.PageChange>(({ currentPage, pageSize }) => {
    data.tablePage.pageIndex = currentPage
    data.tablePage.pageSize = pageSize
    method.getPicked()
  }),
  sureSearch: () => { data.tablePage.pageIndex = 1; method.getPicked() },
  openSourceDecision: (row: PickedOrderRow) => openDecisionDialog(row),
  startWeighingRow: (row: PickedOrderRow) => {
    if (!canStartPickedOrderWeighing(row)) { openDecisionDialog(row); return }
    hookComponent.$dialog({
      content: i18n.global.t('wms.deliveryManagement.goToWeighingConfirm'),
      handleConfirm: async () => {
        try {
          const result = await startDispatchWeighing(row.id, buildStartWeighingRequest(row, createRequestId('start-weighing')))
          const outcome = resolveStartWeighingOutcome(result)
          if (outcome === 'go-weighing') {
            hookComponent.$message({ type: 'success', content: i18n.global.t('wms.deliveryManagement.goToWeighing') })
            emit('statusChanged')
            emit('goToWeighing')
            return
          }
          if (outcome === 'source-decision') {
            await openDecisionDialog(row)
            emit('statusChanged')
            return
          }
          hookComponent.$message({ type: 'error', content: result.errorMessage })
        } catch (error) {
          hookComponent.$message({ type: 'error', content: error instanceof Error ? error.message : String(error) })
        }
      }
    })
  },
  submitSourceDecision: async (choice: DispatchSourceDecision) => {
    decision.reasonTouched = true
    const order = decision.order
    if (!order || !isDecisionReasonValid(decision.reason)) return
    decision.submitting = true
    try {
      const payload = buildPickedOrderDecisionRequest({
        order, decision: choice,
        reason: decision.reason, requestId: createRequestId(`source-${choice.toLowerCase()}`)
      })
      const result = await decideDispatchSourceChange(order.id, payload)
      if (!result.isSuccess) {
        hookComponent.$message({ type: 'error', content: result.errorMessage })
        return
      }
      hookComponent.$message({
        type: 'success',
        content: choice === 'CONTINUE'
          ? i18n.global.t('wms.deliveryManagement.sourceChangeContinue')
          : i18n.global.t('wms.deliveryManagement.sourceChangeCancel')
      })
      decision.submitting = false
      resetDecisionDialog()
      await method.getPicked()
      emit('statusChanged')
    } catch (error) {
      hookComponent.$message({ type: 'error', content: error instanceof Error ? error.message : String(error) })
    } finally {
      decision.submitting = false
    }
  }
})

onMounted(() => {
  data.btnList = [{ name: i18n.global.t('system.page.refresh'), icon: 'mdi-refresh', code: '', click: method.refresh }]
})
const cardHeight = computed(() => computedCardHeight({}))
const tableHeight = computed(() => computedTableHeight({}))
watch(() => props.warehouseId, () => { data.tablePage.pageIndex = 1; method.getPicked() }, { immediate: true })
watch(() => data.searchForm.keyword, () => {
  if (data.timer) clearTimeout(data.timer)
  invalidatePageRequest()
  data.timer = setTimeout(() => { data.timer = null; method.sureSearch() }, DEBOUNCE_TIME)
})
defineExpose({ getPicked: method.getPicked })
</script>

<style lang="less" scoped>
.operateArea { width: 100%; min-width: 760px; display: flex; align-items: center; border-radius: 10px; padding: 0 10px; }
.col { display: flex; align-items: center; }
.row-actions { display: flex; justify-content: center; gap: 8px; }
.task-number-list { display: flex; flex-wrap: wrap; gap: 6px; padding: 4px 0; }
.order-detail { padding: 16px 72px; background: rgba(var(--v-theme-surface-variant), 0.18); }
.detail-loading { min-height: 100px; display: flex; align-items: center; justify-content: center; }
.task-block { margin-bottom: 14px; border: 1px solid rgba(var(--v-border-color), var(--v-border-opacity)); border-radius: 6px; overflow: hidden; background: rgb(var(--v-theme-surface)); }
.task-heading { display: flex; justify-content: space-between; gap: 20px; padding: 10px 16px; background: rgba(var(--v-theme-primary), 0.08); }
.secondary-text { margin-top: 3px; color: rgba(var(--v-theme-on-surface), 0.62); font-size: 12px; }
.detail-image-cell { width: 72px; }
.detail-empty { padding: 24px !important; text-align: center !important; opacity: 0.62; }
.source-diff { margin: 10px 0 0; padding: 10px; max-height: 220px; overflow: auto; white-space: pre-wrap; overflow-wrap: anywhere; border-radius: 6px; background: rgba(var(--v-theme-surface), 0.75); font-size: 12px; }
.dialog-diff { margin: 0 0 16px; border: 1px solid rgba(var(--v-border-color), var(--v-border-opacity)); }
</style>
