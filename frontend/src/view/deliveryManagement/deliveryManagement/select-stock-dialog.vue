<template>
  <v-dialog v-model="visible" max-width="1120" persistent>
    <v-card>
      <v-toolbar color="white" density="compact" title="选择库存">
        <template #append>
          <v-btn icon="mdi-close" variant="text" aria-label="关闭" @click="closeDialog"></v-btn>
        </template>
      </v-toolbar>
      <v-divider></v-divider>
      <v-card-text>
        <div class="stock-dialog-summary">
          <span>装箱任务号：{{ task?.packing_task_sn || '-' }}</span>
          <span>商品：{{ item?.commodity_name || '-' }}</span>
          <span>SKU：{{ item?.commodity_sku || item?.sku || '-' }}</span>
          <span>仓库：{{ task?.warehouse_name || '-' }}</span>
        </div>
        <vxe-table
          ref="xTable"
          :data="stockRows"
          :column-config="{ minWidth: '110px' }"
          :loading="loading"
          align="center"
          height="420"
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
          <vxe-column field="goods_owner_name" title="所属人" min-width="120"></vxe-column>
          <vxe-column field="qty" title="库存量" width="90"></vxe-column>
          <vxe-column field="available_qty" title="可用量" width="90"></vxe-column>
          <vxe-column title="状态" width="100">
            <template #default="{ row }">
              <v-chip v-if="row.selected" size="small" color="success" variant="tonal">已选择</v-chip>
              <v-chip v-else-if="row.matched" size="small" color="primary" variant="tonal">匹配</v-chip>
              <v-chip v-else size="small" variant="tonal">其它</v-chip>
            </template>
          </vxe-column>
          <vxe-column title="操作" width="110" fixed="right">
            <template #default="{ row }">
              <v-btn
                size="small"
                color="primary"
                variant="tonal"
                :disabled="row.selected || row.available_qty <= 0"
                :loading="selectingStockId === row.stock_id"
                @click="method.selectStock(row)"
              >
                {{ row.selected ? '已选择' : '选择' }}
              </v-btn>
            </template>
          </vxe-column>
        </vxe-table>
        <div class="stock-dialog-footer">
          <span>共 {{ total }} 条库存，匹配当前SKU（忽略变体后缀）的优先展示</span>
          <v-btn
            size="small"
            variant="text"
            :disabled="loading || stockRows.length >= total"
            @click="method.loadMore"
          >
            查看更多
          </v-btn>
        </div>
      </v-card-text>
    </v-card>
  </v-dialog>
</template>

<script lang="ts" setup>
import { reactive, ref } from 'vue'
import { getPackingTaskSelectableStock, selectPackingTaskStock } from '@/api/wms/dispatchWorkflow'
import { hookComponent } from '@/components/system'
import ProductImage from '@/components/system/product-image.vue'
import type { PackingTaskItemVO, PackingTaskVO, SelectableStockVO } from '@/types/DeliveryManagement/PackingTask'

const PAGE_SIZE = 20

const visible = ref(false)
const loading = ref(false)
const task = ref<PackingTaskVO | null>(null)
const item = ref<PackingTaskItemVO | null>(null)
const stockRows = ref<SelectableStockVO[]>([])
const total = ref(0)
const pageIndex = ref(1)
const selectingStockId = ref<number | null>(null)

const method = reactive({
  loadPage: async () => {
    if (!item.value || !task.value) return
    loading.value = true
    try {
      const result = await getPackingTaskSelectableStock({
        sellfox_task_id: task.value.sellfox_task_id,
        sellfox_item_id: item.value.sellfox_item_id,
        page_index: pageIndex.value,
        page_size: PAGE_SIZE
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
  loadMore: () => {
    pageIndex.value += 1
    method.loadPage()
  },
  selectStock: async (row: SelectableStockVO) => {
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
      stockRows.value = stockRows.value.map((t) =>
        t.stock_id === row.stock_id ? { ...t, selected: true } : t
      )
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
  total.value = 0
  pageIndex.value = 1
  selectingStockId.value = null
  visible.value = true
  method.loadPage()
}

const closeDialog = (): void => {
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

.stock-dialog-footer {
  display: flex;
  align-items: center;
  justify-content: space-between;
  margin-top: 10px;
  color: rgba(var(--v-theme-on-surface), 0.6);
  font-size: 12px;
}
</style>
