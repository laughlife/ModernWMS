<template>
  <div class="flowTip">
    <v-icon icon="mdi-information-outline" size="20"></v-icon>
    <span>{{ $t('wms.deliveryManagement.packingTaskFlowTip') }}</span>
  </div>

  <div class="operateArea">
    <v-row no-gutters>
      <v-col cols="3" class="col">
        <BtnGroup :authority-list="data.authorityList" :btn-list="data.btnList" />
      </v-col>
      <v-col cols="3" class="createOrderCol">
        <v-btn
          color="primary"
          prepend-icon="mdi-clipboard-list-outline"
          :disabled="data.creating || data.selectedTaskCount === 0 || props.warehouseId === null"
          :loading="data.creating"
          @click="method.createPickingOrder"
        >
          生成待拣货单（{{ data.selectedTaskCount }}）
        </v-btn>
      </v-col>
      <v-col cols="6" @keyup.enter="method.sureSearch">
        <v-text-field
          v-model="data.searchForm.keyword"
          clearable hide-details density="comfortable"
          class="searchInput ml-5 mt-1"
          :label="$t('wms.deliveryManagement.packingTaskKeyword')" variant="solo"
        ></v-text-field>
      </v-col>
    </v-row>
  </div>

  <div class="mt-5 packing-task-list">
    <div v-for="row in data.tableData" :key="row.sellfox_task_id" class="packing-task-card">
      <div class="card-header">
        <v-checkbox
          :model-value="method.isSelected(row)"
          density="compact"
          hide-details
          @change="method.toggleSelect(row)"
        ></v-checkbox>
        <span class="header-item">拣货单号：-</span>
        <span class="header-item">装箱任务号：{{ row.packing_task_sn }}</span>
        <v-chip size="small" color="primary" variant="tonal">待拣货</v-chip>
        <span class="header-spacer"></span>
        <span class="header-item">仓库：{{ row.warehouse_name || '-' }}</span>
        <span class="header-item">进度：{{ row.complete_num ?? 0 }}/{{ row.task_num ?? 0 }}</span>
        <span class="header-item">创建：{{ row.create_name || '-' }} {{ row.source_create_time || '' }}</span>
        <v-btn
          size="small"
          color="primary"
          variant="tonal"
          :loading="data.creatingRowId === row.sellfox_task_id"
          :disabled="data.creating || props.warehouseId === null"
          @click="method.createPickingOrderForRow(row)"
        >
          生成拣货单
        </v-btn>
      </div>
      <div class="card-body">
        <v-table density="compact">
          <thead>
            <tr>
              <th class="colImage">图片</th>
              <th>商品信息</th>
              <th>FNSKU / MSKU</th>
              <th>装箱信息</th>
              <th>任务量</th>
              <th>可用量</th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="item in row.item_list" :key="item.id">
              <td class="detailImageCell">
                <ProductImage
                  :src="item.main_image || ''"
                  :alt="item.commodity_name || item.commodity_sku || ''"
                  :width="56" :height="56"
                />
              </td>
              <td class="detailProductCell">
                <v-tooltip location="top" text="点击复制">
                  <template #activator="{ props: tipProps }">
                    <div class="productTextLine" v-bind="tipProps" @click="method.copyText(item.commodity_name, '商品名称已复制')">
                      <span class="primaryText">{{ method.displayValue(item.commodity_name) }}</span>
                      <v-icon icon="mdi-content-copy" size="15" color="success"></v-icon>
                    </div>
                  </template>
                </v-tooltip>
                <v-tooltip location="top" text="点击复制">
                  <template #activator="{ props: tipProps }">
                    <div class="productTextLine" v-bind="tipProps" @click="method.copyText(item.commodity_sku || item.sku, '商品SKU已复制')">
                      <span class="secondaryText">{{ method.displayValue(item.commodity_sku || item.sku) }}</span>
                      <v-icon icon="mdi-content-copy" size="15" color="success"></v-icon>
                    </div>
                  </template>
                </v-tooltip>
              </td>
              <td>
                <div>{{ method.displayValue(item.fn_sku) }}</div>
                <div class="secondaryText">{{ method.displayValue(item.msku) }}</div>
              </td>
              <td class="packingInfoCell">
                <div>店铺：{{ row.shop_name || '-' }}</div>
                <div>站点：{{ row.marketplace_name || '-' }}</div>
                <div>负责人：{{ row.create_name || '-' }}</div>
              </td>
              <td>{{ method.displayValue(item.task_num) }}</td>
              <td>
                <div class="availableCell">
                  <div class="stockInfoLines">
                    <div class="stockInfoLine">可用量：{{ method.displayStockAvailable(item) }}</div>
                    <div class="stockInfoLine secondaryText">锁定量：{{ item.locked_qty ?? 0 }}</div>
                  </div>
                  <v-btn
                    size="x-small"
                    color="primary"
                    variant="tonal"
                    @click="method.openStockSelect(row, item)"
                  >
                    选择库存
                  </v-btn>
                </div>
              </td>
            </tr>
            <tr v-if="row.item_list.length === 0">
              <td colspan="6">{{ i18n.global.t('system.page.noData') }}</td>
            </tr>
          </tbody>
        </v-table>
      </div>
    </div>

    <div v-if="data.tableData.length === 0" class="emptyState">
      <v-icon icon="mdi-package-variant-closed" size="38"></v-icon>
      <div>{{ data.errorMessage || $t('wms.deliveryManagement.noPackingTask') }}</div>
    </div>
  </div>

  <custom-pager
    :current-page="data.tablePage.pageIndex" :page-size="data.tablePage.pageSize"
    perfect :total="data.tablePage.total" :page-sizes="PAGE_SIZE" :layouts="PAGE_LAYOUT"
    @page-change="method.handlePageChange"
  ></custom-pager>

  <SelectStockDialog ref="selectStockDialogRef" @changed="method.refresh" />
</template>

<script lang="ts" setup>
import { computed, onMounted, reactive, ref, watch } from 'vue'
import { createDispatchOrder, getWorkflowPackingTaskPage } from '@/api/wms/dispatchWorkflow'
import BtnGroup from '@/components/system/btnGroup.vue'
import { hookComponent } from '@/components/system'
import ProductImage from '@/components/system/product-image.vue'
import customPager from '@/components/custom-pager.vue'
import { DEFAULT_PAGE_SIZE, PAGE_LAYOUT, PAGE_SIZE } from '@/constant/vxeTable'
import { DEBOUNCE_TIME } from '@/constant/system'
import i18n from '@/languages/i18n'
import type { PackingTaskItemVO, PackingTaskVO } from '@/types/DeliveryManagement/PackingTask'
import type { btnGroupItem } from '@/types/System/Form'
import { getMenuAuthorityList } from '@/utils/common'
import {
  buildPackingTaskPageRequest,
  createTaskSetIdempotencyKey,
  removeCreatedPackingTasks,
  resetPackingTaskPageState,
  validatePackingTaskSelection
} from './packingTaskSelection'
import SelectStockDialog from './select-stock-dialog.vue'

const props = defineProps<{ warehouseId: number | null }>()
const emit = defineEmits<{
  ordersCreated: [count: number]
  statusChanged: []
}>()

const selectStockDialogRef = ref<InstanceType<typeof SelectStockDialog>>()

const data = reactive({
  searchForm: { keyword: '' },
  tableData: ref<PackingTaskVO[]>([]),
  selectedTaskIds: ref<number[]>([]),
  errorMessage: '',
  creating: false,
  creatingRowId: null as number | null,
  selectedTaskCount: 0,
  tablePage: reactive({
    total: 0,
    pageIndex: 1,
    pageSize: DEFAULT_PAGE_SIZE
  }),
  timer: ref<ReturnType<typeof setTimeout> | null>(null),
  btnList: [] as btnGroupItem[],
  authorityList: getMenuAuthorityList()
})
let pageRequestId = 0

const method = reactive({
  displayValue: (value: unknown): string | number => value === null || value === undefined ? '' : value as string | number,
  summaryName: (row: PackingTaskVO): string => {
    const first = row.item_list?.[0]
    return first?.commodity_name || first?.commodity_sku || '-'
  },
  summarySku: (row: PackingTaskVO): string => {
    const first = row.item_list?.[0]
    return first?.commodity_sku || first?.sku || '-'
  },
  displayStockAvailable: (item: PackingTaskItemVO): string => {
    if (!item.stock_sku_code) return '-'
    const locked = item.locked_qty ?? 0
    const available = Math.max(0, (item.stock_available_qty ?? 0) - locked)
    return `库存${item.stock_sku_code}:${available}`
  },
  copyText: async (text: string | number | null | undefined, successMessage: string) => {
    const value = String(text ?? '').trim()
    if (!value) {
      hookComponent.$message({ type: 'warning', content: '没有可复制的内容' })
      return
    }
    try {
      if (navigator.clipboard?.writeText) {
        await navigator.clipboard.writeText(value)
      } else {
        const textarea = document.createElement('textarea')
        textarea.value = value
        textarea.style.position = 'fixed'
        textarea.style.opacity = '0'
        document.body.appendChild(textarea)
        textarea.select()
        document.execCommand('copy')
        document.body.removeChild(textarea)
      }
      hookComponent.$message({ type: 'success', content: successMessage })
    } catch {
      hookComponent.$message({ type: 'error', content: '复制失败，请手动复制' })
    }
  },
  isSelected: (row: PackingTaskVO): boolean => data.selectedTaskIds.includes(row.sellfox_task_id),
  toggleSelect: (row: PackingTaskVO): void => {
    const ids = [...data.selectedTaskIds]
    const index = ids.indexOf(row.sellfox_task_id)
    if (index >= 0) {
      ids.splice(index, 1)
    } else {
      ids.push(row.sellfox_task_id)
    }
    const selection = validatePackingTaskSelection(
      data.tableData.filter((t) => ids.includes(t.sellfox_task_id)),
      props.warehouseId
    )
    if (!selection.ok && selection.reason === 'CROSS_WAREHOUSE') {
      hookComponent.$message({ type: 'error', content: '一张WMS拣货单只能包含同一仓库的装箱任务' })
      data.selectedTaskIds = ids.filter((id) => id !== row.sellfox_task_id)
      data.selectedTaskCount = data.selectedTaskIds.length
      return
    }
    data.selectedTaskIds = ids
    data.selectedTaskCount = ids.length
  },
  clearSelection: () => {
    data.selectedTaskIds = []
    data.selectedTaskCount = 0
  },
  clearPage: () => {
    resetPackingTaskPageState(data)
  },
  getPage: async (hideLoading = false) => {
    const requestId = ++pageRequestId
    const request = buildPackingTaskPageRequest(
      props.warehouseId,
      data.searchForm.keyword,
      data.tablePage.pageIndex,
      data.tablePage.pageSize
    )
    if (!request) {
      method.clearPage()
      data.errorMessage = ''
      return
    }
    try {
      const res = await getWorkflowPackingTaskPage(request, hideLoading)
      if (requestId !== pageRequestId) return
      if (!res.isSuccess) {
        method.clearPage()
        data.errorMessage = res.errorMessage
        return
      }
      data.errorMessage = ''
      data.tableData = res.data.rows
      data.tablePage.total = res.data.totals
      method.clearSelection()
    } catch (error) {
      if (requestId !== pageRequestId) return
      method.clearPage()
      data.errorMessage = error instanceof Error ? error.message : String(error)
    }
  },
  // 库存选择完成后的数据回刷属于后台同步，不应再次占用全局阻塞式加载遮罩。
  refresh: () => method.getPage(true),
  handlePageChange: ({ currentPage, pageSize }: { currentPage: number; pageSize: number }) => {
    data.tablePage.pageIndex = currentPage
    data.tablePage.pageSize = pageSize
    method.getPage()
  },
  sureSearch: () => {
    data.tablePage.pageIndex = 1
    method.getPage()
  },
  openStockSelect: (task: PackingTaskVO, item: PackingTaskItemVO) => {
    selectStockDialogRef.value?.openDialog(task, item)
  },
  createOrderForTaskIds: async (sourceTaskIds: number[]) => {
    const warehouseId = props.warehouseId
    if (warehouseId === null) return
    data.creating = true
    const createdTaskIds: number[] = []
    const failedTasks: string[] = []
    try {
      // 批量操作只负责连续创建；每个请求仅生成一个独立拣货单。
      for (const sourceTaskId of sourceTaskIds) {
        try {
          const idempotencyKey = await createTaskSetIdempotencyKey([sourceTaskId])
          const res = await createDispatchOrder({
            warehouse_id: warehouseId,
            source_task_ids: [sourceTaskId],
            idempotency_key: idempotencyKey
          })
          if (res.isSuccess) {
            createdTaskIds.push(sourceTaskId)
            // 每个装箱任务对应一个独立拣货单，成功一单就立即推进顶部角标。
            emit('ordersCreated', 1)
          } else {
            failedTasks.push(`${sourceTaskId}：${res.errorMessage}`)
          }
        } catch (error) {
          failedTasks.push(`${sourceTaskId}：${error instanceof Error ? error.message : String(error)}`)
        }
      }

      data.tableData = removeCreatedPackingTasks(data.tableData, createdTaskIds)
      data.tablePage.total = Math.max(0, data.tablePage.total - createdTaskIds.length)
      method.clearSelection()
      if (createdTaskIds.length > 0) {
        hookComponent.$message({ type: 'success', content: `已生成 ${createdTaskIds.length} 个独立拣货单` })
        emit('statusChanged')
      }
      if (failedTasks.length > 0) {
        hookComponent.$message({ type: 'error', content: `以下装箱任务生成失败：${failedTasks.join('；')}` })
      }
      await method.getPage()
    } catch (error) {
      hookComponent.$message({
        type: 'error',
        content: error instanceof Error ? error.message : String(error)
      })
      await method.getPage()
    } finally {
      data.creating = false
      data.creatingRowId = null
    }
  },
  createPickingOrder: async () => {
    const selection = validatePackingTaskSelection(
      data.tableData.filter((t) => data.selectedTaskIds.includes(t.sellfox_task_id)),
      props.warehouseId
    )
    if (!selection.ok) {
      const content = selection.reason === 'CROSS_WAREHOUSE'
        ? '一张WMS拣货单只能包含同一仓库的装箱任务'
        : selection.reason === 'WAREHOUSE_REQUIRED'
          ? '请先选择仓库'
          : '请至少选择一个有效的装箱任务'
      hookComponent.$message({ type: 'error', content })
      return
    }
    await method.createOrderForTaskIds(selection.sourceTaskIds)
  },
  createPickingOrderForRow: async (row: PackingTaskVO) => {
    const selection = validatePackingTaskSelection([row], props.warehouseId)
    if (!selection.ok) {
      hookComponent.$message({ type: 'error', content: '请先选择仓库' })
      return
    }
    data.creatingRowId = row.sellfox_task_id
    await method.createOrderForTaskIds(selection.sourceTaskIds)
  }
})

onMounted(() => {
  data.btnList = [
    { name: i18n.global.t('system.page.refresh'), icon: 'mdi-refresh', code: '', click: method.refresh }
  ]
})

watch(
  () => data.searchForm,
  () => {
    if (data.timer) clearTimeout(data.timer)
    data.timer = setTimeout(() => {
      data.timer = null
      method.sureSearch()
    }, DEBOUNCE_TIME)
  },
  { deep: true }
)

watch(
  () => props.warehouseId,
  () => {
    data.tablePage.pageIndex = 1
    method.clearSelection()
    method.getPage()
  },
  { immediate: true }
)

defineExpose({ getPackingTask: method.getPage })
</script>

<style lang="less" scoped>
.flowTip {
  display: flex;
  gap: 8px;
  align-items: center;
  margin-bottom: 12px;
  padding: 10px 14px;
  border-radius: 6px;
  background: rgba(var(--v-theme-primary), 0.08);
}

.createOrderCol {
  display: flex;
  align-items: center;
}

.packing-task-list {
  display: flex;
  flex-direction: column;
  gap: 12px;
}

.packing-task-card {
  border: 1px solid rgba(var(--v-border-color), var(--v-border-opacity));
  border-radius: 8px;
  overflow: hidden;
}

.card-header {
  display: flex;
  align-items: center;
  flex-wrap: wrap;
  gap: 12px;
  padding: 8px 14px;
  background: rgba(var(--v-theme-primary), 0.06);
}

.header-item {
  color: rgba(var(--v-theme-on-surface), 0.87);
  font-weight: 500;
}

.header-spacer {
  flex: 1;
}

.card-body {
  padding: 10px 14px;
}

.colImage {
  width: 76px;
}

.detailImageCell {
  width: 76px;
  padding: 6px 10px !important;
}

.detailProductCell,
.packingInfoCell {
  text-align: left;
}

.packingInfoCell {
  line-height: 1.7;
}

.productTextLine {
  display: flex;
  align-items: center;
  gap: 4px;
  white-space: nowrap;
  cursor: pointer;
}

.productTextLine:hover .primaryText,
.productTextLine:hover .secondaryText {
  color: rgba(var(--v-theme-primary), 0.95);
}

.productTextLine .secondaryText {
  margin-top: 0;
}

.primaryText {
  color: rgba(var(--v-theme-on-surface), 0.87);
  font-weight: 500;
}

.secondaryText {
  margin-top: 4px;
  color: rgba(var(--v-theme-on-surface), 0.6);
  font-size: 12px;
}

.availableCell {
  display: flex;
  align-items: center;
  gap: 8px;
}

.stockInfoLines {
  display: flex;
  flex-direction: column;
  gap: 2px;
  line-height: 1.4;
  text-align: left;
}

.stockInfoLine {
  white-space: nowrap;
}

.emptyState {
  display: flex;
  flex-direction: column;
  gap: 8px;
  align-items: center;
  padding: 28px;
  color: rgba(var(--v-theme-on-surface), 0.55);
}
</style>
