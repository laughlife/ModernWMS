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
      <div
        v-for="(image, index) in modelValue"
        :key="image.path"
        class="uploadedImage"
      >
        <button class="imagePreviewButton" type="button" @click="openPreview(image)">
          <v-img :src="image.access_url" :alt="image.name" width="112" height="84" cover>
            <div class="previewOverlay">
              <v-icon icon="mdi-magnify-plus-outline" size="24"></v-icon>
            </div>
          </v-img>
          <span class="uploadedImageName" :title="image.name">{{ image.name }}</span>
        </button>
        <v-btn
          class="removeImageButton"
          icon="mdi-close"
          size="x-small"
          color="error"
          variant="flat"
          :aria-label="$t('system.page.delete')"
          @click="removeImage(index)"
        ></v-btn>
      </div>
    </div>

    <v-dialog v-model="previewVisible" max-width="960">
      <v-card v-if="previewImage" class="imagePreviewDialog">
        <v-toolbar color="white" density="compact" :title="previewImage.name">
          <template #append>
            <v-btn icon="mdi-close" variant="text" @click="previewVisible = false"></v-btn>
          </template>
        </v-toolbar>
        <v-divider></v-divider>
        <v-card-text class="imagePreviewContent">
          <v-img
            :src="previewImage.access_url"
            :alt="previewImage.name"
            max-height="72vh"
            contain
          ></v-img>
        </v-card-text>
      </v-card>
    </v-dialog>
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
const previewVisible = ref(false)
const previewImage = ref<ErpReceiptOssImage | null>(null)

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

const openPreview = (image: ErpReceiptOssImage) => {
  previewImage.value = image
  previewVisible.value = true
}
</script>

<style lang="less" scoped>
.uploadedImages {
  display: flex;
  flex-wrap: wrap;
  gap: 12px;
  margin: -8px 0 16px;
}

.uploadedImagesWithIcon {
  margin-left: 40px;
}

.uploadedImage {
  border: 1px solid rgba(var(--v-border-color), var(--v-border-opacity));
  border-radius: 6px;
  overflow: visible;
  position: relative;
  width: 112px;
}

.imagePreviewButton {
  background: transparent;
  border: 0;
  color: inherit;
  cursor: pointer;
  display: block;
  padding: 0;
  text-align: left;
  width: 100%;
}

.previewOverlay {
  align-items: center;
  background: rgba(0, 0, 0, 0.42);
  color: #fff;
  display: flex;
  inset: 0;
  justify-content: center;
  opacity: 0;
  position: absolute;
  transition: opacity 0.2s ease;
}

.imagePreviewButton:hover .previewOverlay,
.imagePreviewButton:focus-visible .previewOverlay {
  opacity: 1;
}

.uploadedImageName {
  display: block;
  font-size: 12px;
  overflow: hidden;
  padding: 6px 8px;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.removeImageButton {
  position: absolute;
  right: -9px;
  top: -9px;
  z-index: 1;
}

.imagePreviewDialog {
  overflow: hidden;
}

.imagePreviewContent {
  background: rgba(var(--v-theme-on-surface), 0.04);
  padding: 16px;
}
</style>
