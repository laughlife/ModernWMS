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
        <v-table density="compact" class="task-summary-table">
          <thead>
            <tr>
              <th class="summary-image-column">图片</th>
              <th>商品信息</th>
              <th>FNSKU / MSKU</th>
              <th>装箱信息</th>
              <th>任务量</th>
              <th>仓库</th>
            </tr>
          </thead>
          <tbody>
            <tr>
              <td class="summary-image-cell">
                <ProductImage
                  :src="item?.main_image || ''"
                  :alt="item?.commodity_name || item?.commodity_sku || ''"
                  :width="52"
                  :height="52"
                  :cover="false"
                />
              </td>
              <td>
                <div class="merged-cell">
                  <div class="merged-primary">{{ item?.commodity_name || '-' }}</div>
                  <div class="merged-secondary">SKU：{{ item?.commodity_sku || item?.sku || '-' }}</div>
                </div>
              </td>
              <td>
                <div class="merged-cell">
                  <div class="merged-primary">{{ item?.fn_sku || '-' }}</div>
                  <div class="merged-secondary">{{ item?.msku || '-' }}</div>
                </div>
              </td>
              <td>
                <div class="merged-cell">
                  <div class="merged-primary">装箱任务号：{{ task?.packing_task_sn || '-' }}</div>
                  <div class="merged-secondary">
                    店铺：{{ task?.shop_name || '-' }}　站点：{{ task?.marketplace_name || '-' }}　创建人：{{ task?.create_name || '-' }}
                  </div>
                </div>
              </td>
              <td>{{ taskQty }}</td>
              <td>{{ task?.warehouse_name || '-' }}</td>
            </tr>
          </tbody>
        </v-table>

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
              <ProductImage :src="row.main_image" :alt="row.commodity_name || row.sku_code" :width="48" :height="48" :cover="false" />
            </template>
          </vxe-column>
          <vxe-column title="商品信息" min-width="200" align="left" header-align="left">
            <template #default="{ row }">
              <div class="merged-cell">
                <div class="merged-primary">{{ row.commodity_name || '-' }}</div>
                <div class="merged-secondary">SKU：{{ row.sku_code || '-' }}</div>
              </div>
            </template>
          </vxe-column>
          <vxe-column title="库位/所属人" min-width="160" align="left" header-align="left">
            <template #default="{ row }">
              <div class="merged-cell">
                <div class="merged-primary">{{ row.location_name || '-' }}</div>
                <div class="merged-secondary">{{ row.goods_owner_name || '-' }}</div>
              </div>
            </template>
          </vxe-column>
          <vxe-column title="库存量/可用量" width="140">
            <template #default="{ row }">{{ row.qty }} / {{ row.available_qty }}</template>
          </vxe-column>
          <vxe-column title="任务量" width="90">
            <template #default>{{ taskQty }}</template>
          </vxe-column>
          <vxe-column title="变体" width="130">
            <template #default="{ row }">
              <div class="variant-cell">
                <v-text-field
                  v-model.number="row.variant"
                  type="number"
                  min="1"
                  step="1"
                  density="compact"
                  variant="outlined"
                  hide-details
                  class="variant-input"
                />
                <div class="variant-lock">锁定 {{ computeLockedQty(taskQty, row.variant) }}</div>
              </div>
            </template>
          </vxe-column>
          <vxe-column title="状态" width="100">
            <template #default="{ row }">
              <v-chip v-if="row.selected" size="small" color="success" variant="tonal">已选择</v-chip>
              <v-chip v-else-if="row.matched" size="small" color="primary" variant="tonal">匹配</v-chip>
              <v-chip v-else-if="row.is_creator_stock" size="small" color="info" variant="tonal">创建人</v-chip>
              <v-chip v-else size="small" variant="tonal">其它</v-chip>
            </template>
          </vxe-column>
          <vxe-column title="操作" width="190" fixed="right">
            <template #default="{ row }">
              <v-btn
                v-if="row.selected"
                size="small"
                color="primary"
                variant="tonal"
                class="mr-1"
                :disabled="loading"
                :loading="selectingStockId === stockIdentity(row)"
                @click="method.selectStock(row)"
              >
                更新
              </v-btn>
              <v-btn
                v-if="row.selected"
                size="small"
                color="error"
                variant="tonal"
                :disabled="loading"
                :loading="selectingStockId === stockIdentity(row)"
                @click="method.unselectStock(row)"
              >
                取消选择
              </v-btn>
              <v-btn
                v-else
                size="small"
                color="primary"
                variant="tonal"
                :disabled="loading"
                :loading="selectingStockId === stockIdentity(row)"
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
import {
  beginPackingTaskSkuMismatchChallenge,
  getPackingTaskSelectableStock,
  selectPackingTaskStock,
  deletePackingTaskStockSelection
} from '@/api/wms/dispatchWorkflow'
import { hookComponent } from '@/components/system'
import ProductImage from '@/components/system/product-image.vue'
import type { PackingTaskItemVO, PackingTaskVO, SelectableStockVO } from '@/types/DeliveryManagement/PackingTask'
import {
  computeLockedQty,
  deriveVariant,
  validatePackingStockSelection
} from './packingTaskSelection'

const PAGE_SIZE = 20

type StockRow = SelectableStockVO & { variant: number }

const emit = defineEmits<{ changed: [selected: SelectableStockVO[]] }>()

const visible = ref(false)
const loading = ref(false)
const task = ref<PackingTaskVO | null>(null)
const item = ref<PackingTaskItemVO | null>(null)
const stockRows = ref<StockRow[]>([])
const selectedRows = ref<StockRow[]>([])
const total = ref(0)
const pageIndex = ref(1)
const selectingStockId = ref<string | null>(null)
const commandLease = reactive<Record<string, { signature: string; requestId: string }>>({})
const stockIdentity = (row: Pick<SelectableStockVO, 'stock_id' | 'stock_allocation_id'>): string =>
  row.stock_allocation_id ? `allocation:${row.stock_allocation_id}` : `legacy:${row.stock_id}`
const searchForm = reactive({ keyword: '', location: '', owner: '' })
const searching = ref(false)
const requestIdFor = (operation: string, row: StockRow, signature: string) => {
  const key = `${operation}:${stockIdentity(row)}`
  const existing = commandLease[key]
  if (existing?.signature === signature) return existing.requestId
  const requestId = globalThis.crypto?.randomUUID?.() ?? `${operation}-${Date.now()}-${Math.random().toString(16).slice(2)}`
  commandLease[key] = { signature, requestId }
  return requestId
}
const clearRequestId = (operation: string, row: StockRow) => delete commandLease[`${operation}:${stockIdentity(row)}`]
const contributionRequestId = (row: StockRow, skuMismatchConfirmed: boolean): string => {
  const variant = row.variant
  const lockedQty = computeLockedQty(taskQty.value, variant)
  return requestIdFor('contribution', row,
    `${row.row_version}:${row.goods_owner_id}:${lockedQty}:${variant}:${skuMismatchConfirmed}`)
}

// 装箱任务量（箱数），锁定数量 = 装箱任务量 × 变体数量。
const taskQty = computed(() => item.value?.task_num ?? 1)

// 商品 SKU 第一个 “-” 后的内容可能是 2、2-pcs 等形式，只取其中首段阿拉伯数字。
const extractSkuVariant = (sku: string | null | undefined): number | null => {
  const normalizedSku = String(sku ?? '').trim()
  const separatorIndex = normalizedSku.indexOf('-')
  if (separatorIndex < 0) return null
  const matched = normalizedSku.slice(separatorIndex + 1).match(/\d+/)
  return matched ? Number(matched[0]) : null
}

// 列表行附带可手动维护的变体数。已选择的行按“已锁定数量 ÷ 任务量”回显变体数，
// 未选择的行默认 1 变体。
const toRow = (r: SelectableStockVO): StockRow => ({
  ...r,
  variant: deriveVariant(taskQty.value, r.selected_qty, r.selected)
})

const footerText = computed(() => searching.value
  ? `搜索其他库存，共 ${total.value} 条匹配记录`
  : `当前展示创建人（${task.value?.create_name || '-'}）的库存，共 ${total.value} 条；搜索可查看其他库存`)

// 已选择的库存行显示绿色背景。
const rowClassName = ({ row }: { row: StockRow }): string => row.selected ? 'selected-row' : ''

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
        stockRows.value = result.data.rows.map(toRow)
      } else {
        stockRows.value = [...stockRows.value, ...result.data.rows.map(toRow)]
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
  selectStock: (row: StockRow) => {
    if (!row.matched) {
      hookComponent.$message({ type: 'warning', content: 'SKU 不匹配，请在 3 秒提示后确认是否继续贡献库存' })
      window.setTimeout(async () => {
        if (!item.value || !task.value) return
        try {
          const variant = row.variant
          const qty = computeLockedQty(taskQty.value, variant)
          const requestId = contributionRequestId(row, true)
          const challenge = await beginPackingTaskSkuMismatchChallenge({
            sellfox_task_id: task.value.sellfox_task_id,
            sellfox_item_id: item.value.sellfox_item_id,
            stock_id: row.stock_id,
            goods_owner_id: row.goods_owner_id,
            qty,
            variant,
            request_id: requestId
          })
          if (!challenge.isSuccess) {
            hookComponent.$message({ type: 'error', content: challenge.errorMessage })
            return
          }
          hookComponent.$dialog({
            content: 'SKU 不匹配已提示 3 秒。确认继续将作为人工确认事实提交 ERP。',
            confirmText: '确认继续',
            cancleText: '取消',
            handleConfirm: () => method.checkStockOwner(row, true, challenge.data)
          })
        } catch (error) {
          hookComponent.$message({ type: 'error', content: error instanceof Error ? error.message : String(error) })
        }
      }, 3000)
      return
    }
    const skuVariant = extractSkuVariant(item.value?.commodity_sku || item.value?.sku)
    if (skuVariant !== null && skuVariant !== Number(row.variant)) {
      hookComponent.$dialog({
        content: '所选的变体和商品信息变体数量不一致，是否继续执行？',
        confirmText: '是',
        cancleText: '否',
        handleConfirm: () => method.checkStockOwner(row, false)
      })
      return
    }
    method.checkStockOwner(row, false)
  },
  checkStockOwner: (row: StockRow, skuMismatchConfirmed: boolean, skuMismatchChallenge = '') => {
    if (row.is_creator_stock) {
      method.confirmSelectStock(row, skuMismatchConfirmed, skuMismatchChallenge)
      return
    }
    hookComponent.$dialog({
      content: '所选库存不是创建人的商品，是否继续执行',
      confirmText: '是',
      cancleText: '否',
      handleConfirm: () => method.confirmSelectStock(row, skuMismatchConfirmed, skuMismatchChallenge)
    })
  },
  confirmSelectStock: async (row: StockRow, skuMismatchConfirmed = false, skuMismatchChallenge = '') => {
    if (!item.value || !task.value) return
    const variant = row.variant
    const validation = validatePackingStockSelection(row, taskQty.value, variant)
    if (!validation.ok && validation.reason === 'INVALID_VARIANT') {
      hookComponent.$message({ type: 'warning', content: '请输入大于0的变体数量' })
      return
    }
    if (!validation.ok) {
      hookComponent.$message({ type: 'warning', content: '可用量不足' })
      return
    }
    // 锁定数量 = 装箱任务量 × 变体数量。
    const lockedQty = computeLockedQty(taskQty.value, variant)
    const requestId = contributionRequestId(row, skuMismatchConfirmed)
    selectingStockId.value = stockIdentity(row)
    try {
      const result = await selectPackingTaskStock({
        sellfox_task_id: task.value.sellfox_task_id,
        sellfox_item_id: item.value.sellfox_item_id,
        stock_id: row.stock_id,
        erp_stock_id: row.erp_stock_id,
        stock_allocation_id: row.stock_allocation_id,
        qty: lockedQty,
        variant,
        row_version: row.row_version,
        request_id: requestId,
        goods_owner_id: row.goods_owner_id,
        sku_mismatch_confirmed: skuMismatchConfirmed,
        sku_mismatch_challenge: skuMismatchChallenge
      })
      if (!result.isSuccess) {
        hookComponent.$message({ type: 'error', content: result.errorMessage })
        return
      }
      clearRequestId('contribution', row)
      hookComponent.$message({ type: 'success', content: '库存选择成功' })
      const selected = {
        ...row,
        selected: true,
        selected_qty: lockedQty,
        available_qty: Math.max(0, row.available_qty + (row.selected_qty ?? 0) - lockedQty)
      }
      stockRows.value = stockRows.value.map((t) =>
        stockIdentity(t) === stockIdentity(row) ? selected : t
      )
      selectedRows.value = [
        ...selectedRows.value.filter((t) => stockIdentity(t) !== stockIdentity(row)),
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
  unselectStock: async (row: StockRow) => {
    if (!item.value || !task.value) return
    selectingStockId.value = stockIdentity(row)
    const requestId = requestIdFor('withdraw', row, `${row.row_version}:${row.goods_owner_id}`)
    try {
      const result = await deletePackingTaskStockSelection({
        sellfox_task_id: task.value.sellfox_task_id,
        sellfox_item_id: item.value.sellfox_item_id,
        stock_id: row.stock_id,
        erp_stock_id: row.erp_stock_id,
        stock_allocation_id: row.stock_allocation_id,
        qty: 0,
        row_version: row.row_version,
        request_id: requestId,
        goods_owner_id: row.goods_owner_id,
        sku_mismatch_confirmed: false
      })
      if (!result.isSuccess) {
        hookComponent.$message({ type: 'error', content: result.errorMessage })
        return
      }
      clearRequestId('withdraw', row)
      hookComponent.$message({ type: 'success', content: '已取消选择，锁定库存已释放' })
      selectedRows.value = selectedRows.value.filter((t) => stockIdentity(t) !== stockIdentity(row))
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
.task-summary-table {
  margin-bottom: 12px;
  border: 1px solid rgba(var(--v-border-color), var(--v-border-opacity));
  border-radius: 6px;
  overflow: hidden;
}

.task-summary-table :deep(th) {
  height: 40px !important;
  background: rgba(var(--v-theme-primary), 0.06);
  color: rgba(var(--v-theme-on-surface), 0.7);
  font-weight: 600 !important;
  white-space: nowrap;
}

.task-summary-table :deep(td) {
  height: 72px !important;
  vertical-align: middle;
}

.summary-image-column {
  width: 78px;
}

.summary-image-cell {
  width: 78px;
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

.merged-cell {
  display: flex;
  flex-direction: column;
  gap: 2px;
  line-height: 1.4;
}

.merged-primary {
  color: rgba(var(--v-theme-on-surface), 0.87);
}

.merged-secondary {
  color: rgba(var(--v-theme-on-surface), 0.6);
  font-size: 12px;
}

.variant-input {
  max-width: 80px;
  margin: 0 auto;
}

.variant-cell {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 2px;
}

.variant-lock {
  color: rgba(var(--v-theme-on-surface), 0.55);
  font-size: 11px;
  line-height: 1.2;
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
