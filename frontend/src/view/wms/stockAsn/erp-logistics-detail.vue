<template>
  <v-dialog v-model="data.showDialog" width="1400" max-width="calc(100vw - 48px)" transition="dialog-top-transition">
    <v-card max-height="85vh">
      <v-toolbar color="white" :title="$t('wms.erpPendingReceipt.logistics_detail_title')">
        <template #append>
          <v-btn icon="mdi-close" variant="text" @click="method.closeDialog"></v-btn>
        </template>
      </v-toolbar>
      <v-divider></v-divider>
      <v-progress-linear v-if="data.loading" indeterminate color="primary"></v-progress-linear>
      <v-card-text class="pa-6 overflow-y-auto">
        <div v-for="item in detailItems" :key="item.label" class="detailRow">
          <span class="detailLabel">{{ item.label }}</span>
          <span class="detailValue">{{ item.value || '-' }}</span>
        </div>
        <v-divider class="my-4"></v-divider>
        <div class="timelineTitle">{{ $t('wms.erpPendingReceipt.logistics_timeline') }}</div>
        <div v-if="data.detail?.event_list.length" class="timeline">
          <div v-for="event in data.detail.event_list" :key="event.id" class="timelineItem">
            <div class="timelineDot"></div>
            <div class="timelineContent">
              <div class="timelineMeta">
                <span>{{ event.event_time || '-' }}</span>
                <span v-if="event.status_name || event.stage">{{ event.status_name || event.stage }}</span>
              </div>
              <div>{{ event.description || '-' }}</div>
              <div v-if="event.location" class="timelineLocation">{{ event.location }}</div>
            </div>
          </div>
        </div>
        <div v-else-if="!data.loading" class="noTimeline">{{ $t('system.page.noData') }}</div>
      </v-card-text>
      <v-divider></v-divider>
      <v-card-actions class="justify-end px-6 py-4">
        <v-btn variant="text" @click="method.closeDialog">{{ $t('system.page.close') }}</v-btn>
      </v-card-actions>
    </v-card>
  </v-dialog>
</template>

<script lang="ts" setup>
import { computed, reactive } from 'vue'
import { getErpReceiptLogistics } from '@/api/wms/stockAsn'
import { hookComponent } from '@/components/system'
import i18n from '@/languages/i18n'
import type { ErpPendingReceiptLogisticsVO, ErpPendingReceiptVO } from '@/types/WMS/StockAsn'

const data = reactive({
  showDialog: false,
  loading: false,
  currentRow: null as ErpPendingReceiptVO | null,
  detail: null as ErpPendingReceiptLogisticsVO | null
})

const detailItems = computed(() => [
  { label: i18n.global.t('wms.erpPendingReceipt.logistics_name'), value: data.detail?.logistics_name ?? data.currentRow?.logistics_name },
  { label: i18n.global.t('wms.erpPendingReceipt.tracking_no'), value: data.detail?.tracking_no ?? data.currentRow?.tracking_no },
  { label: i18n.global.t('wms.erpPendingReceipt.tracking_status'), value: data.detail?.tracking_status_name ?? data.currentRow?.tracking_status_name },
  { label: i18n.global.t('wms.erpPendingReceipt.latest_event_desc'), value: data.detail?.latest_event_desc ?? data.currentRow?.latest_event_desc },
  { label: i18n.global.t('wms.erpPendingReceipt.latest_event_time'), value: data.detail?.latest_event_time ?? data.currentRow?.latest_event_time },
  { label: i18n.global.t('wms.erpPendingReceipt.latest_event_location'), value: data.detail?.latest_event_location ?? data.currentRow?.latest_event_location },
  { label: i18n.global.t('wms.erpPendingReceipt.estimated_delivery_time'), value: data.detail?.estimated_delivery_time ?? data.currentRow?.estimated_delivery_time },
  { label: i18n.global.t('wms.erpPendingReceipt.actual_delivery_time'), value: data.detail?.actual_delivery_time ?? data.currentRow?.actual_delivery_time }
])

const method = reactive({
  openDialog: async (row: ErpPendingReceiptVO) => {
    data.currentRow = row
    data.detail = null
    data.showDialog = true
    data.loading = true
    try {
      const { data: res } = await getErpReceiptLogistics(row.id)
      if (!res.isSuccess) {
        hookComponent.$message({ type: 'error', content: res.errorMessage })
        return
      }
      data.detail = res.data
    } finally {
      data.loading = false
    }
  },
  closeDialog: () => {
    data.showDialog = false
  }
})

defineExpose({
  openDialog: method.openDialog,
  closeDialog: method.closeDialog
})
</script>

<style lang="less" scoped>
.detailRow {
  display: grid;
  grid-template-columns: 150px minmax(0, 1fr);
  min-height: 44px;
  align-items: start;
}

.detailLabel {
  color: rgba(var(--v-theme-on-surface), 0.68);
  padding-right: 20px;
  text-align: right;
}

.detailValue {
  color: rgb(var(--v-theme-on-surface));
  overflow-wrap: anywhere;
}

.timelineTitle {
  color: rgb(var(--v-theme-on-surface));
  font-weight: 600;
  margin-bottom: 16px;
}

.timeline {
  margin-left: 8px;
}

.timelineItem {
  display: grid;
  grid-template-columns: 18px minmax(0, 1fr);
  gap: 10px;
  position: relative;
  padding-bottom: 20px;
}

.timelineItem:not(:last-child)::before {
  background: rgba(var(--v-theme-on-surface), 0.18);
  content: '';
  height: 100%;
  left: 5px;
  position: absolute;
  top: 10px;
  width: 1px;
}

.timelineDot {
  background: rgb(var(--v-theme-primary));
  border-radius: 50%;
  height: 11px;
  margin-top: 5px;
  position: relative;
  width: 11px;
  z-index: 1;
}

.timelineMeta {
  color: rgba(var(--v-theme-on-surface), 0.65);
  display: flex;
  font-size: 12px;
  gap: 12px;
  justify-content: space-between;
  margin-bottom: 4px;
}

.timelineLocation,
.noTimeline {
  color: rgba(var(--v-theme-on-surface), 0.58);
  font-size: 12px;
  margin-top: 4px;
}
</style>
