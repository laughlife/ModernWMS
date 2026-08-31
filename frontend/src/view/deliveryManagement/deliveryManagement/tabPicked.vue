<template>
  <div class="operateArea">
    <v-row no-gutters>
      <v-col cols="2" class="col">
        <BtnGroup :authority-list="data.authorityList" :btn-list="data.btnList" />
      </v-col>
      <v-col cols="2" class="col">
        <v-btn
          color="primary"
          prepend-icon="mdi-scale-balance"
          :loading="data.weighingBatch"
          :disabled="data.loading || data.weighingBatch || data.selectedOrderCount === 0 || !data.authorityList.includes('weighed-weigh')"
          @click="method.startSelectedWeighing"
        >
          批量去称重（{{ data.selectedOrderCount }}）
        </v-btn>
      </v-col>
      <DispatchSearchFilters
        v-model:keyword="data.searchForm.keyword"
        v-model:group-id="data.searchForm.group_id"
        v-model:member-id="data.searchForm.member_id"
        :cols="8"
        @search="method.sureSearch"
      />
    </v-row>
  </div>

  <div class="mt-5" :style="{ height: cardHeight }">
    <vxe-table
      ref="xTable" :column-config="{ minWidth: '120px' }" :row-config="{ keyField: 'id' }"
      :expand-config="{ expandAll: true, trigger: 'manual' }"
      :data="data.tableData" :height="tableHeight" :loading="data.loading" align="center"
      @checkbox-change="method.handleSelectionChange"
      @checkbox-all="method.handleSelectionChange"
    >
      <template #empty>{{ i18n.global.t('system.page.noData') }}</template>
      <vxe-column type="checkbox" width="52" fixed="left" />
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
              <section v-for="task in row.packing_tasks" :key="task.id" class="task-section">
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
                          :cover="false"
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
      <vxe-column field="dispatch_no" :title="$t('wms.deliveryManagement.wmsOrderNo')" min-width="190" />
      <vxe-column field="packing_task_nos" :title="$t('wms.deliveryManagement.packingTaskNos')" min-width="230" align="left" header-align="left">
        <template #default="{ row }">
          <div class="task-number-list">
            <v-chip v-for="taskNo in row.packing_task_nos" :key="taskNo" size="small" color="primary" variant="tonal">{{ taskNo }}</v-chip>
          </div>
        </template>
      </vxe-column>
      <vxe-column title="状态" width="120">
        <template #default>
          <v-chip color="success" size="small" variant="tonal">已拣货</v-chip>
        </template>
      </vxe-column>
      <vxe-column field="creator" :title="$t('wms.deliveryManagement.creator')" width="140" />
      <vxe-column field="create_time" :title="$t('wms.deliveryManagement.create_time')" width="180">
        <template #default="{ row }">{{ formatDateTime(row.create_time) }}</template>
      </vxe-column>
      <vxe-column field="last_update_time" title="最后更新时间" width="180">
        <template #default="{ row }">{{ formatDateTime(row.last_update_time) }}</template>
      </vxe-column>
      <vxe-column field="operate" :title="$t('system.page.operate')" width="200" fixed="right" :resizable="false">
        <template #default="{ row }">
          <div class="row-actions">
            <TooltipBtn
              :flat="true" icon="mdi-arrow-left" tooltip-text="退回待拣货"
              :disabled="data.loading || data.rollbackOrderId === row.id || !data.authorityList.includes('weighed-weigh')"
              @click="method.rollbackRow(row)"
            />
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
import { computed, nextTick, onMounted, reactive, ref, watch } from 'vue'
import type { VxePagerEvents } from 'vxe-table'
import { decideDispatchSourceChange, getDispatchOrder, getDispatchOrderPage, rollbackDispatchPreviousStage, startDispatchWeighing } from '@/api/wms/dispatchWorkflow'
import { hookComponent } from '@/components/system'
import BtnGroup from '@/components/system/btnGroup.vue'
import ProductImage from '@/components/system/product-image.vue'
import DispatchSearchFilters from './dispatch-search-filters.vue'
import TooltipBtn from '@/components/tooltip-btn.vue'
import customPager from '@/components/custom-pager.vue'
import { computedCardHeight, computedTableHeight } from '@/constant/style'
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
  searchForm: { keyword: '', group_id: null as number | null, member_id: null as number | null },
  loading: false,
  weighingBatch: false,
  rollbackOrderId: null as number | null,
  selectedOrderCount: 0,
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
  xTable.value?.clearRowExpand?.()
  xTable.value?.clearCheckboxRow?.()
  data.selectedOrderCount = 0
  resetDecisionDialog()
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
        group_id: data.searchForm.group_id, member_id: data.searchForm.member_id,
        pageIndex: requestIdentity.pageIndex, pageSize: requestIdentity.pageSize
      })
      if (!isCurrentPickedPageRequest(requestIdentity, currentPageRequestIdentity())) return
      if (!result.isSuccess) {
        hookComponent.$message({ type: 'error', content: result.errorMessage })
        return
      }
      data.tableData = result.data.rows.map(toTableRow)
      data.tablePage.total = result.data.totals
      await nextTick()
      await xTable.value?.setAllRowExpand?.(true)
      data.tableData.forEach((row) => { void loadOrderDetail(row) })
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
  handleSelectionChange: () => {
    data.selectedOrderCount = (xTable.value?.getCheckboxRecords?.() ?? []).length
  },
  openSourceDecision: (row: PickedOrderRow) => openDecisionDialog(row),
  rollbackRow: (row: PickedOrderRow) => {
    hookComponent.$dialog({
      content: `确认将拣货单 ${row.dispatch_no} 退回待拣货吗？`,
      handleConfirm: async () => {
        data.rollbackOrderId = row.id
        try {
          const result = await rollbackDispatchPreviousStage(row.id, {
            request_id: createRequestId('rollback-picked'), row_version: row.row_version
          })
          if (!result.isSuccess) {
            hookComponent.$message({ type: 'error', content: result.errorMessage })
            await method.getPicked()
            return
          }
          hookComponent.$message({ type: 'success', content: '已退回待拣货' })
          emit('statusChanged')
          emit('goToPicking')
        } catch (error) {
          hookComponent.$message({ type: 'error', content: error instanceof Error ? error.message : String(error) })
        } finally {
          data.rollbackOrderId = null
        }
      }
    })
  },
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
  startSelectedWeighing: () => {
    const rows = (xTable.value?.getCheckboxRecords?.() ?? []) as PickedOrderRow[]
    if (rows.length === 0 || data.weighingBatch) return
    hookComponent.$dialog({
      content: `确认将选中的 ${rows.length} 张拣货单批量转入称重？`,
      handleConfirm: async () => {
        data.weighingBatch = true
        const succeeded: PickedOrderRow[] = []
        const failed: string[] = []
        try {
          for (const row of rows) {
            if (!canStartPickedOrderWeighing(row)) {
              failed.push(`${row.dispatch_no}：当前状态不可进入称重`)
              continue
            }
            try {
              const result = await startDispatchWeighing(row.id, buildStartWeighingRequest(row, createRequestId('batch-start-weighing')))
              if (resolveStartWeighingOutcome(result) === 'go-weighing') succeeded.push(row)
              else failed.push(`${row.dispatch_no}：${result.errorMessage || '转入称重失败'}`)
            } catch (error) {
              failed.push(`${row.dispatch_no}：${error instanceof Error ? error.message : String(error)}`)
            }
          }
          if (succeeded.length > 0) {
            hookComponent.$message({ type: 'success', content: `已将 ${succeeded.length} 张拣货单转入称重` })
            emit('statusChanged')
          }
          if (failed.length > 0) hookComponent.$message({ type: 'error', content: `以下拣货单处理失败：${failed.join('；')}` })
          await method.getPicked()
          if (succeeded.length > 0) emit('goToWeighing')
        } finally {
          data.weighingBatch = false
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
defineExpose({ getPicked: method.getPicked })
</script>

<style lang="less" scoped>
.operateArea { width: 100%; min-width: 760px; display: flex; align-items: center; border-radius: 10px; padding: 0 10px; }
.col { display: flex; align-items: center; }
.task-number-list { display: flex; flex-direction: column; align-items: flex-start; gap: 6px; padding: 6px 0; }
.order-detail { padding: 14px 72px; }
.detail-loading, .detail-empty { display: flex; justify-content: center; padding: 24px; }
.task-section + .task-section { margin-top: 16px; }
.task-heading { display: flex; justify-content: space-between; padding: 8px 12px; background: rgba(var(--v-theme-primary), 0.07); }
.secondary-text { margin-top: 3px; color: rgba(var(--v-theme-on-surface), 0.62); font-size: 12px; }
.detail-image-cell { width: 72px; }
.row-actions { display: flex; justify-content: center; gap: 10px; }
.source-diff { margin: 10px 0 0; padding: 10px; max-height: 220px; overflow: auto; white-space: pre-wrap; overflow-wrap: anywhere; border-radius: 6px; background: rgba(var(--v-theme-surface), 0.75); font-size: 12px; }
.dialog-diff { margin: 0 0 16px; border: 1px solid rgba(var(--v-border-color), var(--v-border-opacity)); }
</style>
