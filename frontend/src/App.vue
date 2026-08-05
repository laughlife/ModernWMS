<template>
  <div class="appViewContainer">
    <v-dialog v-model="loadingFlag" :scrim="false" persistent max-width="200">
      <v-card color="primary">
        <v-card-text>
          加载中...
          <v-progress-linear indeterminate color="white"></v-progress-linear>
        </v-card-text>
      </v-card>
    </v-dialog>
    <div v-show="loadingFlag" class="mask"></div>
    <router-view></router-view>
  </div>
</template>

<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { emitter } from './utils/bus'
import { useSystemStore } from './store/module/system'

const loadingFlag = ref(false)
const systemStore = useSystemStore()

function updateClientSize() {
  const clientHeight = document.documentElement.clientHeight
  const clientWidth = document.documentElement.clientWidth
  systemStore.setClientHeight(clientHeight)
  systemStore.setClientWidth(clientWidth)
}

onMounted(() => {
  emitter.on('showLoading', () => {
    loadingFlag.value = true
  })
  emitter.on('closeLoading', () => {
    loadingFlag.value = false
  })

  updateClientSize()

  window.onresize = function () {
    updateClientSize()
  }
})
</script>

<style scoped>
.appViewContainer {
  height: 100%;
  width: 100%;
  background-color: #f4f5fa;
}

.mask {
  position: absolute;
  z-index: 9999;
  width: 100vw;
  height: 100vh;
}
</style>
