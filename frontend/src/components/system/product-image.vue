<template>
  <v-img
    v-if="src"
    :src="src"
    :alt="alt"
    :width="width"
    :height="height"
    :cover="cover"
    referrerpolicy="no-referrer"
    class="productImage"
  >
    <template #error>
      <div class="productImageFallback">
        <v-icon icon="mdi-image-off-outline" :size="fallbackIconSize"></v-icon>
      </div>
    </template>
  </v-img>
  <div v-else class="productImage productImageFallback" :style="emptyImageStyle">
    <v-icon icon="mdi-image-off-outline" :size="fallbackIconSize"></v-icon>
  </div>
</template>

<script setup lang="ts">
import { computed } from 'vue'

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
const emptyImageStyle = computed(() => ({
  width: toCssSize(props.width),
  height: toCssSize(props.height)
}))
</script>

<style scoped>
.productImage {
  border: 1px solid rgba(var(--v-border-color), var(--v-border-opacity));
  border-radius: 6px;
  overflow: hidden;
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
</style>
