<template>
  <div class="packing-editor">
    <v-progress-linear v-if="loading" indeterminate color="primary" />
    <v-alert v-else-if="errorMessage" type="error" variant="tonal" density="compact">{{ errorMessage }}</v-alert>
    <template v-else-if="plan">
      <div class="editor-heading">
        <strong>装箱任务：{{ plan.packing_task_no }}</strong>
        <v-chip size="small" :color="plan.packing_plan_status === 'DRAFT' ? 'warning' : 'success'" variant="tonal">
          {{ plan.packing_plan_status === 'DRAFT' ? '装箱中' : '装箱完成' }}
        </v-chip>
      </div>
      <v-table density="compact" class="product-pool">
        <thead><tr><th>图片</th><th>商品信息</th><th>FNSKU / MSKU</th><th>变体</th><th>任务量</th><th>商品需求量</th><th>已分配/剩余</th></tr></thead>
        <tbody>
          <tr v-for="item in plan.items" :key="item.id">
            <td><ProductImage :src="item.main_image" :alt="item.commodity_name" :width="48" :height="48" :cover="false" /></td>
            <td class="text-left"><div>{{ item.commodity_name || '-' }}</div><small>SKU：{{ item.commodity_sku || '-' }}</small></td>
            <td><div>{{ item.fn_sku || '-' }}</div><small>{{ item.msku || '-' }}</small></td>
            <td>{{ item.variant_qty }}</td><td>{{ itemTaskLimit(plan, item) }}</td><td>{{ itemTaskLimit(plan, item) * item.variant_qty }}</td>
            <td>{{ allocatedTaskQty(plan, item.id) }} / {{ remainingTaskQty(plan, item) }}</td>
          </tr>
        </tbody>
      </v-table>

      <div class="box-toolbar"><v-btn size="small" color="primary" prepend-icon="mdi-package-variant" :disabled="!editable" @click="addEmptyBox">新增箱</v-btn></div>
      <v-card v-for="(box, boxIndex) in plan.boxes" :key="box.client_key" variant="outlined" class="box-card">
        <v-card-title class="box-title"><span>第 {{ boxIndex + 1 }} 箱</span><v-btn icon="mdi-delete-outline" size="small" color="error" variant="text" :disabled="!editable" @click="removeBox(boxIndex)" /></v-card-title>
        <v-card-text>
          <div class="measurement-grid">
            <v-text-field v-model.number="box.weight" type="number" min="0" label="重量(kg)" density="compact" hide-details :disabled="!editable" />
            <v-text-field v-model.number="box.length" type="number" min="0" label="长(cm)" density="compact" hide-details :disabled="!editable" />
            <v-text-field v-model.number="box.width" type="number" min="0" label="宽(cm)" density="compact" hide-details :disabled="!editable" />
            <v-text-field v-model.number="box.height" type="number" min="0" label="高(cm)" density="compact" hide-details :disabled="!editable" />
          </div>
          <div class="box-item-header">
            <span>商品信息</span><span>任务量</span><span>变体</span><span>商品需求量</span><span>操作</span>
          </div>
          <div v-for="(boxItem, itemIndex) in box.items" :key="boxItem.packing_task_item_id" class="box-item-row">
            <span>
              <span>{{ product(boxItem.packing_task_item_id)?.commodity_name || '-' }}</span>
              <small>SKU：{{ product(boxItem.packing_task_item_id)?.commodity_sku || '-' }}</small>
            </span>
            <v-text-field v-model.number="boxItem.task_qty" type="number" min="0" label="任务量" density="compact" hide-details :disabled="!editable" />
            <span>{{ product(boxItem.packing_task_item_id)?.variant_qty || 0 }}</span>
            <span>{{ Number(boxItem.task_qty || 0) * Number(product(boxItem.packing_task_item_id)?.variant_qty || 0) }}</span>
            <v-btn icon="mdi-close" size="x-small" variant="text" :disabled="!editable" @click="box.items[itemIndex].task_qty = 0" />
          </div>
          <div class="add-product-row">
            <v-select v-model="selectedProduct[box.client_key]" :items="availableProducts(box)" item-title="commodity_name" item-value="id" label="添加同任务商品" density="compact" hide-details :disabled="!editable" />
            <v-btn size="small" :disabled="!editable || !selectedProduct[box.client_key]" @click="addProduct(box)">添加</v-btn>
            <v-btn size="small" prepend-icon="mdi-content-copy" :disabled="!editable" @click="copyBox(box, boxIndex)">复制</v-btn>
          </div>
        </v-card-text>
      </v-card>
      <div class="editor-actions">
        <v-btn :loading="saving" :disabled="!editable" @click="savePacking">保存</v-btn>
        <v-btn color="primary" :loading="completing" :disabled="!editable || !canConfirmPackingPlan(plan)" @click="completePacking">确定装箱完成</v-btn>
      </div>
    </template>
  </div>
</template>

<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { completeDispatchOrderWeighing, completeDispatchTaskWeighing, confirmDispatchActualPacking, getDispatchPackingPlan, saveDispatchPackingPlan } from '@/api/wms/dispatchWorkflow'
import { hookComponent } from '@/components/system'
import ProductImage from '@/components/system/product-image.vue'
import type { PackingPlan, PackingPlanBox } from '@/types/DeliveryManagement/DispatchWorkflow'
import { allocatedTaskQty, canConfirmPackingPlan, copyDraftBox, itemTaskLimit, newDraftBox, releasedRequiredQty, remainingTaskQty } from './packingPlanPolicy'

const props = defineProps<{ orderId: number; packingTaskId: number; frozen?: boolean }>()
const emit = defineEmits<{ saved: []; completed: [] }>()
const plan = ref<PackingPlan | null>(null)
const loading = ref(false); const saving = ref(false); const completing = ref(false)
const errorMessage = ref(''); const selectedProduct = ref<Record<string, number | null>>({})
const editable = computed(() => !props.frozen && plan.value?.packing_plan_status === 'DRAFT')
const requestId = () => globalThis.crypto?.randomUUID?.() ?? `packing-${Date.now()}-${Math.random().toString(16).slice(2)}`
const product = (id: number) => plan.value?.items.find((item) => item.id === id)
const fillBoxProductRows = (packingPlan: PackingPlan, initializeFirstBox = false) => {
  if (initializeFirstBox && packingPlan.packing_plan_status === 'DRAFT' && packingPlan.boxes.length === 0) {
    packingPlan.boxes.push(newDraftBox(1))
  }
  const fillFirstBox = initializeFirstBox && packingPlan.boxes.length === 1 && packingPlan.boxes[0].items.length === 0
  packingPlan.boxes.forEach((box) => {
    const currentItems = new Map(box.items.map((item) => [item.packing_task_item_id, item]))
    box.items = packingPlan.items.map((item) => currentItems.get(item.id) ?? {
      packing_task_item_id: item.id,
      task_qty: fillFirstBox ? itemTaskLimit(packingPlan, item) : 0
    })
  })
  plan.value = packingPlan
}
const boxesForSave = () => plan.value?.boxes.map((box) => ({
  ...box,
  items: box.items.filter((item) => Number(item.task_qty) > 0)
})) ?? []
const load = async () => { loading.value = true; errorMessage.value = ''; try { const result = await getDispatchPackingPlan(props.orderId, props.packingTaskId, true); if (!result.isSuccess) throw new Error(result.errorMessage); fillBoxProductRows(result.data, true) } catch (error) { errorMessage.value = error instanceof Error ? error.message : String(error) } finally { loading.value = false } }
const addEmptyBox = () => {
  if (!plan.value) return
  const box = newDraftBox(plan.value.boxes.length + 1)
  box.items = plan.value.items.map((item) => ({ packing_task_item_id: item.id, task_qty: remainingTaskQty(plan.value!, item) }))
  plan.value.boxes.push(box)
}
const removeBox = (index: number) => plan.value?.boxes.splice(index, 1)
const copyBox = (box: PackingPlanBox, index: number) => {
  if (!plan.value) return
  plan.value.boxes.splice(index + 1, 0, copyDraftBox(box, index + 2))
  plan.value.boxes.forEach((entry, boxIndex) => { entry.box_sequence = boxIndex + 1 })
  hookComponent.$message({ type: 'info', content: `已复制为第 ${index + 2} 箱，请按实际装箱数量调整后保存` })
}
const availableProducts = (box: PackingPlanBox) => plan.value?.items.filter((item) => Number(box.items.find((entry) => entry.packing_task_item_id === item.id)?.task_qty || 0) === 0 && remainingTaskQty(plan.value!, item) > 0) ?? []
const addProduct = (box: PackingPlanBox) => { const id = selectedProduct.value[box.client_key]; const item = id ? product(id) : undefined; if (!plan.value || !item) return; const boxItem = box.items.find((entry) => entry.packing_task_item_id === item.id); if (boxItem) boxItem.task_qty = remainingTaskQty(plan.value, item); else box.items.push({ packing_task_item_id: item.id, task_qty: remainingTaskQty(plan.value, item) }); selectedProduct.value[box.client_key] = null }
const savePacking = async () => { if (!plan.value) return; saving.value = true; try { const result = await saveDispatchPackingPlan(props.orderId, props.packingTaskId, { request_id: requestId(), row_version: plan.value.row_version, task_row_version: plan.value.task_row_version, boxes: boxesForSave() }); if (!result.isSuccess) throw new Error(result.errorMessage); fillBoxProductRows(result.data); hookComponent.$message({ type: 'success', content: '装箱信息已保存，确定装箱完成前可继续修改或删除' }); emit('saved') } catch (error) { hookComponent.$message({ type: 'error', content: error instanceof Error ? error.message : String(error) }) } finally { saving.value = false } }
const completePacking = () => { if (!plan.value) return; const leftovers = plan.value.items.filter((item) => remainingTaskQty(plan.value!, item) > 0).map((item) => `${item.commodity_name}：未装任务量 ${remainingTaskQty(plan.value!, item)}，释放库存 ${releasedRequiredQty(plan.value!, item)} 件`); hookComponent.$dialog({ content: leftovers.length ? `确定装箱完成后将不能再修改或删除。以下余量会解除锁定：${leftovers.join('；')}` : '确定装箱完成后将不能再修改或删除，是否继续？', handleConfirm: async () => { if (!plan.value) return; completing.value = true; try { const saved = await saveDispatchPackingPlan(props.orderId, props.packingTaskId, { request_id: requestId(), row_version: plan.value.row_version, task_row_version: plan.value.task_row_version, boxes: boxesForSave() }); if (!saved.isSuccess) throw new Error(saved.errorMessage); fillBoxProductRows(saved.data); const confirmed = await confirmDispatchActualPacking(props.orderId, props.packingTaskId, { request_id: requestId(), row_version: plan.value!.row_version, task_row_version: plan.value!.task_row_version }); if (!confirmed.isSuccess) throw new Error(confirmed.errorMessage); plan.value = confirmed.data; const taskResult = await completeDispatchTaskWeighing(props.orderId, props.packingTaskId, { request_id: requestId(), row_version: plan.value.row_version }); if (!taskResult.isSuccess) throw new Error(taskResult.errorMessage); const orderResult = await completeDispatchOrderWeighing(props.orderId, { request_id: requestId(), row_version: taskResult.data.row_version }); hookComponent.$message({ type: 'success', content: orderResult.isSuccess ? '装箱及称重已完成，已进入待出库' : '当前装箱任务已完成，其他装箱任务完成后进入待出库' }); emit('completed') } catch (error) { hookComponent.$message({ type: 'error', content: error instanceof Error ? error.message : String(error) }) } finally { completing.value = false } } }) }
onMounted(load)
</script>

<style scoped lang="less">
.packing-editor { padding: 4px; background: rgb(var(--v-theme-surface)); }
.editor-heading,.box-title,.editor-actions,.box-toolbar,.add-product-row,.box-item-row { display: flex; align-items: center; gap: 12px; }
.editor-heading,.box-title { justify-content: space-between; }.product-pool { margin-top: 10px; }.box-toolbar { margin: 14px 0; }
.box-card { background: rgb(var(--v-theme-surface)); }.box-card + .box-card { margin-top: 12px; }.measurement-grid { display: grid; grid-template-columns: repeat(4, minmax(120px,1fr)); gap: 10px; }
.box-item-header,.box-item-row { display: grid; grid-template-columns: minmax(180px,2fr) minmax(100px,1fr) minmax(70px,.7fr) minmax(110px,1fr) 48px; align-items: center; gap: 12px; }
.box-item-header { margin-top: 16px; padding: 8px 0; border-bottom: 1px solid rgba(var(--v-border-color),var(--v-border-opacity)); font-weight: 600; }
.box-item-row { margin-top: 10px; }.box-item-row small { display: block; }.add-product-row { margin-top: 12px; max-width: 650px; }.editor-actions { justify-content: flex-end; margin-top: 16px; }
small { opacity: .65; }
</style>
