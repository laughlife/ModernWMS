<template>
  <v-dialog v-model="visible" width="560" persistent>
    <v-card>
      <v-toolbar color="white" title="设置承运单位" />
      <v-card-text>
        <div class="mb-3 text-medium-emphasis">请选择一个国内仓库作为本行承运单位。</div>
        <v-radio-group v-model="selectedWarehouseId" hide-details>
          <v-radio v-for="option in options" :key="option.id" :value="option.id" :label="option.name" color="primary" />
        </v-radio-group>
        <div v-if="options.length === 0" class="text-medium-emphasis py-4">暂无可选承运单位</div>
      </v-card-text>
      <v-card-actions class="justify-end">
        <v-btn variant="text" @click="closeDialog">关闭</v-btn>
        <v-btn color="primary" variant="text" :disabled="!selectedWarehouseId" :loading="saving" @click="save">确定</v-btn>
      </v-card-actions>
    </v-card>
  </v-dialog>
</template>

<script lang="ts" setup>
import { ref } from 'vue'
import { getOutboundCarrierOptions, setOutboundCarrier } from '@/api/wms/deliveryManagement'
import { hookComponent } from '@/components/system'
import type { DeliveryManagementDetailVO, OutboundCarrierOptionVO } from '@/types/DeliveryManagement/DeliveryManagement'

const emit = defineEmits<{ saved: [] }>()
const visible = ref(false)
const saving = ref(false)
const currentRow = ref<DeliveryManagementDetailVO | null>(null)
const options = ref<OutboundCarrierOptionVO[]>([])
const selectedWarehouseId = ref<number | null>(null)

const openDialog = async (row: DeliveryManagementDetailVO) => {
  const { data: res } = await getOutboundCarrierOptions()
  if (!res.isSuccess) {
    hookComponent.$message({ type: 'error', content: res.errorMessage })
    return
  }
  currentRow.value = row
  options.value = res.data
  selectedWarehouseId.value = row.carrier_warehouse_id || null
  visible.value = true
}

const closeDialog = () => { visible.value = false }

const save = async () => {
  if (!currentRow.value || !selectedWarehouseId.value) return
  saving.value = true
  try {
    const { data: res } = await setOutboundCarrier({
      id: currentRow.value.id,
      carrier_warehouse_id: selectedWarehouseId.value
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
