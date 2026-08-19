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
      <div class="box-toolbar">
        <v-text-field
          :model-value="plannedBoxCountInput"
          class="planned-box-count"
          label="计划箱数"
          density="compact"
          variant="outlined"
          inputmode="numeric"
          pattern="[0-9]*"
          hide-details
          :disabled="!editable"
          @keydown="restrictPlannedBoxCountKeydown"
          @update:model-value="updatePlannedBoxCount"
          @blur="commitPlannedBoxCount"
        />
        <v-btn size="small" color="primary" prepend-icon="mdi-package-variant" :disabled="!editable" @click="addEmptyBox">新增箱</v-btn>
      </div>
      <v-card v-for="(box, boxIndex) in plan.boxes" :key="box.client_key" variant="outlined" class="box-card">
        <v-card-title class="box-title">
          <span>第 {{ boxIndex + 1 }} 箱</span>
          <div class="box-actions">
            <v-btn size="small" prepend-icon="mdi-content-copy" variant="tonal" :disabled="!editable" @click="openCopyBox(box, boxIndex)">复制</v-btn>
            <v-btn size="small" prepend-icon="mdi-broom" color="warning" variant="tonal" :disabled="!editable" @click="clearBoxMeasurements(box, boxIndex)">清空</v-btn>
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

    <v-dialog v-model="copyDialog.visible" max-width="520" persistent>
      <v-card>
        <v-card-title>复制箱数据</v-card-title>
        <v-card-text>
          <v-alert type="info" variant="tonal" density="compact" class="mb-4">
            将覆盖目标箱的重量、长宽高和装箱数量，目标箱编号保持不变。
          </v-alert>
          <v-select
            v-model="copyDialog.sourceIndex"
            :items="copySourceOptions"
            label="选择来源箱"
            density="compact"
            class="mb-3"
            hide-details
            @update:model-value="changeCopySource"
          />
          <v-select v-model="copyDialog.targetIndexes" :items="copyTargetOptions" label="选择目标箱（可多选）" density="compact" multiple chips closable-chips hide-details />
        </v-card-text>
        <v-card-actions class="justify-end">
          <v-btn variant="text" @click="closeCopyDialog">取消</v-btn>
          <v-btn color="primary" :disabled="copyDialog.targetIndexes.length === 0" @click="confirmCopyBox">确认复制</v-btn>
        </v-card-actions>
      </v-card>
    </v-dialog>
  </div>
</template>

<script setup lang="ts">
import { computed, onMounted, reactive, ref } from 'vue'
import { confirmDispatchPacking, getDispatchPackingPlan, saveDispatchPackingPlan } from '@/api/wms/dispatchWorkflow'
import { hookComponent } from '@/components/system'
import ProductImage from '@/components/system/product-image.vue'
import type { PackingPlan, PackingPlanBox } from '@/types/DeliveryManagement/DispatchWorkflow'
import {
  allocatedTaskQty,
  copyDraftBox,
  expandPackingPlanBoxes,
  itemTaskLimit,
  newDraftBox,
  normalizePlannedBoxCount,
  plannedBoxCountDigits,
  remainingTaskQty
} from './packingPlanPolicy'
import { advancePackingPlan, inspectPackingPlan } from './packingPlanCompletion'

const props = defineProps<{ orderId: number; packingTaskId: number; frozen?: boolean; autoCheck?: boolean }>()
const emit = defineEmits<{ saved: []; completed: [] }>()
const plan = ref<PackingPlan | null>(null)
const loading = ref(false); const saving = ref(false); const completing = ref(false); const checking = ref(false)
const copyDialog = reactive({ visible: false, sourceIndex: -1, targetIndexes: [] as number[] })
const errorMessage = ref('')
const plannedBoxCountInput = ref('1')
const packingPlanStatus = computed(() => String(plan.value?.packing_plan_status ?? ''))
const editable = computed(() => !props.frozen && (packingPlanStatus.value === 'DRAFT' || packingPlanStatus.value === 'PACKING_CONFIRMED'))
const completionHint = computed(() => plan.value ? inspectPackingPlan(plan.value).issues.join('；') : '')
const copySourceOptions = computed(() => plan.value?.boxes
  .map((_, index) => ({ title: `第 ${index + 1} 箱`, value: index })) ?? [])
const copyTargetOptions = computed(() => plan.value?.boxes
  .map((_, index) => ({ title: `第 ${index + 1} 箱`, value: index }))
  .filter((option) => option.value !== copyDialog.sourceIndex) ?? [])
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
  plannedBoxCountInput.value = String(Math.max(1, packingPlan.boxes.length))
}
const boxesForSave = () => plan.value?.boxes.map((box) => ({
  ...box,
  items: box.items.filter((item) => Number(item.task_qty) > 0)
})) ?? []
const load = async () => { loading.value = true; errorMessage.value = ''; try { const result = await getDispatchPackingPlan(props.orderId, props.packingTaskId, true); if (!result.isSuccess) throw new Error(result.errorMessage); fillBoxProductRows(result.data, true); if (props.autoCheck) checkPacking() } catch (error) { errorMessage.value = error instanceof Error ? error.message : String(error) } finally { loading.value = false } }
const addEmptyBox = () => {
  if (!plan.value) return
  plan.value.boxes = expandPackingPlanBoxes(plan.value, plan.value.boxes.length + 1)
  plannedBoxCountInput.value = String(plan.value.boxes.length)
}
const updatePlannedBoxCount = (value: unknown) => {
  if (!plan.value) return
  const digits = plannedBoxCountDigits(value)
  plannedBoxCountInput.value = digits
  if (!digits) return
  const targetCount = normalizePlannedBoxCount(digits)
  plan.value.boxes = expandPackingPlanBoxes(plan.value, targetCount)
  plannedBoxCountInput.value = String(Math.max(targetCount, plan.value.boxes.length))
}
const commitPlannedBoxCount = () => {
  if (!plan.value) return
  const targetCount = Math.max(1, plan.value.boxes.length, normalizePlannedBoxCount(plannedBoxCountInput.value))
  plan.value.boxes = expandPackingPlanBoxes(plan.value, targetCount)
  plannedBoxCountInput.value = String(plan.value.boxes.length)
}
const restrictPlannedBoxCountKeydown = (event: KeyboardEvent) => {
  const allowedKeys = ['Backspace', 'Delete', 'Tab', 'ArrowLeft', 'ArrowRight', 'Home', 'End']
  if (/^\d$/.test(event.key) || allowedKeys.includes(event.key) || event.ctrlKey || event.metaKey) return
  event.preventDefault()
}
const removeBox = (index: number) => {
  if (!plan.value) return
  plan.value.boxes.splice(index, 1)
  plan.value.boxes = expandPackingPlanBoxes(plan.value, Math.max(1, plan.value.boxes.length))
  plannedBoxCountInput.value = String(plan.value.boxes.length)
}
const closeCopyDialog = () => { copyDialog.visible = false; copyDialog.sourceIndex = -1; copyDialog.targetIndexes = [] }
const changeCopySource = (sourceIndex: number) => {
  copyDialog.targetIndexes = copyDialog.targetIndexes.filter((targetIndex) => targetIndex !== sourceIndex)
}
const openCopyBox = (box: PackingPlanBox, index: number) => {
  if (!plan.value) return
  if (plan.value.boxes.length === 1) {
    plan.value.boxes.push(copyDraftBox(box, 2))
    plannedBoxCountInput.value = String(plan.value.boxes.length)
    hookComponent.$message({ type: 'info', content: '当前没有其他箱，已新建第 2 箱并复制全部数据' })
    return
  }
  copyDialog.sourceIndex = index
  copyDialog.targetIndexes = []
  copyDialog.visible = true
}
const confirmCopyBox = () => {
  if (!plan.value || copyDialog.sourceIndex < 0 || copyDialog.targetIndexes.length === 0) return
  const source = plan.value.boxes[copyDialog.sourceIndex]
  if (!source) return
  const targetIndexes = [...copyDialog.targetIndexes]
  targetIndexes.forEach((targetIndex) => {
    const target = plan.value?.boxes[targetIndex]
    if (!target || source === target) return
    target.weight = source.weight
    target.length = source.length
    target.width = source.width
    target.height = source.height
    target.items = source.items.map((item) => ({ ...item }))
  })
  const targetSequences = targetIndexes.map((targetIndex) => `第 ${targetIndex + 1} 箱`).join('、')
  closeCopyDialog()
  hookComponent.$message({ type: 'info', content: `已将当前箱数据复制到${targetSequences}，请确认后保存` })
}
const clearBoxMeasurements = (box: PackingPlanBox, index: number) => {
  box.weight = null
  box.length = null
  box.width = null
  box.height = null
  hookComponent.$message({ type: 'info', content: `第 ${index + 1} 箱的重量和长宽高已清空，装箱数量保持不变` })
}
const savePacking = async () => { if (!plan.value) return; saving.value = true; try { const result = await saveDispatchPackingPlan(props.orderId, props.packingTaskId, { request_id: requestId(), row_version: plan.value.row_version, task_row_version: plan.value.task_row_version, boxes: boxesForSave() }); if (!result.isSuccess) throw new Error(result.errorMessage); fillBoxProductRows(result.data); hookComponent.$message({ type: 'success', content: '装箱信息已保存，可继续修改或删除' }); emit('saved') } catch (error) { hookComponent.$message({ type: 'error', content: error instanceof Error ? error.message : String(error) }) } finally { saving.value = false } }
const completePacking = () => { if (!plan.value) return; hookComponent.$dialog({ content: '本次仅记录人工确认装箱完成，不执行最终业务校验，也不会进入下一步；确认后仍可继续修改和保存，是否继续？', handleConfirm: async () => { if (!plan.value) return; completing.value = true; try { const saved = await saveDispatchPackingPlan(props.orderId, props.packingTaskId, { request_id: requestId(), row_version: plan.value.row_version, task_row_version: plan.value.task_row_version, boxes: boxesForSave() }); if (!saved.isSuccess) throw new Error(saved.errorMessage); fillBoxProductRows(saved.data); const confirmed = await confirmDispatchPacking(props.orderId, props.packingTaskId, { request_id: requestId(), row_version: plan.value!.row_version, task_row_version: plan.value!.task_row_version }); if (!confirmed.isSuccess) throw new Error(confirmed.errorMessage); fillBoxProductRows(confirmed.data); hookComponent.$message({ type: 'success', content: '已确认装箱完成，当前仍可继续修改和保存；业务下一步尚未执行' }); emit('saved') } catch (error) { hookComponent.$message({ type: 'error', content: error instanceof Error ? error.message : String(error) }) } finally { completing.value = false } } }) }
const advanceAfterPackingCheck = async () => {
  if (!plan.value) return
  checking.value = true
  try {
    await advancePackingPlan(props.orderId, props.packingTaskId, plan.value)
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
  const unfinishedProducts = inspectPackingPlan(plan.value).unfinishedProducts
    .map((item) => `${item.name}商品（剩余任务量${item.remainingTaskQty}，剩余商品数量${item.remainingRequiredQty}）`)
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
.editor-heading,.box-title { justify-content: space-between; }.product-pool { margin-top: 10px; }.box-toolbar { margin: 0 0 14px; }
.planned-box-count { flex: 0 0 110px; max-width: 110px; }
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
