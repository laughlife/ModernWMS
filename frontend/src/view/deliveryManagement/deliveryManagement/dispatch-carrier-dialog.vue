<template>
  <v-dialog v-model="visible" width="560" persistent>
    <v-card>
      <v-card-title>{{ orderIds.length > 1 ? `批量设置承运信息（${orderIds.length}单）` : '设置承运信息' }}</v-card-title>
      <v-card-text>
        <v-alert type="info" variant="tonal" density="compact" class="mb-4">
          承运信息可选择除有座山深圳仓以外的国内仓库。
        </v-alert>
        <v-select
          v-model="selectedWarehouseId"
          :items="options"
          item-title="name"
          item-value="id"
          label="承运仓库"
          placeholder="请选择承运仓库"
          :loading="loadingOptions"
          :disabled="loadingOptions || saving"
          no-data-text="暂无可选承运仓库"
        />
      </v-card-text>
      <v-card-actions>
        <v-spacer />
        <v-btn :disabled="saving" @click="closeDialog">取消</v-btn>
        <v-btn color="primary" :loading="saving" :disabled="!selectedWarehouseId || loadingOptions" @click="save">确定</v-btn>
      </v-card-actions>
    </v-card>
  </v-dialog>
</template>

<script lang="ts" setup>
import { ref } from 'vue'
import { getDispatchCarrierOptions, setDispatchCarrier } from '@/api/wms/dispatchWorkflow'
import type { DispatchCarrierOption } from '@/api/wms/dispatchWorkflow'
import { hookComponent } from '@/components/system'

const emit = defineEmits<{ saved: [] }>()
const visible = ref(false)
const loadingOptions = ref(false)
const saving = ref(false)
const orderIds = ref<number[]>([])
const options = ref<DispatchCarrierOption[]>([])
const selectedWarehouseId = ref<number | null>(null)

const openDialog = async (ids: number[], currentCarrierId: number | null = null): Promise<void> => {
  orderIds.value = [...new Set(ids.filter((id) => id > 0))]
  if (orderIds.value.length === 0) return
  selectedWarehouseId.value = currentCarrierId
  visible.value = true
  loadingOptions.value = true
  try {
    const result = await getDispatchCarrierOptions()
    if (!result.isSuccess) {
      hookComponent.$message({ type: 'error', content: result.errorMessage })
      visible.value = false
      return
    }
    options.value = result.data
  } catch (error) {
    hookComponent.$message({ type: 'error', content: error instanceof Error ? error.message : '承运仓库加载失败' })
    visible.value = false
  } finally {
    loadingOptions.value = false
  }
}

const closeDialog = (): void => {
  if (saving.value) return
  visible.value = false
}

const save = async (): Promise<void> => {
  if (!selectedWarehouseId.value || orderIds.value.length === 0 || saving.value) return
  saving.value = true
  try {
    const result = await setDispatchCarrier({
      order_ids: orderIds.value,
      carrier_warehouse_id: selectedWarehouseId.value
    })
    if (!result.isSuccess) {
      hookComponent.$message({ type: 'error', content: result.errorMessage })
      return
    }
    hookComponent.$message({
      type: 'success',
      content: `已为${result.data.updated_order_count}张拣货单设置承运仓库：${result.data.carrier_unit}`
    })
    visible.value = false
    emit('saved')
  } catch (error) {
    hookComponent.$message({ type: 'error', content: error instanceof Error ? error.message : '承运信息保存失败' })
  } finally {
    saving.value = false
  }
}

defineExpose({ openDialog })
</script>
