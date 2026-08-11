<template>
  <v-dialog v-model="visible" width="720" persistent>
    <v-card>
      <v-toolbar color="white" title="设置材积比" />
      <v-card-text>
        <div class="mb-4 text-medium-emphasis">单箱材积重 = 单箱体积(cm³) ÷ 材积比，结果保留两位小数。</div>
        <v-radio-group v-model="selectedDivisor" hide-details>
          <div v-for="option in volumeOptions" :key="option.divisor" class="volume-option mb-3">
            <v-radio :value="option.divisor" :label="`材积比 ${option.divisor}`" color="primary" />
            <div class="box-values">
              <span v-for="box in option.boxes" :key="`${option.divisor}-${box.box_no}`">
                {{ box.box_no }}：{{ box.volumetric_weight.toFixed(2) }} kg
              </span>
            </div>
          </div>
        </v-radio-group>
      </v-card-text>
      <v-card-actions class="justify-end">
        <v-btn variant="text" @click="closeDialog">关闭</v-btn>
        <v-btn color="primary" variant="text" :loading="saving" @click="save">确定</v-btn>
      </v-card-actions>
    </v-card>
  </v-dialog>
</template>

<script lang="ts" setup>
import { computed, ref } from 'vue'
import { getWeighingBoxes, setOutboundVolumeDivisor } from '@/api/wms/deliveryManagement'
import { hookComponent } from '@/components/system'
import type { DeliveryManagementDetailVO, DispatchWeighingBoxVO } from '@/types/DeliveryManagement/DeliveryManagement'
import { calculateBoxVolumetricWeights, OUTBOUND_VOLUME_DIVISORS } from '@/utils/outboundSettings'

const emit = defineEmits<{ saved: [] }>()
const visible = ref(false)
const saving = ref(false)
const currentRow = ref<DeliveryManagementDetailVO | null>(null)
const boxes = ref<DispatchWeighingBoxVO[]>([])
const selectedDivisor = ref<number>(5000)

const volumeOptions = computed(() => OUTBOUND_VOLUME_DIVISORS.map(divisor => ({
  divisor,
  boxes: calculateBoxVolumetricWeights(boxes.value, divisor)
})))

const openDialog = async (row: DeliveryManagementDetailVO) => {
  if (!row.dispatch_no || !row.fba_shipment_id) {
    hookComponent.$message({ type: 'error', content: '未找到该行对应的FBA货件箱数据' })
    return
  }
  const { data: res } = await getWeighingBoxes(row.dispatch_no, row.fba_shipment_id)
  if (!res.isSuccess) {
    hookComponent.$message({ type: 'error', content: res.errorMessage })
    return
  }
  currentRow.value = row
  boxes.value = res.data
  selectedDivisor.value = row.volume_divisor || 5000
  visible.value = true
}

const closeDialog = () => { visible.value = false }

const save = async () => {
  if (!currentRow.value) return
  saving.value = true
  try {
    const { data: res } = await setOutboundVolumeDivisor({
      id: currentRow.value.id,
      volume_divisor: selectedDivisor.value
    })
    if (!res.isSuccess) {
      hookComponent.$message({ type: 'error', content: res.errorMessage })
      return
    }
    hookComponent.$message({ type: 'success', content: res.data })
    visible.value = false
    emit('saved')
  } finally {
    saving.value = false
  }
}

defineExpose({ openDialog })
</script>

<style lang="less" scoped>
.volume-option { border: 1px solid rgba(var(--v-border-color), var(--v-border-opacity)); border-radius: 8px; padding: 8px 14px 12px; }
.box-values { display: flex; flex-wrap: wrap; gap: 6px 20px; padding-left: 40px; color: rgba(var(--v-theme-on-surface), 0.7); }
</style>
