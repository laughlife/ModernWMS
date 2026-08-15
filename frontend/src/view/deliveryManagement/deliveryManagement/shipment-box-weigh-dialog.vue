<template>
  <v-dialog v-model="dialogVisible" max-width="1480" persistent>
    <v-card>
      <v-card-title class="dialog-title"><span>逐箱称重测量：{{ order?.dispatch_no }}</span><span class="progress-text">{{ completedBoxCount }}/{{ totalBoxCount }} 箱已测量</span></v-card-title>
      <v-card-text>
        <v-alert type="info" variant="tonal" density="compact" class="mb-4">重量（kg）和长宽高（cm）均以仓库实测为唯一数据来源，四项必须全部大于 0。</v-alert>
        <v-alert v-if="frozen" type="error" variant="tonal" density="compact" class="mb-4">来源装箱任务已发生变更，本单已冻结。请人工选择取消发货或继续发货后再操作。</v-alert>
        <v-alert v-if="capabilityError" type="error" variant="tonal" density="compact" class="mb-4">{{ capabilityError }}</v-alert>
        <div v-if="loading" class="loading-area"><v-progress-circular indeterminate color="primary" /></div>
        <template v-else>
          <section v-for="task in tasks" :key="task.id" class="task-section">
            <div class="task-title">
              <div><strong>{{ task.source_task_no }}</strong><span class="task-progress">{{ taskCompleteCount(task.id) }}/{{ taskBoxes(task.id).length }} 箱</span></div>
              <v-btn size="small" color="success" variant="tonal" :loading="completingTaskId === task.id"
                :disabled="frozen || Boolean(taskCapabilityError(task.id)) || !canCompleteTask(task.id) || task.measured_box_count >= task.expected_box_count"
                @click="completeTask(task.id)">完成该装箱任务测量</v-btn>
            </div>
            <v-alert v-if="taskCapabilityError(task.id)" type="error" variant="tonal" density="compact" class="mb-2">{{ taskCapabilityError(task.id) }}</v-alert>
            <div class="box-table-wrap">
              <table class="box-table">
                <thead><tr><th>来源箱</th><th>重量 kg</th><th>长 cm</th><th>宽 cm</th><th>高 cm</th><th>体积 cm³</th><th>复制到现有箱</th><th>操作</th></tr></thead>
                <tbody>
                  <tr v-for="(box, rowIndex) in taskBoxes(task.id)" :key="box.id">
                    <td class="box-identity"><div>第 {{ box.box_sequence }} 箱</div><small>{{ box.source_box_identity }}</small></td>
                    <td><v-text-field v-model.number="box.weight" :id="inputId(box, 'weight')" type="number" min="0.01" step="0.01" density="compact" hide-details :disabled="frozen" @update:model-value="markDirty(box.id)" @keydown.enter.prevent="focusNext(task.id, rowIndex, 'weight')" @keydown.tab.exact.prevent="focusNext(task.id, rowIndex, 'weight')" /></td>
                    <td><v-text-field v-model.number="box.length" :id="inputId(box, 'length')" type="number" min="0.01" step="0.01" density="compact" hide-details :disabled="frozen" @update:model-value="markDirty(box.id)" @keydown.enter.prevent="focusNext(task.id, rowIndex, 'length')" @keydown.tab.exact.prevent="focusNext(task.id, rowIndex, 'length')" /></td>
                    <td><v-text-field v-model.number="box.width" :id="inputId(box, 'width')" type="number" min="0.01" step="0.01" density="compact" hide-details :disabled="frozen" @update:model-value="markDirty(box.id)" @keydown.enter.prevent="focusNext(task.id, rowIndex, 'width')" @keydown.tab.exact.prevent="focusNext(task.id, rowIndex, 'width')" /></td>
                    <td><v-text-field v-model.number="box.height" :id="inputId(box, 'height')" type="number" min="0.01" step="0.01" density="compact" hide-details :disabled="frozen" @update:model-value="markDirty(box.id)" @keydown.enter.prevent="focusNext(task.id, rowIndex, 'height')" @keydown.tab.exact.prevent="focusNext(task.id, rowIndex, 'height')" /></td>
                    <td>{{ volumeOf(box) || '-' }}</td>
                    <td><v-select v-model="copyTargets[box.id]" :items="copyTargetOptions(task.id, box.id)" item-title="title" item-value="value" density="compact" hide-details clearable :disabled="frozen || !canCopyFrom(box)" /></td>
                    <td><div class="box-actions">
                      <v-btn size="small" variant="tonal" :disabled="frozen" @click="startSequentialWeighing(task.id, rowIndex)">依次称重</v-btn>
                      <v-btn size="small" color="primary" variant="tonal" :loading="savingBoxId === box.id" :disabled="frozen || !isBoxMeasurementComplete(box)" @click="saveBox(box)">保存</v-btn>
                      <v-btn size="small" color="success" variant="tonal" :loading="copyingSourceId === box.id" :disabled="frozen || !copyTargets[box.id] || !canCopyFrom(box)" @click="copyBox(box)">复制</v-btn>
                    </div></td>
                  </tr>
                </tbody>
              </table>
            </div>
          </section>
        </template>
      </v-card-text>
      <v-card-actions class="justify-end">
        <v-btn variant="text" @click="closeDialog">{{ $t('system.page.close') }}</v-btn>
        <v-btn color="primary" :loading="completingOrder" :disabled="frozen || Boolean(capabilityError) || dirtyBoxIds.size > 0 || !allTasksComplete" @click="completeOrder">全部任务测量完成，进入待出库</v-btn>
      </v-card-actions>
    </v-card>
  </v-dialog>
</template>

<script setup lang="ts">
import { computed, nextTick, ref } from 'vue'
import { completeDispatchOrderWeighing, completeDispatchTaskWeighing, copyDispatchWeighingBox, getDispatchOrder, getDispatchTaskBoxes, saveDispatchWeighingBox } from '@/api/wms/dispatchWorkflow'
import { hookComponent } from '@/components/system'
import type { DispatchOrderDetail, DispatchOrderSummary, DispatchPackingTask, WeighingBox } from '@/types/DeliveryManagement/DispatchWorkflow'
import { applyCopiedMeasurement, buildCopyMeasurementCommand, buildSaveMeasurementCommand, getMeasurementCapabilityError, isBoxMeasurementComplete, isCurrentDialogRequest, isTaskMeasurementComplete, mergeRefreshedBoxesPreservingDirtyDrafts } from './dispatchBoxMeasurement'
import type { DialogRequestIdentity } from './dispatchBoxMeasurement'
import { getNextWeighingField } from './weighingFocus'
import type { WeighingField } from './weighingFocus'

const emit = defineEmits<{ saved: []; completed: [] }>()
const dialogVisible = ref(false)
const loading = ref(false)
const order = ref<DispatchOrderDetail | null>(null)
const boxesByTask = ref<Record<number, WeighingBox[]>>({})
const copyTargets = ref<Record<number, number | null>>({})
const savingBoxId = ref<number | null>(null)
const copyingSourceId = ref<number | null>(null)
const completingTaskId = ref<number | null>(null)
const completingOrder = ref(false)
const capabilityErrors = ref<Record<number, string>>({})
const dirtyBoxIds = ref<Set<number>>(new Set())
const activeOrderId = ref<number | null>(null)
let dialogGeneration = 0
const tasks = computed<DispatchPackingTask[]>(() => order.value?.packing_tasks ?? [])
const frozen = computed(() => Boolean(order.value?.source_change_pending))
const totalBoxCount = computed(() => Object.values(boxesByTask.value).reduce((sum, boxes) => sum + boxes.length, 0))
const completedBoxCount = computed(() => Object.values(boxesByTask.value).flat().filter(isBoxMeasurementComplete).length)
const capabilityError = computed(() => Object.values(capabilityErrors.value).find(Boolean) ?? '')
const allTasksComplete = computed(() => tasks.value.length > 0 && tasks.value.every((task) => task.measured_box_count >= task.expected_box_count && task.expected_box_count > 0))
const requestId = () => globalThis.crypto?.randomUUID?.() ?? `weigh-${Date.now()}-${Math.random().toString(16).slice(2)}`
const showError = (message: string) => { if (message === 'SOURCE_CHANGE_PENDING' && order.value) order.value.source_change_pending = true; hookComponent.$message({ type: 'error', content: message }) }
const dialogRequestIsCurrent = (request: DialogRequestIdentity) => isCurrentDialogRequest(request, {
  generation: dialogGeneration,
  orderId: activeOrderId.value ?? -1,
  visible: dialogVisible.value
})
const taskBoxes = (taskId: number) => boxesByTask.value[taskId] ?? []
const taskCapabilityError = (taskId: number) => capabilityErrors.value[taskId] ?? ''
const taskCompleteCount = (taskId: number) => taskBoxes(taskId).filter(isBoxMeasurementComplete).length
const isTaskComplete = (taskId: number) => isTaskMeasurementComplete(taskBoxes(taskId))
const taskHasDirtyBox = (taskId: number) => taskBoxes(taskId).some((box) => dirtyBoxIds.value.has(box.id))
const canCompleteTask = (taskId: number) => isTaskComplete(taskId) && !taskHasDirtyBox(taskId)
const canCopyFrom = (box: WeighingBox) => isBoxMeasurementComplete(box) && !dirtyBoxIds.value.has(box.id)
const markDirty = (boxId: number) => { const next = new Set(dirtyBoxIds.value); next.add(boxId); dirtyBoxIds.value = next }
const clearDirty = (boxId: number) => { const next = new Set(dirtyBoxIds.value); next.delete(boxId); dirtyBoxIds.value = next }
const volumeOf = (box: WeighingBox) => { const value = Number(box.length) * Number(box.width) * Number(box.height); return Number.isFinite(value) && value > 0 ? Number(value.toFixed(2)) : 0 }
const inputId = (box: WeighingBox, field: WeighingField) => `dispatch-box-${box.id}-${field}`
const focusInput = (taskId: number, rowIndex: number, field: WeighingField) => { const box = taskBoxes(taskId)[rowIndex]; if (box) document.getElementById(inputId(box, field))?.focus() }
const focusNext = (taskId: number, rowIndex: number, field: WeighingField) => { const target = getNextWeighingField(rowIndex, field, taskBoxes(taskId).length); if (target) focusInput(taskId, target.rowIndex, target.field) }
const startSequentialWeighing = async (taskId: number, rowIndex: number) => { await nextTick(); focusInput(taskId, rowIndex, 'weight'); hookComponent.$message({ type: 'info', content: '已定位重量输入框；称重设备接入完成前请录入仓库实测值。' }) }

const loadTaskBoxes = async (taskId: number, refreshedBoxId: number | undefined, context: DialogRequestIdentity) => {
  if (!dialogRequestIsCurrent(context)) return
  const result = await getDispatchTaskBoxes(context.orderId, taskId)
  if (!dialogRequestIsCurrent(context)) return
  if (!result.isSuccess) { capabilityErrors.value = { ...capabilityErrors.value, [taskId]: result.errorMessage }; return }
  const refreshedBoxes = result.data.map((box) => ({ ...box }))
  boxesByTask.value = {
    ...boxesByTask.value,
    [taskId]: refreshedBoxId === undefined
      ? refreshedBoxes
      : mergeRefreshedBoxesPreservingDirtyDrafts(taskBoxes(taskId), refreshedBoxes, dirtyBoxIds.value, refreshedBoxId)
  }
  const error = getMeasurementCapabilityError(result.data); const nextErrors = { ...capabilityErrors.value }
  if (error) nextErrors[taskId] = error; else delete nextErrors[taskId]
  capabilityErrors.value = nextErrors
}
const loadOrder = async (context: DialogRequestIdentity) => {
  const result = await getDispatchOrder(context.orderId)
  if (!dialogRequestIsCurrent(context)) return
  if (!result.isSuccess) throw new Error(result.errorMessage)
  order.value = result.data; boxesByTask.value = {}; capabilityErrors.value = {}; dirtyBoxIds.value = new Set()
  await Promise.all(result.data.packing_tasks.map((task) => loadTaskBoxes(task.id, undefined, context)))
}
const openDialog = async (row: DispatchOrderSummary) => {
  const context = { generation: ++dialogGeneration, orderId: row.id }
  activeOrderId.value = row.id; dialogVisible.value = true; loading.value = true; order.value = null; boxesByTask.value = {}; copyTargets.value = {}
  savingBoxId.value = null; copyingSourceId.value = null; completingTaskId.value = null; completingOrder.value = false
  try {
    await loadOrder(context)
    if (!dialogRequestIsCurrent(context)) return
    await nextTick()
    if (!dialogRequestIsCurrent(context)) return
    const firstTask = tasks.value[0]; if (firstTask) focusInput(firstTask.id, 0, 'weight')
  } catch (error) { if (dialogRequestIsCurrent(context)) showError(error instanceof Error ? error.message : '加载称重数据失败') }
  finally { if (dialogRequestIsCurrent(context)) loading.value = false }
}
const closeDialog = () => { dialogGeneration++; dialogVisible.value = false; activeOrderId.value = null; order.value = null; boxesByTask.value = {} }
const saveBox = async (box: WeighingBox) => {
  if (!order.value || frozen.value) return
  const context = { generation: dialogGeneration, orderId: order.value.id }
  savingBoxId.value = box.id
  try {
    const command = buildSaveMeasurementCommand(order.value, box, requestId())
    const result = await saveDispatchWeighingBox(command.orderId, command.boxId, command.payload)
    if (!dialogRequestIsCurrent(context)) return
    if (!result.isSuccess) { showError(result.errorMessage); return }
    order.value!.row_version = result.data.row_version; clearDirty(box.id); await loadTaskBoxes(box.packing_task_id, box.id, context)
    if (!dialogRequestIsCurrent(context)) return
    hookComponent.$message({ type: 'success', content: '箱体实测数据已保存' }); emit('saved')
  } catch (error) { if (dialogRequestIsCurrent(context)) showError(error instanceof Error ? error.message : '保存箱体实测数据失败') }
  finally { if (dialogRequestIsCurrent(context)) savingBoxId.value = null }
}
const copyTargetOptions = (taskId: number, sourceBoxId: number) => taskBoxes(taskId).filter((box) => box.id !== sourceBoxId).map((box) => ({ title: `第 ${box.box_sequence} 箱 · ${box.source_box_identity}`, value: box.id }))
const copyBox = async (source: WeighingBox) => {
  if (!order.value || frozen.value) return
  const targetId = copyTargets.value[source.id]; const target = taskBoxes(source.packing_task_id).find((box) => box.id === targetId)
  if (!target) { showError('请选择同一装箱任务中的现有目标箱'); return }
  const context = { generation: dialogGeneration, orderId: order.value.id }
  copyingSourceId.value = source.id
  try {
    const command = buildCopyMeasurementCommand(order.value, source, target, requestId())
    const result = await copyDispatchWeighingBox(command.orderId, command.targetBoxId, command.payload)
    if (!dialogRequestIsCurrent(context)) return
    if (!result.isSuccess) { showError(result.errorMessage); return }
    order.value!.row_version = result.data.row_version
    boxesByTask.value = { ...boxesByTask.value, [source.packing_task_id]: applyCopiedMeasurement(taskBoxes(source.packing_task_id), source, target) }
    clearDirty(target.id); await loadTaskBoxes(source.packing_task_id, target.id, context)
    if (!dialogRequestIsCurrent(context)) return
    copyTargets.value[source.id] = null
    hookComponent.$message({ type: 'success', content: '已复制到现有目标箱，目标箱仍可继续编辑' }); emit('saved')
  } catch (error) { if (dialogRequestIsCurrent(context)) showError(error instanceof Error ? error.message : '复制箱体实测数据失败') }
  finally { if (dialogRequestIsCurrent(context)) copyingSourceId.value = null }
}
const completeTask = async (taskId: number) => {
  if (!order.value || frozen.value || !canCompleteTask(taskId)) return
  const context = { generation: dialogGeneration, orderId: order.value.id }
  completingTaskId.value = taskId
  try {
    const result = await completeDispatchTaskWeighing(order.value.id, taskId, { request_id: requestId(), row_version: order.value.row_version })
    if (!dialogRequestIsCurrent(context)) return
    if (!result.isSuccess) { showError(result.errorMessage); return }
    await loadOrder(context)
    if (!dialogRequestIsCurrent(context)) return
    hookComponent.$message({ type: 'success', content: '该装箱任务的全部箱已完成测量' }); emit('saved')
  } catch (error) { if (dialogRequestIsCurrent(context)) showError(error instanceof Error ? error.message : '完成装箱任务测量失败') }
  finally { if (dialogRequestIsCurrent(context)) completingTaskId.value = null }
}
const completeOrder = async () => {
  if (!order.value || frozen.value || !allTasksComplete.value) return
  const context = { generation: dialogGeneration, orderId: order.value.id }
  completingOrder.value = true
  try {
    const result = await completeDispatchOrderWeighing(order.value.id, { request_id: requestId(), row_version: order.value.row_version })
    if (!dialogRequestIsCurrent(context)) return
    if (!result.isSuccess) { showError(result.errorMessage); return }
    closeDialog(); hookComponent.$message({ type: 'success', content: '全部装箱任务已测量，拣货单已进入待出库' }); emit('completed')
  } catch (error) { if (dialogRequestIsCurrent(context)) showError(error instanceof Error ? error.message : '完成整单称重失败') }
  finally { if (dialogRequestIsCurrent(context)) completingOrder.value = false }
}
defineExpose({ openDialog })
</script>

<style lang="less" scoped>
.dialog-title { display: flex; align-items: center; justify-content: space-between; }
.progress-text { font-size: 14px; font-weight: 400; opacity: 0.7; }
.loading-area { min-height: 220px; display: flex; align-items: center; justify-content: center; }
.task-section + .task-section { margin-top: 24px; }
.task-title { display: flex; align-items: center; justify-content: space-between; margin-bottom: 10px; }
.task-progress { margin-left: 12px; font-size: 13px; opacity: 0.68; }
.box-table-wrap { overflow-x: auto; }
.box-table { width: 100%; min-width: 1280px; border-collapse: collapse; }
.box-table th, .box-table td { padding: 10px 8px; border-bottom: 1px solid rgba(var(--v-border-color), var(--v-border-opacity)); text-align: center; }
.box-table th { white-space: nowrap; }
.box-table td:not(.box-identity) { min-width: 128px; }
.box-identity { min-width: 210px; text-align: left !important; font-weight: 600; }
.box-identity small { display: block; margin-top: 3px; font-weight: 400; opacity: 0.62; word-break: break-all; }
.box-actions { display: flex; justify-content: center; gap: 6px; min-width: 230px; }
</style>
