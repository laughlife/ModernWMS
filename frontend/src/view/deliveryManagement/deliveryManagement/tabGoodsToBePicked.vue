<template>
  <div class="operateArea">
    <v-row no-gutters>
      <v-col cols="2" class="col">
        <BtnGroup :authority-list="data.authorityList" :btn-list="data.btnList" />
      </v-col>
      <v-col cols="2" class="col">
        <v-btn
          color="primary"
          prepend-icon="mdi-printer"
          :loading="data.printing"
          :disabled="data.loading || data.printing || data.completing || data.selectedOrderCount === 0"
          @click="method.printSelected"
        >
          批量打印（{{ data.selectedOrderCount }}）
        </v-btn>
      </v-col>
      <v-col cols="2" class="col">
        <v-btn
          color="primary"
          prepend-icon="mdi-check-all"
          :loading="data.completing"
          :disabled="data.loading || data.printing || data.completing || data.selectedOrderCount === 0 || !data.authorityList.includes('picked-confirm')"
          @click="method.completeSelected"
        >
          拣货完成（{{ data.selectedOrderCount }}）
        </v-btn>
      </v-col>
      <v-col cols="6" @keyup.enter="method.sureSearch">
        <v-text-field
          v-model="data.searchForm.keyword"
          clearable
          hide-details
          density="comfortable"
          class="searchInput ml-5 mt-1"
          :label="$t('wms.deliveryManagement.packingTaskKeyword')"
          variant="solo"
        />
      </v-col>
    </v-row>
  </div>

  <div class="mt-5" :style="{ height: cardHeight }">
    <vxe-table
      ref="xTable"
      :column-config="{ minWidth: '120px' }"
      :row-config="{ keyField: 'id' }"
      :expand-config="{ expandAll: true, trigger: 'manual' }"
      :data="data.tableData"
      :height="tableHeight"
      :loading="data.loading"
      align="center"
      @checkbox-change="method.handleSelectionChange"
      @checkbox-all="method.handleSelectionChange"
    >
      <template #empty>{{ data.errorMessage || i18n.global.t('system.page.noData') }}</template>
      <vxe-column type="checkbox" width="52" fixed="left" />
      <vxe-column type="seq" width="60" />
      <vxe-column type="expand" width="54">
        <template #content="{ row }">
          <div class="order-detail">
            <div v-if="row.detail_loading" class="detail-loading">
              <v-progress-circular indeterminate color="primary" size="28" />
            </div>
            <v-alert v-else-if="row.detail_error" type="error" variant="tonal">
              {{ row.detail_error }}
            </v-alert>
            <template v-else-if="row.detail">
              <section v-for="task in row.detail.packing_tasks" :key="task.id" class="task-section">
                <div class="task-heading">
                  <strong>{{ $t('wms.deliveryManagement.packingTaskNo') }}：{{ task.source_task_no }}</strong>
                  <span>状态：待拣货</span>
                </div>
                <v-table density="compact">
                  <thead>
                    <tr>
                      <th>图片</th>
                      <th>{{ $t('wms.deliveryManagement.productInfo') }}</th>
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
                  </tbody>
                </v-table>
              </section>
              <div v-if="row.detail.packing_tasks.length === 0" class="detail-empty">
                {{ i18n.global.t('system.page.noData') }}
              </div>
            </template>
          </div>
        </template>
      </vxe-column>
      <vxe-column field="dispatch_no" :title="$t('wms.deliveryManagement.wmsOrderNo')" min-width="190" />
      <vxe-column :title="$t('wms.deliveryManagement.packingTaskNos')" min-width="230" align="left" header-align="left">
        <template #default="{ row }">
          <div class="task-number-list">
            <v-chip v-for="taskNo in row.packing_task_nos" :key="taskNo" size="small" color="primary" variant="tonal">
              {{ taskNo }}
            </v-chip>
          </div>
        </template>
      </vxe-column>
      <vxe-column title="状态" width="120">
        <template #default>
          <v-chip size="small" color="primary" variant="tonal">待拣货</v-chip>
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
              :flat="true"
              icon="mdi-printer"
              :tooltip-text="$t('system.page.print')"
              :disabled="data.loading || data.printing || data.completing"
              @click="method.printRow(row)"
            />
            <TooltipBtn
              :flat="true"
              icon="mdi-check-all"
              :tooltip-text="$t('wms.deliveryManagement.completePicking')"
              :disabled="data.loading || data.printing || data.completing || !data.authorityList.includes('picked-confirm')"
              @click="method.completeRow(row)"
            />
            <TooltipBtn
              :flat="true"
              icon="mdi-undo"
              tooltip-text="退回"
              :disabled="data.loading || data.printing || data.completing"
              @click="method.rollbackRow(row)"
            />
          </div>
        </template>
      </vxe-column>
    </vxe-table>
    <custom-pager
      :current-page="data.tablePage.pageIndex"
      :page-size="data.tablePage.pageSize"
      perfect
      :total="data.tablePage.total"
      :page-sizes="PAGE_SIZE"
      :layouts="PAGE_LAYOUT"
      @page-change="method.handlePageChange"
    />
  </div>

  <button ref="printButtonRef" v-print="'#pickingPrintArea'" class="print-trigger" type="button" />
  <div id="pickingPrintArea" class="print-area">
    <article v-for="order in data.printOrders" :key="order.id" class="print-order">
      <h2>{{ $t('wms.deliveryManagement.pickingList') }}</h2>
      <table class="print-meta-table">
        <tbody>
          <tr>
            <td>拣货单号：{{ order.dispatch_no }}</td>
            <td>装箱任务号：{{ order.packing_task_nos.join('、') || '-' }}</td>
            <td>仓库名称：{{ warehouseName(order.warehouse_id) }}</td>
          </tr>
        </tbody>
      </table>
      <table class="print-product-table">
        <thead>
          <tr>
            <th>图片</th>
            <th>{{ $t('wms.deliveryManagement.productInfo') }}</th>
            <th>FNSKU / MSKU</th>
            <th>任务量</th>
            <th>商品需求量</th>
            <th>可用量快照</th>
          </tr>
        </thead>
        <tbody>
          <template v-for="task in order.packing_tasks" :key="task.id">
            <tr v-for="item in task.items" :key="item.id">
              <td class="print-image-cell">
                <img
                  v-if="item.main_image"
                  :src="item.main_image"
                  :alt="item.commodity_name || item.commodity_sku"
                  referrerpolicy="no-referrer"
                />
                <span v-else>-</span>
              </td>
              <td>
                <div>{{ displayValue(item.commodity_name) }}</div>
                <div>SKU：{{ displayValue(item.commodity_sku) }}</div>
              </td>
              <td>
                <div>{{ displayValue(item.fn_sku) }}</div>
                <div>{{ displayValue(item.msku) }}</div>
              </td>
              <td>{{ displayValue(item.task_qty) }}</td>
              <td>{{ displayValue(item.required_qty) }}</td>
              <td>{{ displayValue(item.source_stock_available) }}</td>
            </tr>
          </template>
        </tbody>
      </table>
    </article>
  </div>
</template>

<script lang="ts" setup>
import { computed, nextTick, onMounted, reactive, ref, watch } from 'vue'
import type { VxePagerEvents } from 'vxe-table'
import {
  completeDispatchPicking,
  getDispatchOrder,
  getDispatchOrderPage,
  getDispatchOrderPrint,
  reconcileDispatchOrder,
  rollbackPendingPick
} from '@/api/wms/dispatchWorkflow'
import { hookComponent } from '@/components/system'
import BtnGroup from '@/components/system/btnGroup.vue'
import ProductImage from '@/components/system/product-image.vue'
import TooltipBtn from '@/components/tooltip-btn.vue'
import customPager from '@/components/custom-pager.vue'
import { computedCardHeight, computedTableHeight } from '@/constant/style'
import { DEBOUNCE_TIME } from '@/constant/system'
import { DEFAULT_PAGE_SIZE, PAGE_LAYOUT, PAGE_SIZE } from '@/constant/vxeTable'
import i18n from '@/languages/i18n'
import { useDispatchWarehouseStore } from '@/store/module/dispatchWarehouse'
import type { DispatchOrderDetail, DispatchOrderSummary } from '@/types/DeliveryManagement/DispatchWorkflow'
import type { btnGroupItem } from '@/types/System/Form'
import { getMenuAuthorityList } from '@/utils/common'
import {
  buildCompletePickingPayload,
  buildPendingPickBatchPrintSnapshots,
  buildPendingPickPageRequest,
  buildRollbackPendingPickPayload,
  getPendingPickFailureOutcome,
  shouldAcceptPendingPickResponse,
  shouldAcceptPendingPickPrintContext,
  toPendingPickRows
} from './pendingPickPolicy'

type PendingPickTableRow = DispatchOrderSummary & {
  detail: DispatchOrderDetail | null
  detail_loading: boolean
  detail_error: string
}

const props = defineProps<{ warehouseId: number | null }>()
const emit = defineEmits<{ statusChanged: [] }>()
const xTable = ref()
const printButtonRef = ref<HTMLButtonElement>()
const dispatchWarehouseStore = useDispatchWarehouseStore()

const data = reactive({
  searchForm: { keyword: '' },
  timer: null as ReturnType<typeof setTimeout> | null,
  tableData: [] as PendingPickTableRow[],
  printOrders: [] as DispatchOrderDetail[],
  printing: false,
  completing: false,
  selectedOrderCount: 0,
  errorMessage: '',
  loading: false,
  tablePage: { total: 0, pageIndex: 1, pageSize: DEFAULT_PAGE_SIZE },
  btnList: [] as btnGroupItem[],
  authorityList: getMenuAuthorityList()
})

let pageRequestSeq = 0
let printRequestSeq = 0

const toTableRow = (order: DispatchOrderSummary): PendingPickTableRow => ({
  ...order,
  packing_task_nos: [...order.packing_task_nos],
  detail: null,
  detail_loading: false,
  detail_error: ''
})

const displayValue = (value: unknown): string | number =>
  value === null || value === undefined || value === '' ? '-' : value as string | number

const formatDateTime = (value?: string): string => {
  if (!value) return '-'
  const date = new Date(value)
  return Number.isNaN(date.getTime()) ? value : date.toLocaleString('zh-CN', { hour12: false })
}

const warehouseName = (warehouseId: number): string =>
  dispatchWarehouseStore.warehouseOptions.find((warehouse) => warehouse.id === warehouseId)?.name || '-'

const createRequestId = (): string => {
  if (typeof globalThis.crypto?.randomUUID === 'function') return globalThis.crypto.randomUUID()
  return `pick-${Date.now()}-${Math.random().toString(16).slice(2)}`
}

const isCurrentPageRequest = (requestSeq: number, requestedWarehouseId: number | null): boolean =>
  shouldAcceptPendingPickResponse({
    requestSeq,
    latestRequestSeq: pageRequestSeq,
    requestedWarehouseId,
    currentWarehouseId: props.warehouseId
  })

const clearPendingPickView = (): void => {
  printRequestSeq += 1
  data.tableData = []
  data.tablePage.total = 0
  data.printOrders = []
  data.errorMessage = ''
  xTable.value?.clearCheckboxRow?.()
  xTable.value?.clearRowExpand?.()
  data.selectedOrderCount = 0
}

const waitForPrintImages = async (): Promise<void> => {
  const images = Array.from(document.querySelectorAll<HTMLImageElement>('#pickingPrintArea img'))
  await Promise.all(images.map((image) => {
    if (image.complete) return Promise.resolve()
    return new Promise<void>((resolve) => {
      const done = () => {
        image.removeEventListener('load', done)
        image.removeEventListener('error', done)
        resolve()
      }
      image.addEventListener('load', done, { once: true })
      image.addEventListener('error', done, { once: true })
      setTimeout(done, 3000)
    })
  }))
}

const loadDetail = async (row: PendingPickTableRow, force = false): Promise<void> => {
  if ((!force && row.detail) || row.detail_loading) return
  row.detail_loading = true
  row.detail_error = ''
  try {
    const result = await getDispatchOrder(row.id)
    if (!result.isSuccess) {
      row.detail = null
      row.detail_error = result.errorMessage
      return
    }
    row.detail = result.data
    row.row_version = result.data.row_version
  } finally {
    row.detail_loading = false
  }
}

const refreshAfterFailure = async (orderId: number): Promise<void> => {
  const detailResult = await reconcileDispatchOrder(orderId)
  await method.getGoodsToBePicked()
  if (!detailResult.isSuccess) return
  const currentRow = data.tableData.find((row) => row.id === orderId)
  if (currentRow) {
    currentRow.detail = detailResult.data
    currentRow.row_version = detailResult.data.row_version
  }
}

const method = reactive({
  refresh: () => method.getGoodsToBePicked(),
  getGoodsToBePicked: async () => {
    const requestSeq = ++pageRequestSeq
    const requestedWarehouseId = props.warehouseId
    const requestedKeyword = data.searchForm.keyword
    const requestedPageIndex = data.tablePage.pageIndex
    const requestedPageSize = data.tablePage.pageSize
    clearPendingPickView()
    data.loading = requestedWarehouseId !== null

    if (requestedWarehouseId === null) {
      data.loading = false
      return
    }

    try {
      const result = await getDispatchOrderPage(buildPendingPickPageRequest(
        requestedWarehouseId,
        requestedKeyword,
        requestedPageIndex,
        requestedPageSize
      ))
      if (!isCurrentPageRequest(requestSeq, requestedWarehouseId)) return
      if (!result.isSuccess) {
        data.errorMessage = result.errorMessage
        hookComponent.$message({ type: 'error', content: result.errorMessage })
        return
      }
      data.tableData = toPendingPickRows(result.data.rows).map(toTableRow)
      data.tablePage.total = result.data.totals
      await nextTick()
      await xTable.value?.setAllRowExpand?.(true)
      data.tableData.forEach((row) => { void loadDetail(row) })
    } catch (error) {
      if (!isCurrentPageRequest(requestSeq, requestedWarehouseId)) return
      const message = error instanceof Error ? error.message : String(error)
      data.errorMessage = message
      hookComponent.$message({ type: 'error', content: message })
    } finally {
      if (isCurrentPageRequest(requestSeq, requestedWarehouseId)) data.loading = false
    }
  },
  handlePageChange: ref<VxePagerEvents.PageChange>(({ currentPage, pageSize }) => {
    data.tablePage.pageIndex = currentPage
    data.tablePage.pageSize = pageSize
    method.getGoodsToBePicked()
  }),
  sureSearch: () => {
    data.tablePage.pageIndex = 1
    method.getGoodsToBePicked()
  },
  handleSelectionChange: () => {
    data.selectedOrderCount = (xTable.value?.getCheckboxRecords?.() ?? []).length
  },
  printRows: async (rows: PendingPickTableRow[]) => {
    if (rows.length === 0 || data.printing) return
    const requestSeq = ++printRequestSeq
    const requestedWarehouseId = props.warehouseId
    data.printing = true
    try {
      const details: DispatchOrderDetail[] = []
      for (const row of rows) {
        const result = await getDispatchOrderPrint(row.id)
        if (!shouldAcceptPendingPickPrintContext({
          requestSeq,
          latestRequestSeq: printRequestSeq,
          requestedWarehouseId,
          currentWarehouseId: props.warehouseId
        })) return
        if (!result.isSuccess) {
          hookComponent.$message({ type: 'error', content: result.errorMessage })
          await refreshAfterFailure(row.id)
          return
        }
        details.push(result.data)
      }
      data.printOrders = buildPendingPickBatchPrintSnapshots(details)
      await nextTick()
      await waitForPrintImages()
      if (!shouldAcceptPendingPickPrintContext({
        requestSeq,
        latestRequestSeq: printRequestSeq,
        requestedWarehouseId,
        currentWarehouseId: props.warehouseId
      })) return
      printButtonRef.value?.click()
    } catch (error) {
      if (shouldAcceptPendingPickPrintContext({
        requestSeq,
        latestRequestSeq: printRequestSeq,
        requestedWarehouseId,
        currentWarehouseId: props.warehouseId
      })) {
        const message = error instanceof Error ? error.message : String(error)
        hookComponent.$message({ type: 'error', content: message })
      }
    } finally {
      data.printing = false
    }
  },
  printRow: (row: PendingPickTableRow) => method.printRows([row]),
  printSelected: () => method.printRows(
    (xTable.value?.getCheckboxRecords?.() ?? []) as PendingPickTableRow[]
  ),
  completeRows: async (rows: PendingPickTableRow[]) => {
    if (rows.length === 0 || data.completing) return
    data.completing = true
    let completedCount = 0
    const failedMessages: string[] = []
    try {
      for (const row of rows) {
        try {
          const result = await completeDispatchPicking(row.id, buildCompletePickingPayload(row, createRequestId()))
          if (result.isSuccess) {
            completedCount += 1
            continue
          }
          const outcome = getPendingPickFailureOutcome(result.errorMessage)
          failedMessages.push(`${row.dispatch_no}：${outcome.message ?? i18n.global.t(outcome.messageKey)}`)
        } catch (error) {
          failedMessages.push(`${row.dispatch_no}：${error instanceof Error ? error.message : String(error)}`)
        }
      }
      if (completedCount > 0) {
        hookComponent.$message({ type: 'success', content: `已完成 ${completedCount} 张拣货单` })
        emit('statusChanged')
      }
      if (failedMessages.length > 0) {
        hookComponent.$message({ type: 'error', content: failedMessages.join('；') })
      }
      await method.getGoodsToBePicked()
    } finally {
      data.completing = false
    }
  },
  confirmCompleteRows: (rows: PendingPickTableRow[]) => {
    if (rows.length === 0) return
    hookComponent.$dialog({
      content: rows.length === 1
        ? i18n.global.t('wms.deliveryManagement.completePickingConfirm')
        : `确认将选中的 ${rows.length} 张拣货单批量标记为拣货完成？`,
      handleConfirm: () => method.completeRows(rows)
    })
  },
  completeRow: (row: PendingPickTableRow) => method.confirmCompleteRows([row]),
  completeSelected: () => method.confirmCompleteRows(
    (xTable.value?.getCheckboxRecords?.() ?? []) as PendingPickTableRow[]
  ),
  rollbackRow: (row: PendingPickTableRow) => {
    hookComponent.$dialog({
      content: '确认回退该拣货单？回退后装箱任务将回到装箱任务列表，可重新选择建单。',
      handleConfirm: async () => {
        const result = await rollbackPendingPick(row.id, buildRollbackPendingPickPayload(row, createRequestId()))
        if (!result.isSuccess) {
          const outcome = getPendingPickFailureOutcome(result.errorMessage)
          hookComponent.$message({
            type: 'error',
            content: `${outcome.message ?? i18n.global.t(outcome.messageKey)}（${result.errorMessage}）`
          })
          await refreshAfterFailure(row.id)
          return
        }
        hookComponent.$message({ type: 'success', content: '已回退，装箱任务已回到装箱任务列表' })
        await method.getGoodsToBePicked()
        emit('statusChanged')
      }
    })
  }
})

onMounted(() => {
  data.btnList = [
    { name: i18n.global.t('system.page.refresh'), icon: 'mdi-refresh', code: '', click: method.refresh }
  ]
})

const cardHeight = computed(() => computedCardHeight({ hasTab: false, hasOperateBtn: false }))
const tableHeight = computed(() => computedTableHeight({ hasTab: false, hasOperateBtn: false }))

watch(
  () => props.warehouseId,
  () => {
    data.tablePage.pageIndex = 1
    method.getGoodsToBePicked()
  },
  { immediate: true }
)

watch(
  () => data.searchForm.keyword,
  () => {
    if (data.timer) clearTimeout(data.timer)
    data.timer = setTimeout(() => {
      data.timer = null
      method.sureSearch()
    }, DEBOUNCE_TIME)
  }
)

defineExpose({ getGoodsToBePicked: method.getGoodsToBePicked })
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
.print-trigger { position: fixed; left: -10000px; width: 1px; height: 1px; opacity: 0; }
.print-area { position: fixed; left: -10000px; top: 0; width: 1000px; padding: 20px; background: white; color: #000; }
.print-order h2 { margin: 0 0 14px; text-align: center; }
.print-order + .print-order { margin-top: 24px; }
.print-meta-table { margin-bottom: 12px; }
.print-meta-table td { width: 33.333%; text-align: left; font-weight: 600; }
.print-area table { width: 100%; border-collapse: collapse; }
.print-area th, .print-area td { padding: 7px; border: 1px solid #333; text-align: center; }
.print-image-cell { width: 76px; }
.print-image-cell img { display: block; width: 64px; height: 64px; margin: 0 auto; object-fit: contain; }

@media print {
  .print-area { position: static; left: auto; top: auto; width: 100%; padding: 0; }
  .print-order { break-inside: auto; page-break-inside: auto; }
  .print-product-table tr { break-inside: avoid; page-break-inside: avoid; }
}
</style>
