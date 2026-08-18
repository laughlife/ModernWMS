<template>
  <div class="packing-editor">
    <v-progress-linear v-if="loading" indeterminate color="primary" />
    <v-alert v-else-if="errorMessage" type="error" variant="tonal" density="compact">{{ errorMessage }}</v-alert>
    <template v-else-if="plan">
      <div class="editor-heading">
        <strong>装箱任务：{{ plan.packing_task_no }}</strong>
        <v-chip size="small" :color="packingPlanStatus === 'DRAFT' ? 'warning' : 'success'" variant="tonal">
          {{ packingPlanStatus === 'DRAFT' ? '装箱中' : packingPlanStatus === 'PACKING_CONFIRMED' ? '已确认装箱完成' : '业务已确认' }}
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
      <section class="packing-progress">
        <div class="packing-progress-title">装箱进度</div>
        <v-table density="compact">
          <thead><tr><th>商品信息</th><th>已装箱信息</th><th>未装箱信息</th></tr></thead>
          <tbody>
            <tr v-for="item in plan.items" :key="`progress-${item.id}`">
              <td>
                <div class="progress-product">
                  <ProductImage :src="item.main_image" :alt="item.commodity_name" :width="44" :height="44" :cover="false" />
                  <div>
                    <div>{{ item.commodity_name || '-' }}</div>
                    <small>SKU：{{ item.commodity_sku || '-' }}</small>
                    <small>总任务量：{{ itemTaskLimit(plan, item) }}　变体：{{ item.variant_qty }}</small>
                  </div>
                </div>
              </td>
              <td class="packed-summary">
                <div>任务量：{{ allocatedTaskQty(plan, item.id) }}</div>
                <small>商品数量：{{ allocatedTaskQty(plan, item.id) * item.variant_qty }}</small>
              </td>
              <td class="remaining-summary">
                <div>剩余任务量：{{ remainingTaskQty(plan, item) }}</div>
                <small>剩余商品数量：{{ remainingTaskQty(plan, item) * item.variant_qty }}</small>
              </td>
            </tr>
          </tbody>
        </v-table>
      </section>
      <v-card v-for="(box, boxIndex) in plan.boxes" :key="box.client_key" variant="outlined" class="box-card">
        <v-card-title class="box-title">
          <span>第 {{ boxIndex + 1 }} 箱</span>
          <div class="box-actions">
            <v-btn size="small" prepend-icon="mdi-content-copy" variant="tonal" :disabled="!editable" @click="copyBox(box, boxIndex)">复制</v-btn>
            <v-btn icon="mdi-delete-outline" size="small" color="error" variant="text" :disabled="!editable" @click="removeBox(boxIndex)" />
          </div>
        </v-card-title>
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
        </v-card-text>
      </v-card>
      <div class="editor-footer">
        <div class="completion-status">
          <strong>装箱信息可随时保存</strong>
          <small v-if="completionHint" :title="completionHint">业务下一步还需：{{ completionHint }}</small>
          <small v-else>信息已填写完整，已具备业务下一步校验条件</small>
        </div>
        <div class="editor-actions">
          <v-btn :loading="saving" :disabled="!editable" @click="savePacking">保存</v-btn>
          <v-btn color="primary" :loading="completing" :disabled="!editable || packingPlanStatus === 'PACKING_CONFIRMED'" @click="completePacking">
            {{ packingPlanStatus === 'PACKING_CONFIRMED' ? '已确认装箱完成' : '确定装箱完成' }}
          </v-btn>
          <v-btn color="success" :loading="checking" :disabled="!editable || packingPlanStatus !== 'PACKING_CONFIRMED'" @click="checkPacking">
            检测装箱
          </v-btn>
        </div>
      </div>
    </template>
  </div>
</template>

<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { completeDispatchOrderWeighing, completeDispatchTaskWeighing, confirmDispatchActualPacking, confirmDispatchPacking, getDispatchPackingPlan, saveDispatchPackingPlan } from '@/api/wms/dispatchWorkflow'
import { hookComponent } from '@/components/system'
import ProductImage from '@/components/system/product-image.vue'
import type { PackingPlan, PackingPlanBox } from '@/types/DeliveryManagement/DispatchWorkflow'
import { allocatedTaskQty, copyDraftBox, itemTaskLimit, newDraftBox, remainingTaskQty } from './packingPlanPolicy'

const props = defineProps<{ orderId: number; packingTaskId: number; frozen?: boolean; autoCheck?: boolean }>()
const emit = defineEmits<{ saved: []; completed: [] }>()
const plan = ref<PackingPlan | null>(null)
const loading = ref(false); const saving = ref(false); const completing = ref(false); const checking = ref(false)
const errorMessage = ref('')
const packingPlanStatus = computed(() => String(plan.value?.packing_plan_status ?? ''))
const editable = computed(() => !props.frozen && (packingPlanStatus.value === 'DRAFT' || packingPlanStatus.value === 'PACKING_CONFIRMED'))
const completionHint = computed(() => {
  if (!plan.value) return ''
  if (plan.value.boxes.length === 0) return '至少建立一个箱子'
  const issues: string[] = []
  plan.value.boxes.forEach((box, index) => {
    const missing: string[] = []
    if (Number(box.weight) <= 0) missing.push('重量')
    if (Number(box.length) <= 0) missing.push('长')
    if (Number(box.width) <= 0) missing.push('宽')
    if (Number(box.height) <= 0) missing.push('高')
    if (!box.items.some((item) => Number(item.task_qty) > 0)) missing.push('商品任务量')
    if (box.items.some((item) => !Number.isInteger(Number(item.task_qty)) || Number(item.task_qty) < 0)) missing.push('有效整数任务量')
    if (missing.length) issues.push(`第${index + 1}箱缺少${missing.join('、')}`)
  })
  plan.value.items.forEach((item) => {
    if (allocatedTaskQty(plan.value!, item.id) > itemTaskLimit(plan.value!, item)) issues.push(`${item.commodity_name}任务量超限`)
  })
  return issues.join('；')
})
const requestId = () => globalThis.crypto?.randomUUID?.() ?? `packing-${Date.now()}-${Math.random().toString(16).slice(2)}`
const product = (id: number) => plan.value?.items.find((item) => item.id === id)
const fillBoxProductRows = (packingPlan: PackingPlan, initializeFirstBox = false) => {
  if (initializeFirstBox && (packingPlan.packing_plan_status === 'DRAFT' || String(packingPlan.packing_plan_status) === 'PACKING_CONFIRMED') && packingPlan.boxes.length === 0) {
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
const load = async () => { loading.value = true; errorMessage.value = ''; try { const result = await getDispatchPackingPlan(props.orderId, props.packingTaskId, true); if (!result.isSuccess) throw new Error(result.errorMessage); fillBoxProductRows(result.data, true); if (props.autoCheck) checkPacking() } catch (error) { errorMessage.value = error instanceof Error ? error.message : String(error) } finally { loading.value = false } }
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
const savePacking = async () => { if (!plan.value) return; saving.value = true; try { const result = await saveDispatchPackingPlan(props.orderId, props.packingTaskId, { request_id: requestId(), row_version: plan.value.row_version, task_row_version: plan.value.task_row_version, boxes: boxesForSave() }); if (!result.isSuccess) throw new Error(result.errorMessage); fillBoxProductRows(result.data); hookComponent.$message({ type: 'success', content: '装箱信息已保存，可继续修改或删除' }); emit('saved') } catch (error) { hookComponent.$message({ type: 'error', content: error instanceof Error ? error.message : String(error) }) } finally { saving.value = false } }
const completePacking = () => { if (!plan.value) return; hookComponent.$dialog({ content: '本次仅记录人工确认装箱完成，不执行最终业务校验，也不会进入下一步；确认后仍可继续修改和保存，是否继续？', handleConfirm: async () => { if (!plan.value) return; completing.value = true; try { const saved = await saveDispatchPackingPlan(props.orderId, props.packingTaskId, { request_id: requestId(), row_version: plan.value.row_version, task_row_version: plan.value.task_row_version, boxes: boxesForSave() }); if (!saved.isSuccess) throw new Error(saved.errorMessage); fillBoxProductRows(saved.data); const confirmed = await confirmDispatchPacking(props.orderId, props.packingTaskId, { request_id: requestId(), row_version: plan.value!.row_version, task_row_version: plan.value!.task_row_version }); if (!confirmed.isSuccess) throw new Error(confirmed.errorMessage); fillBoxProductRows(confirmed.data); hookComponent.$message({ type: 'success', content: '已确认装箱完成，当前仍可继续修改和保存；业务下一步尚未执行' }); emit('saved') } catch (error) { hookComponent.$message({ type: 'error', content: error instanceof Error ? error.message : String(error) }) } finally { completing.value = false } } }) }
const advanceAfterPackingCheck = async () => {
  if (!plan.value) return
  checking.value = true
  try {
    const saved = await saveDispatchPackingPlan(props.orderId, props.packingTaskId, { request_id: requestId(), row_version: plan.value.row_version, task_row_version: plan.value.task_row_version, boxes: boxesForSave() })
    if (!saved.isSuccess) throw new Error(saved.errorMessage)
    fillBoxProductRows(saved.data)
    const confirmed = await confirmDispatchActualPacking(props.orderId, props.packingTaskId, { request_id: requestId(), row_version: plan.value!.row_version, task_row_version: plan.value!.task_row_version })
    if (!confirmed.isSuccess) throw new Error(confirmed.errorMessage)
    fillBoxProductRows(confirmed.data)
    const taskCompleted = await completeDispatchTaskWeighing(props.orderId, props.packingTaskId, { request_id: requestId(), row_version: plan.value!.row_version })
    if (!taskCompleted.isSuccess) throw new Error(taskCompleted.errorMessage)
    const orderCompleted = await completeDispatchOrderWeighing(props.orderId, { request_id: requestId(), row_version: taskCompleted.data.row_version })
    if (!orderCompleted.isSuccess) throw new Error(orderCompleted.errorMessage)
    hookComponent.$message({ type: 'success', content: '装箱检测通过，已进入待出库' })
    emit('completed')
  } catch (error) {
    hookComponent.$message({ type: 'error', content: error instanceof Error ? error.message : String(error) })
  } finally {
    checking.value = false
  }
}
const checkPacking = () => {
  if (!plan.value) return
  if (packingPlanStatus.value !== 'PACKING_CONFIRMED') {
    hookComponent.$message({ type: 'error', content: '请先确定装箱完成，再进行装箱检测' })
    return
  }
  if (completionHint.value) {
    hookComponent.$message({ type: 'error', content: `装箱检测未通过：${completionHint.value}` })
    return
  }
  const unfinishedProducts = plan.value.items
    .map((item) => ({ item, remaining: remainingTaskQty(plan.value!, item) }))
    .filter(({ remaining }) => remaining > 0)
    .map(({ item, remaining }) => `${item.commodity_name || item.commodity_sku || '未命名'}商品（剩余任务量${remaining}，剩余商品数量${remaining * item.variant_qty}）`)
  const content = unfinishedProducts.length > 0
    ? `任务中还有${unfinishedProducts.join('、')}没有完成装箱，是否确定完成？`
    : '装箱数据检测通过，是否确定完成并进入待出库？'
  hookComponent.$dialog({ content, handleConfirm: advanceAfterPackingCheck })
}
onMounted(load)
</script>

<style scoped lang="less">
.packing-editor { padding: 4px; background: rgb(var(--v-theme-surface)); }
.editor-heading,.box-title,.box-actions,.editor-actions,.box-toolbar,.box-item-row,.editor-footer { display: flex; align-items: center; gap: 12px; }
.editor-heading,.box-title { justify-content: space-between; }.product-pool { margin-top: 10px; }.box-toolbar { margin: 14px 0; }
.packing-progress { margin-bottom: 14px; overflow: hidden; border: 1px solid rgba(var(--v-border-color),var(--v-border-opacity)); border-radius: 6px; }
.packing-progress-title { padding: 9px 12px; background: rgba(var(--v-theme-primary),.08); font-weight: 600; }
.progress-product { display: flex; align-items: center; gap: 10px; }.progress-product small { display: block; }
.packed-summary { color: rgb(var(--v-theme-success)); }.remaining-summary { color: rgb(var(--v-theme-warning)); }.packed-summary small,.remaining-summary small { display: block; }
.box-card { background: rgb(var(--v-theme-surface)); }.box-card + .box-card { margin-top: 12px; }.measurement-grid { display: grid; grid-template-columns: repeat(4, minmax(120px,1fr)); gap: 10px; }
.box-item-header,.box-item-row { display: grid; grid-template-columns: minmax(180px,2fr) minmax(100px,1fr) minmax(70px,.7fr) minmax(110px,1fr) 48px; align-items: center; gap: 12px; }
.box-item-header { margin-top: 16px; padding: 8px 0; border-bottom: 1px solid rgba(var(--v-border-color),var(--v-border-opacity)); font-weight: 600; }
.box-item-row { margin-top: 10px; }.box-item-row small { display: block; }
.editor-footer { position: sticky; z-index: 3; bottom: -20px; justify-content: space-between; margin: 16px -20px -20px; padding: 12px 20px; border-top: 1px solid rgba(var(--v-border-color),var(--v-border-opacity)); background: rgb(var(--v-theme-surface)); box-shadow: 0 -3px 10px rgba(0,0,0,.08); }
.completion-status { min-width: 0; }.completion-status strong,.completion-status small { display: block; }.completion-status small { max-width: 900px; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.editor-actions { justify-content: flex-end; flex-shrink: 0; }
small { opacity: .65; }
</style>
