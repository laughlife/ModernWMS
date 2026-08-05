<template>
  <div class="appViewContainer">
    <v-dialog v-model="loadingFlag" :scrim="false" persistent max-width="200">
      <v-card color="primary">
        <v-card-text>
          Loading...
          <v-progress-linear indeterminate color="white"></v-progress-linear>
        </v-card-text>
      </v-card>
    </v-dialog>
    <div v-show="loadingFlag" class="mask"></div>
    <router-view v-if="data.isShow"></router-view>
  </div>
</template>

<script setup lang="ts">
import { reactive, nextTick, onMounted, ref, computed, watch } from 'vue'
import { emitter } from './utils/bus'
import { useSystemStore } from './store/module/system'
import { installNewLang } from '@/languages/method/index'

const loadingFlag = ref(false)
const systemStore = useSystemStore()

const data = reactive({
  isShow: true // Used to refresh the interface when switching languages
})

const method = reactive({
  refreshSystem: () => {
    data.isShow = false
    installNewLang()
    nextTick(() => {
      data.isShow = true
      systemStore.setRefreshFlag(false)
    })
  },
  getClientSize: () => {
    const clientHeight = document.documentElement.clientHeight
    const clientWidth = document.documentElement.clientWidth
    systemStore.setClientHeight(clientHeight)
    systemStore.setClientWidth(clientWidth)
  }
})

const refreshFlag = computed(() => systemStore.refreshFlag)

watch(
  () => refreshFlag.value,
  (flag) => {
    if (flag) {
      method.refreshSystem()
    }
  }
)

onMounted(() => {
  emitter.on('showLoading', () => {
    loadingFlag.value = true
  })
  emitter.on('closeLoading', () => {
    loadingFlag.value = false
  })

  method.getClientSize()

  window.onresize = function () {
    method.getClientSize()
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
