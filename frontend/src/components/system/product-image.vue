<template>
  <div class="productImageRoot" :style="imageSizeStyle">
    <button
      v-if="src"
      type="button"
      class="productImageButton"
      :aria-label="previewAriaLabel"
      @click="previewVisible = true"
    >
      <v-img
        :src="src"
        :alt="alt"
        width="100%"
        height="100%"
        :cover="cover"
        referrerpolicy="no-referrer"
        class="productImage"
      >
        <div class="productImagePreviewOverlay">
          <v-icon icon="mdi-magnify-plus-outline" size="24"></v-icon>
        </div>
        <template #error>
          <div class="productImageFallback">
            <v-icon icon="mdi-image-off-outline" :size="fallbackIconSize"></v-icon>
          </div>
        </template>
      </v-img>
    </button>
    <div v-else class="productImage productImageFallback">
      <v-icon icon="mdi-image-off-outline" :size="fallbackIconSize"></v-icon>
    </div>

    <v-dialog v-model="previewVisible" max-width="1100">
      <v-card class="productImagePreviewDialog">
        <v-toolbar color="white" density="compact" :title="alt || '商品图片'">
          <template #append>
            <v-btn icon="mdi-close" variant="text" aria-label="关闭大图" @click="previewVisible = false"></v-btn>
          </template>
        </v-toolbar>
        <v-divider></v-divider>
        <v-card-text class="productImagePreviewContent">
          <v-img
            :src="src"
            :alt="alt"
            max-height="80vh"
            contain
            referrerpolicy="no-referrer"
          ></v-img>
        </v-card-text>
      </v-card>
    </v-dialog>
  </div>
</template>

<script setup lang="ts">
import { computed, ref } from 'vue'

type ImageSize = number | string

const props = withDefaults(defineProps<{
  src?: string
  alt?: string
  width?: ImageSize
  height?: ImageSize
  cover?: boolean
  fallbackIconSize?: ImageSize
}>(), {
  src: '',
  alt: '',
  width: 64,
  height: 64,
  cover: true,
  fallbackIconSize: 24
})

const toCssSize = (value: ImageSize) => {
  return typeof value === 'number' || /^\d+(?:\.\d+)?$/.test(value)
    ? `${value}px`
    : value
}
const imageSizeStyle = computed(() => ({
  width: toCssSize(props.width),
  height: toCssSize(props.height)
}))
const previewAriaLabel = computed(() => `查看大图：${props.alt || '商品图片'}`)
const previewVisible = ref(false)
</script>

<style scoped>
.productImageRoot {
  display: block;
  min-width: 0;
}

.productImageButton {
  background: transparent;
  border: 0;
  cursor: zoom-in;
  display: block;
  height: 100%;
  padding: 0;
  width: 100%;
}

.productImageButton:focus-visible {
  outline: 2px solid rgb(var(--v-theme-primary));
  outline-offset: 2px;
}

.productImage {
  border: 1px solid rgba(var(--v-border-color), var(--v-border-opacity));
  border-radius: 6px;
  height: 100%;
  overflow: hidden;
  width: 100%;
}

.productImageFallback {
  align-items: center;
  background: rgba(var(--v-theme-on-surface), 0.04);
  color: rgba(var(--v-theme-on-surface), 0.38);
  display: flex;
  justify-content: center;
}

.v-img .productImageFallback {
  height: 100%;
  width: 100%;
}

.productImagePreviewOverlay {
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

.productImageButton:hover .productImagePreviewOverlay,
.productImageButton:focus-visible .productImagePreviewOverlay {
  opacity: 1;
}

.productImagePreviewDialog {
  overflow: hidden;
}

.productImagePreviewContent {
  background: rgba(var(--v-theme-on-surface), 0.04);
  padding: 16px;
}
</style>
