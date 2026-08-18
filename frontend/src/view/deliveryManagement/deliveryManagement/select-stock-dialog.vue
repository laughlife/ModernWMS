<template>
  <v-dialog v-model="visible" max-width="1120" persistent>
    <v-card>
      <v-toolbar color="white" density="compact" title="选择库存">
        <template #append>
          <v-btn icon="mdi-close" variant="text" aria-label="关闭" @click="cancelDialog"></v-btn>
        </template>
      </v-toolbar>
      <v-divider></v-divider>
      <v-card-text>
        <div class="stock-dialog-summary">
          <span>装箱任务号：{{ task?.packing_task_sn || '-' }}</span>
          <span>创建人：{{ task?.create_name || '-' }}</span>
          <span>商品：{{ item?.commodity_name || '-' }}</span>
          <span>SKU：{{ item?.commodity_sku || item?.sku || '-' }}</span>
          <span>仓库：{{ task?.warehouse_name || '-' }}</span>
        </div>

        <div class="stock-search-bar">
          <v-text-field
            v-model="searchForm.keyword"
            label="SKU/商品名称"
            hide-details
            density="compact"
            variant="outlined"
            clearable
            class="search-field"
            @keyup.enter="method.searchOthers"
          />
          <v-text-field
            v-model="searchForm.location"
            label="库位"
            hide-details
            density="compact"
            variant="outlined"
            clearable
            class="search-field"
            @keyup.enter="method.searchOthers"
          />
          <v-text-field
            v-model="searchForm.owner"
            label="所属人"
            hide-details
            density="compact"
            variant="outlined"
            clearable
            class="search-field"
            @keyup.enter="method.searchOthers"
          />
          <v-btn color="primary" variant="tonal" :loading="loading" @click="method.searchOthers">
            搜索其他库存
          </v-btn>
          <v-btn variant="text" :disabled="loading" @click="method.resetSearch">重置</v-btn>
        </div>

        <vxe-table
          ref="xTable"
          :data="stockRows"
          :column-config="{ minWidth: '110px' }"
          :row-class-name="rowClassName"
          :loading="loading"
          align="center"
          height="400"
        >
          <template #empty>暂无可用库存</template>
          <vxe-column type="seq" width="56"></vxe-column>
          <vxe-column width="76">
            <template #default="{ row }">
              <ProductImage :src="row.main_image" :alt="row.commodity_name || row.sku_code" :width="48" :height="48" />
            </template>
          </vxe-column>
          <vxe-column field="sku_code" title="SKU" min-width="140"></vxe-column>
          <vxe-column field="commodity_name" title="商品名称" min-width="180"></vxe-column>
          <vxe-column field="location_name" title="库位" min-width="130"></vxe-column>
          <vxe-column field="goods_owner_name" title="所属人" min-width="130"></vxe-column>
          <vxe-column field="qty" title="库存量" width="90"></vxe-column>
          <vxe-column field="available_qty" title="可用量" width="90"></vxe-column>
          <vxe-column title="状态" width="100">
            <template #default="{ row }">
              <v-chip v-if="row.selected" size="small" color="success" variant="tonal">已选择</v-chip>
              <v-chip v-else-if="row.matched" size="small" color="primary" variant="tonal">匹配</v-chip>
              <v-chip v-else-if="row.is_creator_stock" size="small" color="info" variant="tonal">创建人</v-chip>
              <v-chip v-else size="small" variant="tonal">其它</v-chip>
            </template>
          </vxe-column>
          <vxe-column title="操作" width="130" fixed="right">
            <template #default="{ row }">
              <v-btn
                v-if="row.selected"
                size="small"
                color="error"
                variant="tonal"
                :disabled="loading"
                :loading="selectingStockId === row.stock_id"
                @click="method.unselectStock(row)"
              >
                取消选择
              </v-btn>
              <v-btn
                v-else
                size="small"
                color="primary"
                variant="tonal"
                :disabled="row.available_qty <= 0"
                :loading="selectingStockId === row.stock_id"
                @click="method.selectStock(row)"
              >
                选择
              </v-btn>
            </template>
          </vxe-column>
        </vxe-table>
        <div class="stock-dialog-footer">
          <span>{{ footerText }}</span>
          <div class="footer-actions">
            <v-btn
              size="small"
              variant="text"
              :disabled="loading || stockRows.length >= total"
              @click="method.loadMore"
            >
              查看更多
            </v-btn>
            <v-btn variant="text" :disabled="loading" @click="cancelDialog">取消</v-btn>
            <v-btn color="primary" variant="tonal" :disabled="loading" @click="confirmDialog">确定</v-btn>
          </div>
        </div>
      </v-card-text>
    </v-card>
  </v-dialog>
</template>

<script lang="ts" setup>
import { computed, reactive, ref } from 'vue'
import { getPackingTaskSelectableStock, selectPackingTaskStock, deletePackingTaskStockSelection } from '@/api/wms/dispatchWorkflow'
import { hookComponent } from '@/components/system'
import ProductImage from '@/components/system/product-image.vue'
import type { PackingTaskItemVO, PackingTaskVO, SelectableStockVO } from '@/types/DeliveryManagement/PackingTask'

const PAGE_SIZE = 20

const emit = defineEmits<{ changed: [selected: SelectableStockVO[]] }>()

const visible = ref(false)
const loading = ref(false)
const task = ref<PackingTaskVO | null>(null)
const item = ref<PackingTaskItemVO | null>(null)
const stockRows = ref<SelectableStockVO[]>([])
const selectedRows = ref<SelectableStockVO[]>([])
const total = ref(0)
const pageIndex = ref(1)
const selectingStockId = ref<number | null>(null)
const searchForm = reactive({ keyword: '', location: '', owner: '' })
const searching = ref(false)

const footerText = computed(() => searching.value
  ? `搜索其他库存，共 ${total.value} 条匹配记录`
  : `当前展示创建人（${task.value?.create_name || '-'}）的库存，共 ${total.value} 条；搜索可查看其他库存`)

// 已选择的库存行显示绿色背景。
const rowClassName = ({ row }: { row: SelectableStockVO }): string => row.selected ? 'selected-row' : ''

const method = reactive({
  loadPage: async () => {
    if (!item.value || !task.value) return
    loading.value = true
    try {
      const result = await getPackingTaskSelectableStock({
        sellfox_task_id: task.value.sellfox_task_id,
        sellfox_item_id: item.value.sellfox_item_id,
        page_index: pageIndex.value,
        page_size: PAGE_SIZE,
        search_others: searching.value,
        keyword: searchForm.keyword.trim(),
        location: searchForm.location.trim(),
        owner: searchForm.owner.trim()
      })
      if (!result.isSuccess) {
        hookComponent.$message({ type: 'error', content: result.errorMessage })
        return
      }
      if (pageIndex.value === 1) {
        stockRows.value = result.data.rows
      } else {
        stockRows.value = [...stockRows.value, ...result.data.rows]
      }
      total.value = result.data.totals
    } catch (error) {
      hookComponent.$message({ type: 'error', content: error instanceof Error ? error.message : String(error) })
    } finally {
      loading.value = false
    }
  },
  searchOthers: () => {
    searching.value = true
    pageIndex.value = 1
    method.loadPage()
  },
  resetSearch: () => {
    searchForm.keyword = ''
    searchForm.location = ''
    searchForm.owner = ''
    searching.value = false
    pageIndex.value = 1
    method.loadPage()
  },
  loadMore: () => {
    pageIndex.value += 1
    method.loadPage()
  },
  selectStock: (row: SelectableStockVO) => {
    if (row.is_creator_stock) {
      method.confirmSelectStock(row)
      return
    }
    hookComponent.$dialog({
      content: '所选库存不是创建人的商品，是否继续执行',
      confirmText: '是',
      cancleText: '否',
      handleConfirm: () => method.confirmSelectStock(row)
    })
  },
  confirmSelectStock: async (row: SelectableStockVO) => {
    if (!item.value || !task.value) return
    selectingStockId.value = row.stock_id
    try {
      const result = await selectPackingTaskStock({
        sellfox_task_id: task.value.sellfox_task_id,
        sellfox_item_id: item.value.sellfox_item_id,
        stock_id: row.stock_id,
        qty: row.available_qty
      })
      if (!result.isSuccess) {
        hookComponent.$message({ type: 'error', content: result.errorMessage })
        return
      }
      hookComponent.$message({ type: 'success', content: '库存选择成功' })
      const selected = { ...row, selected: true, available_qty: 0 }
      stockRows.value = stockRows.value.map((t) =>
        t.stock_id === row.stock_id ? selected : t
      )
      selectedRows.value = [
        ...selectedRows.value.filter((t) => t.stock_id !== row.stock_id),
        selected
      ]
      // 选择成功后自动关闭，并把所选库存回传给父页面刷新数据。
      emit('changed', [...selectedRows.value])
      visible.value = false
    } catch (error) {
      hookComponent.$message({ type: 'error', content: error instanceof Error ? error.message : String(error) })
    } finally {
      selectingStockId.value = null
    }
  },
  unselectStock: async (row: SelectableStockVO) => {
    if (!item.value || !task.value) return
    selectingStockId.value = row.stock_id
    try {
      const result = await deletePackingTaskStockSelection({
        sellfox_task_id: task.value.sellfox_task_id,
        sellfox_item_id: item.value.sellfox_item_id,
        stock_id: row.stock_id,
        qty: 0
      })
      if (!result.isSuccess) {
        hookComponent.$message({ type: 'error', content: result.errorMessage })
        return
      }
      hookComponent.$message({ type: 'success', content: '已取消选择，锁定库存已释放' })
      selectedRows.value = selectedRows.value.filter((t) => t.stock_id !== row.stock_id)
      // 重新加载列表恢复该行的可用量与选择状态，并通知父页面刷新锁定量。
      pageIndex.value = 1
      await method.loadPage()
      emit('changed', [...selectedRows.value])
    } catch (error) {
      hookComponent.$message({ type: 'error', content: error instanceof Error ? error.message : String(error) })
    } finally {
      selectingStockId.value = null
    }
  }
})

const openDialog = (taskRow: PackingTaskVO, itemRow: PackingTaskItemVO): void => {
  task.value = taskRow
  item.value = itemRow
  stockRows.value = []
  selectedRows.value = []
  total.value = 0
  pageIndex.value = 1
  selectingStockId.value = null
  searchForm.keyword = ''
  searchForm.location = ''
  searchForm.owner = ''
  searching.value = false
  visible.value = true
  method.loadPage()
}

// 确定：把本次已选择的库存回传给父页面并关闭。
const confirmDialog = (): void => {
  emit('changed', [...selectedRows.value])
  visible.value = false
}

// 取消：仅关闭，不触发刷新。
const cancelDialog = (): void => {
  visible.value = false
}

defineExpose({ openDialog })
</script>

<style lang="less" scoped>
.stock-dialog-summary {
  display: flex;
  flex-wrap: wrap;
  gap: 16px;
  margin-bottom: 12px;
  padding: 10px 14px;
  border-radius: 6px;
  background: rgba(var(--v-theme-primary), 0.06);
  font-weight: 500;
}

.stock-search-bar {
  display: flex;
  align-items: center;
  gap: 10px;
  margin-bottom: 12px;
  padding: 10px 14px;
  border-radius: 6px;
  background: rgba(var(--v-theme-info), 0.05);
}

.search-field {
  flex: 1;
  min-width: 140px;
}

.stock-dialog-footer {
  display: flex;
  align-items: center;
  justify-content: space-between;
  margin-top: 10px;
  color: rgba(var(--v-theme-on-surface), 0.6);
  font-size: 12px;
}

.footer-actions {
  display: flex;
  align-items: center;
  gap: 8px;
}

.selected-row,
.selected-row > td,
:deep(.selected-row),
:deep(.selected-row > td),
:deep(.vxe-table--fixed-left-wrapper .selected-row),
:deep(.vxe-table--fixed-left-wrapper .selected-row > td),
:deep(.vxe-table--fixed-right-wrapper .selected-row),
:deep(.vxe-table--fixed-right-wrapper .selected-row > td) {
  background-color: #e8f5e9 !important;
}
</style>
