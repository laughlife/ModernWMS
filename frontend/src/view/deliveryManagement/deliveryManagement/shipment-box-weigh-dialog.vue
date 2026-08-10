<template>
  <v-dialog v-model="dialogVisible" max-width="820" persistent>
    <v-card>
      <v-card-title class="dialog-title">
        <span>{{ $t('wms.deliveryManagement.boxWeighing') }}：{{ shipment?.fba_no }}</span>
        <span class="progress-text">{{ completedCount }}/{{ boxes.length }} {{ $t('wms.deliveryManagement.boxUnit') }}</span>
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
                <td>
                  <div class="box-actions">
                    <v-btn
                      v-if="validBox(box) && completedCount < boxes.length"
                      size="small"
                      color="success"
                      variant="tonal"
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
        <v-btn color="primary" :loading="submitting" :disabled="boxes.length === 0" @click="confirmAll">
          {{ $t('system.page.confirm') }}
        </v-btn>
      </v-card-actions>
    </v-card>
  </v-dialog>
</template>

<script setup lang="ts">
import { computed, ref } from 'vue'
import { confirmWeighingBoxes, getWeighingBoxes } from '@/api/wms/deliveryManagement'
import { hookComponent } from '@/components/system'
import type {
  DispatchWeighingBoxVO,
  DispatchWeighingShipmentVO
} from '@/types/DeliveryManagement/DeliveryManagement'

const emit = defineEmits<{ saved: [] }>()
const dialogVisible = ref(false)
const shipment = ref<DispatchWeighingShipmentVO | null>(null)
const boxes = ref<DispatchWeighingBoxVO[]>([])

const validBox = (box: DispatchWeighingBoxVO) => Number(box.weighing_weight) > 0

const submitting = ref(false)
const completedCount = computed(() => boxes.value.filter(validBox).length)

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

const copyToRemaining = (source: DispatchWeighingBoxVO) => {
  for (const box of boxes.value) {
    if (box.erp_box_id === source.erp_box_id || box.is_weighed) continue
    box.weighing_weight = Number(source.weighing_weight)
  }
}

const confirmAll = async () => {
  if (!shipment.value || boxes.value.length === 0) return
  const incomplete = boxes.value.find((box) => !validBox(box))
  if (incomplete) {
    hookComponent.$message({ type: 'error', content: `${incomplete.box_no} 的重量未填写` })
    return
  }

  submitting.value = true
  try {
    const { data: res } = await confirmWeighingBoxes(boxes.value.map((box) => ({
      dispatch_no: shipment.value!.dispatch_no,
      fba_shipment_id: shipment.value!.fba_shipment_id,
      erp_box_id: box.erp_box_id,
      weighing_weight: Number(box.weighing_weight)
    })))
    if (!res.isSuccess) {
      hookComponent.$message({ type: 'error', content: res.errorMessage })
      return
    }
    hookComponent.$message({ type: 'success', content: res.data })
    dialogVisible.value = false
    emit('saved')
  } finally {
    submitting.value = false
  }
}

defineExpose({ openDialog })
</script>

<style lang="less" scoped>
.dialog-title { display: flex; align-items: center; justify-content: space-between; }
.progress-text { font-size: 14px; font-weight: 400; opacity: 0.7; }
.box-table-wrap { overflow-x: auto; }
.box-table { width: 100%; min-width: 620px; border-collapse: collapse; }
.box-table th, .box-table td { padding: 10px 8px; border-bottom: 1px solid rgba(var(--v-border-color), var(--v-border-opacity)); text-align: center; }
.box-table th { white-space: nowrap; }
.box-table td:not(.box-no) { min-width: 130px; }
.box-no { min-width: 210px; text-align: left !important; font-weight: 600; }
.box-no small { display: block; margin-top: 4px; opacity: 0.65; font-weight: 400; }
.box-actions { display: flex; justify-content: center; gap: 8px; min-width: 180px; }
</style>
