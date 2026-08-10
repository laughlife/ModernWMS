<template>
  <v-dialog v-model="dialogVisible" max-width="1320" persistent>
    <v-card>
      <v-card-title class="dialog-title">
        <span>{{ $t('wms.deliveryManagement.boxWeighing') }}：{{ shipment?.fba_no }}</span>
        <span class="progress-text">{{ weighedCount }}/{{ boxes.length }} {{ $t('wms.deliveryManagement.boxUnit') }}</span>
      </v-card-title>
      <v-card-text>
        <v-alert type="info" variant="tonal" density="compact" class="mb-4">
          {{ $t('wms.deliveryManagement.boxMeasurementTip') }}
        </v-alert>
        <v-alert v-if="boxes.length === 0" type="warning" variant="tonal" density="compact" class="mb-4">
          {{ $t('wms.deliveryManagement.noBoxData') }}
        </v-alert>
        <div class="box-table-wrap">
          <table class="box-table">
            <thead>
              <tr>
                <th>{{ $t('wms.deliveryManagement.boxNo') }}</th>
                <th>{{ $t('wms.deliveryManagement.weighingWeightKg') }}</th>
                <th>{{ $t('wms.deliveryManagement.weighingLength') }}</th>
                <th>{{ $t('wms.deliveryManagement.weighingWidth') }}</th>
                <th>{{ $t('wms.deliveryManagement.weighingHeight') }}</th>
                <th>{{ $t('wms.deliveryManagement.volumeCm3') }}</th>
                <th>{{ $t('system.page.operate') }}</th>
              </tr>
            </thead>
            <tbody>
              <tr v-for="box in boxes" :key="box.erp_box_id">
                <td class="box-no">
                  <div>{{ box.box_no }}</div>
                  <small v-if="box.tracking_id">{{ box.tracking_id }}</small>
                </td>
                <td><v-text-field v-model.number="box.weighing_weight" type="number" min="0.01" step="0.01" density="compact" hide-details /></td>
                <td><v-text-field v-model.number="box.weighing_length" type="number" min="0.01" step="0.01" density="compact" hide-details /></td>
                <td><v-text-field v-model.number="box.weighing_width" type="number" min="0.01" step="0.01" density="compact" hide-details /></td>
                <td><v-text-field v-model.number="box.weighing_height" type="number" min="0.01" step="0.01" density="compact" hide-details /></td>
                <td>{{ volumeOf(box) || '-' }}</td>
                <td>
                  <div class="box-actions">
                    <v-btn size="small" color="primary" variant="tonal" :loading="savingBoxId === box.erp_box_id" @click="saveBox(box)">
                      {{ $t('wms.deliveryManagement.weigh') }}
                    </v-btn>
                    <v-btn
                      v-if="box.is_weighed && weighedCount < boxes.length"
                      size="small"
                      color="success"
                      variant="tonal"
                      :loading="copying"
                      @click="copyToRemaining(box)"
                    >{{ $t('wms.deliveryManagement.copyToOtherBoxes') }}</v-btn>
                  </div>
                </td>
              </tr>
            </tbody>
          </table>
        </div>
      </v-card-text>
      <v-card-actions class="justify-end">
        <v-btn variant="text" @click="closeDialog">{{ $t('system.page.close') }}</v-btn>
      </v-card-actions>
    </v-card>
  </v-dialog>
</template>

<script setup lang="ts">
import { computed, ref } from 'vue'
import { copyWeighingBox, getWeighingBoxes, saveWeighingBox } from '@/api/wms/deliveryManagement'
import { hookComponent } from '@/components/system'
import type {
  DispatchWeighingBoxVO,
  DispatchWeighingShipmentVO
} from '@/types/DeliveryManagement/DeliveryManagement'

const emit = defineEmits<{ saved: [] }>()
const dialogVisible = ref(false)
const shipment = ref<DispatchWeighingShipmentVO | null>(null)
const boxes = ref<DispatchWeighingBoxVO[]>([])
const savingBoxId = ref<number | null>(null)
const copying = ref(false)
const weighedCount = computed(() => boxes.value.filter((box) => box.is_weighed).length)

const volumeOf = (box: DispatchWeighingBoxVO) => {
  const volume = Number(box.weighing_length) * Number(box.weighing_width) * Number(box.weighing_height)
  return Number.isFinite(volume) && volume > 0 ? Number(volume.toFixed(2)) : 0
}

const loadBoxes = async () => {
  if (!shipment.value) return
  const { data: res } = await getWeighingBoxes(shipment.value.dispatch_no, shipment.value.fba_shipment_id)
  if (!res.isSuccess) {
    hookComponent.$message({ type: 'error', content: res.errorMessage })
    return
  }
  boxes.value = res.data
}

const openDialog = async (row: DispatchWeighingShipmentVO) => {
  shipment.value = row
  boxes.value = []
  dialogVisible.value = true
  await loadBoxes()
}

const closeDialog = () => {
  dialogVisible.value = false
  emit('saved')
}

const validBox = (box: DispatchWeighingBoxVO) =>
  Number(box.weighing_weight) > 0 && Number(box.weighing_length) > 0 && Number(box.weighing_width) > 0 && Number(box.weighing_height) > 0

const saveBox = async (box: DispatchWeighingBoxVO) => {
  if (!shipment.value || !validBox(box)) {
    hookComponent.$message({ type: 'error', content: '重量和长宽高必须大于0' })
    return
  }
  savingBoxId.value = box.erp_box_id
  try {
    const { data: res } = await saveWeighingBox({
      dispatch_no: shipment.value.dispatch_no,
      fba_shipment_id: shipment.value.fba_shipment_id,
      erp_box_id: box.erp_box_id,
      weighing_weight: Number(box.weighing_weight),
      weighing_length: Number(box.weighing_length),
      weighing_width: Number(box.weighing_width),
      weighing_height: Number(box.weighing_height)
    })
    if (!res.isSuccess) {
      hookComponent.$message({ type: 'error', content: res.errorMessage })
      return
    }
    hookComponent.$message({ type: 'success', content: res.data })
    await loadBoxes()
    emit('saved')
  } finally {
    savingBoxId.value = null
  }
}

const copyToRemaining = async (box: DispatchWeighingBoxVO) => {
  if (!shipment.value) return
  copying.value = true
  try {
    const { data: res } = await copyWeighingBox(shipment.value.dispatch_no, shipment.value.fba_shipment_id, box.erp_box_id)
    if (!res.isSuccess) {
      hookComponent.$message({ type: 'error', content: res.errorMessage })
      return
    }
    hookComponent.$message({ type: 'success', content: res.data })
    await loadBoxes()
    emit('saved')
  } finally {
    copying.value = false
  }
}

defineExpose({ openDialog })
</script>

<style lang="less" scoped>
.dialog-title { display: flex; align-items: center; justify-content: space-between; }
.progress-text { font-size: 14px; font-weight: 400; opacity: 0.7; }
.box-table-wrap { overflow-x: auto; }
.box-table { width: 100%; min-width: 1120px; border-collapse: collapse; }
.box-table th, .box-table td { padding: 10px 8px; border-bottom: 1px solid rgba(var(--v-border-color), var(--v-border-opacity)); text-align: center; }
.box-table th { white-space: nowrap; }
.box-table td:not(.box-no) { min-width: 130px; }
.box-no { min-width: 210px; text-align: left !important; font-weight: 600; }
.box-no small { display: block; margin-top: 4px; opacity: 0.65; font-weight: 400; }
.box-actions { display: flex; justify-content: center; gap: 8px; min-width: 210px; }
</style>
