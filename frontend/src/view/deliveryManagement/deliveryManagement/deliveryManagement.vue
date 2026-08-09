<template>
  <div class="container">
    <v-card>
      <v-card-title class="pageTitle">
        <v-icon icon="mdi-truck-fast-outline" size="24"></v-icon>
        <span>{{ $t('wms.deliveryManagement.fbaShipment') }}</span>
      </v-card-title>
      <v-card-text>
        <FbaShipmentList />
      </v-card-text>
    </v-card>
  </div>
</template>

<script lang="ts" setup>
import FbaShipmentList from './fba-shipment-list.vue'
</script>

<style scoped lang="less">
.pageTitle {
  display: flex;
  gap: 10px;
  align-items: center;
  padding: 18px 24px 8px;
}
</style>
