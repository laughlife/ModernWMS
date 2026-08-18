<template>
  <div class="operateArea">
    <v-row no-gutters>
      <v-col cols="4" class="col"><BtnGroup :authority-list="data.authorityList" :btn-list="data.btnList" /></v-col>
      <DispatchSearchFilters
        v-model:keyword="data.keyword"
        v-model:group-id="data.group_id"
        v-model:member-id="data.member_id"
        :cols="8"
        @search="method.search"
      />
    </v-row>
  </div>

  <div class="mt-5" :style="{ height: cardHeight }">
    <vxe-table ref="xTable" :loading="data.loading" :column-config="{ minWidth: '120px' }" :row-config="{ keyField: 'id' }"
      :expand-config="{ expandAll: true, trigger: 'manual' }" :data="data.tableData" :height="tableHeight" align="center">
      <template #empty>{{ i18n.global.t('system.page.noData') }}</template>
      <vxe-column type="seq" width="60" />
      <vxe-column type="expand" width="54">
        <template #content="{ row }">
          <div class="order-detail">
            <div v-if="row.detailLoading" class="detail-loading"><v-progress-circular indeterminate color="primary" size="24" /></div>
            <v-alert v-else-if="row.detailError" type="error" variant="tonal" density="compact">{{ row.detailError }}</v-alert>
            <template v-else-if="row.detail">
              <section v-for="task in row.detail.packing_tasks" :key="task.id" class="task-section">
                <div class="task-heading">
                  <strong>装箱任务：{{ task.source_task_no }}</strong>
                  <v-chip size="small" :color="task.measured_box_count >= task.expected_box_count && task.expected_box_count > 0 ? 'success' : 'warning'" variant="tonal">
                    {{ task.measured_box_count }}/{{ task.expected_box_count }} 箱已测量
                  </v-chip>
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
                      <th>操作</th>
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
                      <td>
                        <v-btn size="small" color="primary" variant="tonal" @click="method.showPackingPlanPending">
                          建立装箱并称重
                        </v-btn>
                      </td>
                    </tr>
                    <tr v-if="task.items.length === 0"><td colspan="7" class="detail-empty">{{ $t('system.page.noData') }}</td></tr>
                  </tbody>
                </v-table>
              </section>
            </template>
          </div>
        </template>
      </vxe-column>
      <vxe-column field="dispatch_no" title="WMS拣货单号" min-width="190" align="left" header-align="left" />
      <vxe-column title="装箱任务号" min-width="280" align="left" header-align="left">
        <template #default="{ row }"><div class="task-number-list"><v-chip v-for="taskNo in row.packing_task_nos" :key="taskNo" size="small" variant="tonal">{{ taskNo }}</v-chip></div></template>
      </vxe-column>
      <vxe-column field="warehouse_id" title="仓库ID" width="130" />
      <vxe-column title="状态" width="180">
        <template #default="{ row }"><v-chip :color="row.source_change_pending ? 'error' : 'warning'" size="small" variant="tonal">{{ row.source_change_pending ? '来源变更待人工处理' : '称重测量中' }}</v-chip></template>
      </vxe-column>
      <vxe-column field="creator" :title="$t('wms.deliveryManagement.creator')" width="140" />
      <vxe-date-column field="create_time" title="创建时间" width="180" format="yyyy-MM-dd HH:mm" />
      <vxe-column :title="$t('system.page.operate')" width="130" fixed="right" :resizable="false">
        <template #default="{ row }">
          <div class="row-actions">
            <TooltipBtn v-if="row.source_change_pending" :flat="true" icon="mdi-account-alert" tooltip-text="人工选择继续或取消发货"
              :disabled="!data.authorityList.includes('weighed-weigh')" @click="method.openDecision(row)" />
          </div>
        </template>
      </vxe-column>
    </vxe-table>
    <custom-pager :current-page="data.pageIndex" :page-size="data.pageSize" perfect :total="data.total"
      :page-sizes="PAGE_SIZE" :layouts="PAGE_LAYOUT" @page-change="method.handlePageChange" />
  </div>

  <v-dialog v-model="decisionDialog.visible" max-width="720" persistent>
    <v-card>
      <v-card-title>来源变更人工裁决：{{ decisionDialog.row?.dispatch_no }}</v-card-title>
      <v-card-text>
        <v-alert type="warning" variant="tonal" density="compact" class="mb-3">裁决前称重、复制、完成任务和进入待出库均保持冻结。</v-alert>
        <div class="snapshot-title">来源变更快照</div>
        <pre class="source-change-snapshot">{{ decisionDialog.row?.source_change_snapshot || '暂无变更快照，请复核来源数据后处理。' }}</pre>
        <v-textarea v-model="decisionDialog.reason" label="处理原因（必填）" rows="3" maxlength="500" counter />
      </v-card-text>
      <v-card-actions class="justify-end">
        <v-btn variant="text" :disabled="decisionDialog.submitting" @click="method.closeDecision">关闭</v-btn>
        <v-btn color="error" variant="tonal" :loading="decisionDialog.submitting" @click="method.submitDecision('CANCEL')">取消发货</v-btn>
        <v-btn color="primary" :loading="decisionDialog.submitting" @click="method.submitDecision('CONTINUE')">复核后继续</v-btn>
      </v-card-actions>
    </v-card>
  </v-dialog>
</template>

<script lang="ts" setup>
import { computed, nextTick, onMounted, reactive, ref, watch } from 'vue'
import type { VxePagerEvents } from 'vxe-table'
import { decideDispatchSourceChange, getDispatchOrder, getDispatchOrderPage } from '@/api/wms/dispatchWorkflow'
import { hookComponent } from '@/components/system'
import BtnGroup from '@/components/system/btnGroup.vue'
import ProductImage from '@/components/system/product-image.vue'
import DispatchSearchFilters from './dispatch-search-filters.vue'
import TooltipBtn from '@/components/tooltip-btn.vue'
import customPager from '@/components/custom-pager.vue'
import { computedCardHeight, computedTableHeight } from '@/constant/style'
import { DEFAULT_PAGE_SIZE, PAGE_LAYOUT, PAGE_SIZE } from '@/constant/vxeTable'
import i18n from '@/languages/i18n'
import type { DispatchOrderDetail, DispatchOrderSummary, DispatchSourceDecision } from '@/types/DeliveryManagement/DispatchWorkflow'
import type { btnGroupItem } from '@/types/System/Form'
import { getMenuAuthorityList } from '@/utils/common'
import { buildWeighingSourceDecisionCommand, isCurrentWeighingListRequest } from './dispatchBoxMeasurement'
import type { WeighingListRequestIdentity } from './dispatchBoxMeasurement'

type WeighingOrderRow = DispatchOrderSummary & { detail: DispatchOrderDetail | null; detailLoaded: boolean; detailLoading: boolean; detailError: string }
const props = defineProps<{ warehouseId: number | null }>()
const emit = defineEmits<{ statusChanged: [] }>()
const xTable = ref()
let listRequestSequence = 0
const data = reactive({ keyword: '', group_id: null as number | null, member_id: null as number | null,
  loading: false, tableData: [] as WeighingOrderRow[], total: 0, pageIndex: 1, pageSize: DEFAULT_PAGE_SIZE,
  btnList: [] as btnGroupItem[], authorityList: getMenuAuthorityList() })
const decisionDialog = reactive({ visible: false, submitting: false, reason: '', row: null as DispatchOrderSummary | null })
const requestId = () => globalThis.crypto?.randomUUID?.() ?? `source-decision-${Date.now()}-${Math.random().toString(16).slice(2)}`
const showError = (message: string) => hookComponent.$message({ type: 'error', content: message })
const displayValue = (value: unknown): string | number =>
  value === null || value === undefined || value === '' ? '-' : value as string | number
const currentListIdentity = (): WeighingListRequestIdentity | null => props.warehouseId === null ? null : ({
  sequence: listRequestSequence,
  warehouseId: props.warehouseId,
  keyword: data.keyword.trim(),
  pageIndex: data.pageIndex,
  pageSize: data.pageSize
})
const listRequestIsCurrent = (request: WeighingListRequestIdentity): boolean => {
  const current = currentListIdentity()
  return current !== null && isCurrentWeighingListRequest(request, current)
}

const loadOrderDetail = async (row: WeighingOrderRow): Promise<void> => {
  if (row.detailLoaded || row.detailLoading) return
  row.detailLoading = true; row.detailError = ''
  try {
    const result = await getDispatchOrder(row.id)
    if (!result.isSuccess) { row.detailError = result.errorMessage; return }
    row.detail = result.data; row.detailLoaded = true
  } catch (error) { row.detailError = error instanceof Error ? error.message : '加载拣货单详情失败' }
  finally { row.detailLoading = false }
}

const method = reactive({
  refresh: async () => { await method.getWeighed(); emit('statusChanged') },
  getWeighed: async () => {
    if (props.warehouseId === null) {
      listRequestSequence++
      data.loading = false; data.tableData = []; data.total = 0
      return
    }
    const request: WeighingListRequestIdentity = {
      sequence: ++listRequestSequence,
      warehouseId: props.warehouseId,
      keyword: data.keyword.trim(),
      pageIndex: data.pageIndex,
      pageSize: data.pageSize
    }
    data.loading = true; data.tableData = []; data.total = 0
    try {
      const result = await getDispatchOrderPage({
        status: 'WEIGHING', warehouse_id: request.warehouseId, keyword: request.keyword,
        group_id: data.group_id, member_id: data.member_id,
        pageIndex: request.pageIndex, pageSize: request.pageSize
      })
      if (!listRequestIsCurrent(request)) return
      if (!result.isSuccess) { showError(result.errorMessage); return }
      data.tableData = result.data.rows.map((row) => ({ ...row, detail: null, detailLoaded: false, detailLoading: false, detailError: '' }))
      data.total = result.data.totals
      await nextTick()
      await xTable.value?.setAllRowExpand?.(true)
      data.tableData.forEach((row) => { void loadOrderDetail(row) })
    } catch (error) {
      if (listRequestIsCurrent(request)) showError(error instanceof Error ? error.message : '加载称重列表失败')
    } finally {
      if (listRequestIsCurrent(request)) data.loading = false
    }
  },
  search: () => { data.pageIndex = 1; method.getWeighed() },
  handlePageChange: ref<VxePagerEvents.PageChange>(({ currentPage, pageSize }) => { data.pageIndex = currentPage; data.pageSize = pageSize; method.getWeighed() }),
  showPackingPlanPending: () => {
    hookComponent.$message({ type: 'info', content: '装箱及混装规则待确认，本次仅完成一级页面展示' })
  },
  openDecision: (row: DispatchOrderSummary) => { decisionDialog.row = row; decisionDialog.reason = ''; decisionDialog.visible = true },
  closeDecision: () => { decisionDialog.visible = false; decisionDialog.row = null; decisionDialog.reason = '' },
  submitDecision: async (decision: DispatchSourceDecision) => {
    const row = decisionDialog.row
    if (!row) return
    if (!decisionDialog.reason.trim()) { showError('处理原因不能为空'); return }
    decisionDialog.submitting = true
    try {
      const payload = buildWeighingSourceDecisionCommand(row, decision, decisionDialog.reason, requestId())
      const result = await decideDispatchSourceChange(row.id, payload)
      if (!result.isSuccess) { showError(result.errorMessage); return }
      hookComponent.$message({ type: 'success', content: decision === 'CONTINUE' ? '已确认继续发货，可恢复称重操作' : '已人工取消发货' })
      method.closeDecision(); await method.getWeighed(); emit('statusChanged')
    } catch (error) { showError(error instanceof Error ? error.message : '来源变更裁决失败') }
    finally { decisionDialog.submitting = false }
  }
})

onMounted(() => { data.btnList = [{ name: i18n.global.t('system.page.refresh'), icon: 'mdi-refresh', code: '', click: method.refresh }] })
watch(() => props.warehouseId, () => { data.pageIndex = 1; method.getWeighed() }, { immediate: true })
const cardHeight = computed(() => computedCardHeight({}))
const tableHeight = computed(() => computedTableHeight({}))
defineExpose({ getWeighed: method.getWeighed })
</script>

<style lang="less" scoped>
.operateArea { width: 100%; min-width: 760px; display: flex; align-items: center; border-radius: 10px; padding: 0 10px; }
.col { display: flex; align-items: center; }
.task-number-list { display: flex; flex-wrap: wrap; gap: 6px; }
.order-detail { padding: 14px 72px; background: rgba(var(--v-theme-surface-variant), 0.18); }
.detail-loading { min-height: 88px; display: flex; align-items: center; justify-content: center; }
.task-section + .task-section { margin-top: 16px; }
.task-heading { display: flex; align-items: center; justify-content: space-between; margin-bottom: 8px; text-align: left; }
.detail-image-cell { width: 72px; }
.detail-empty { padding: 24px !important; text-align: center !important; }
.secondary-text { margin-top: 3px; color: rgba(var(--v-theme-on-surface), 0.62); font-size: 12px; }
.row-actions { display: flex; justify-content: center; gap: 8px; }
.snapshot-title { margin-bottom: 6px; font-weight: 600; }
.source-change-snapshot { max-height: 220px; margin: 0 0 16px; padding: 12px; overflow: auto; white-space: pre-wrap; word-break: break-word; border-radius: 6px; background: rgba(var(--v-theme-surface-variant), 0.45); font-family: inherit; }
</style>
