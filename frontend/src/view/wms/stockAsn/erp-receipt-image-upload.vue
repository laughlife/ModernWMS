<template>
  <div class="ossImageUpload">
    <v-file-input
      v-model="selectedFiles"
      accept="image/jpeg,image/png,image/gif,image/webp"
      multiple
      chips
      clearable
      variant="outlined"
      density="comfortable"
      :prepend-icon="hideLabel ? undefined : icon"
      :label="hideLabel ? undefined : label"
      :aria-label="label"
      :hint="$t('wms.erpPendingReceipt.attachment_tip')"
      :loading="uploading"
      :disabled="uploading"
      persistent-hint
      @update:model-value="uploadFiles"
    ></v-file-input>

    <div v-if="modelValue.length" class="uploadedImages" :class="{ uploadedImagesWithIcon: !hideLabel }">
      <v-chip
        v-for="(image, index) in modelValue"
        :key="image.path"
        class="uploadedImage"
        color="success"
        variant="tonal"
        closable
        @click="openImage(image.access_url)"
        @click:close.stop="removeImage(index)"
      >
        <v-icon start icon="mdi-image-check-outline"></v-icon>
        {{ image.name }}
      </v-chip>
    </div>
  </div>
</template>

<script lang="ts" setup>
import { ref } from 'vue'
import { hookComponent } from '@/components/system'
import i18n from '@/languages/i18n'
import {
  uploadErpReceiptImage,
  type ErpReceiptImageCategory,
  type ErpReceiptOssImage
} from '@/api/wms/stockAsn'

const props = defineProps<{
  modelValue: ErpReceiptOssImage[]
  shipmentId: number
  category: ErpReceiptImageCategory
  label: string
  icon: string
  hideLabel?: boolean
}>()

const emit = defineEmits<{
  (event: 'update:modelValue', value: ErpReceiptOssImage[]): void
}>()

const selectedFiles = ref<File[]>([])
const uploading = ref(false)

const uploadFiles = async (value: File[] | File | null) => {
  const files = Array.isArray(value) ? value : value ? [value] : []
  if (!files.length || uploading.value) return
  if (props.modelValue.length + files.length > 9) {
    hookComponent.$message({
      type: 'warning',
      content: i18n.global.t('wms.erpPendingReceipt.attachment_count_exceeded')
    })
    selectedFiles.value = []
    return
  }

  uploading.value = true
  const targetShipmentId = props.shipmentId
  const uploaded = [...props.modelValue]
  try {
    for (const file of files) {
      const response = await uploadErpReceiptImage(file, targetShipmentId, props.category)
      if (!response?.isSuccess) {
        throw new Error(response?.errorMessage || i18n.global.t('wms.erpPendingReceipt.attachment_upload_failed'))
      }
      if (props.shipmentId !== targetShipmentId) return
      uploaded.push(response.data)
      emit('update:modelValue', [...uploaded])
    }
    hookComponent.$message({
      type: 'success',
      content: i18n.global.t('wms.erpPendingReceipt.attachment_upload_success')
    })
  } catch (error) {
    hookComponent.$message({
      type: 'error',
      content: error instanceof Error ? error.message : i18n.global.t('wms.erpPendingReceipt.attachment_upload_failed')
    })
  } finally {
    uploading.value = false
    selectedFiles.value = []
  }
}

const removeImage = (index: number) => {
  emit('update:modelValue', props.modelValue.filter((_, itemIndex) => itemIndex !== index))
}

const openImage = (url: string) => {
  window.open(url, '_blank', 'noopener,noreferrer')
}
</script>

<style lang="less" scoped>
.uploadedImages {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
  margin: -8px 0 16px;
}

.uploadedImagesWithIcon {
  margin-left: 40px;
}

.uploadedImage {
  cursor: pointer;
  max-width: calc(100% - 8px);
}
</style>
