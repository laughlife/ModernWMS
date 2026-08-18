<template>
  <div class="flowTip">
    <v-icon icon="mdi-information-outline" size="20"></v-icon>
    <span>{{ $t('wms.deliveryManagement.fbaFlowTip') }}</span>
  </div>

  <div class="operateArea">
    <v-row no-gutters>
      <v-col cols="3" class="col">
        <BtnGroup :authority-list="data.authorityList" :btn-list="data.btnList" />
      </v-col>
      <v-col cols="9">
        <v-row no-gutters @keyup.enter="method.sureSearch">
          <v-col cols="4">
            <v-text-field
              v-model="data.searchForm.dept_name"
              clearable hide-details density="comfortable"
              class="searchInput ml-5 mt-1"
              :label="$t('wms.deliveryManagement.deptName')" variant="solo"
            ></v-text-field>
          </v-col>
          <v-col cols="4">
            <v-text-field
              v-model="data.searchForm.order_user_name"
              clearable hide-details density="comfortable"
              class="searchInput ml-5 mt-1"
              :label="$t('wms.deliveryManagement.orderUserName')" variant="solo"
            ></v-text-field>
          </v-col>
          <v-col cols="4">
            <v-text-field
              v-model="data.searchForm.keyword"
              clearable hide-details density="comfortable"
              class="searchInput ml-5 mt-1"
              :label="$t('wms.deliveryManagement.fbaKeyword')" variant="solo"
            ></v-text-field>
          </v-col>
        </v-row>
      </v-col>
    </v-row>
  </div>

  <div class="mt-5" :style="{ height: cardHeight }">
    <vxe-table ref="xTable" :column-config="{ minWidth: '120px' }" :data="data.tableData" :height="tableHeight" align="center">
      <template #empty>
        <div class="emptyState">
          <v-icon icon="mdi-truck-outline" size="38"></v-icon>
          <div>{{ $t('wms.deliveryManagement.noFbaShipment') }}</div>
        </div>
      </template>
      <vxe-column type="seq" width="60"></vxe-column>
      <vxe-column type="expand" width="60">
        <template #content="{ row }">
          <div class="productDetail">
            <v-table density="compact">
              <thead>
                <tr>
                  <th></th>
                  <th>{{ $t('wms.deliveryManagement.productInfo') }}</th>
                  <th>{{ $t('wms.deliveryManagement.stockSku') }}</th>
                  <th>{{ $t('wms.deliveryManagement.fbaSku') }}</th>
                  <th>{{ $t('wms.deliveryManagement.fbaQty') }}</th>
                  <th>{{ $t('wms.deliveryManagement.shipmentTotalQty') }}</th>
                  <th>{{ $t('wms.deliveryManagement.stockOccupiedQty') }}</th>
                  <th>{{ $t('wms.deliveryManagement.inventoryCheck') }}</th>
                </tr>
              </thead>
              <tbody>
                <tr v-for="item in row.item_list" :key="item.stock_move_item_id">
                  <td class="detailImageCell">
                    <ProductImage :src="item.main_image" :alt="item.commodity_name || item.stock_sku" :width="56" :height="56" :cover="false" />
                  </td>
                  <td class="detailProductCell">
                    <div class="primaryText">{{ item.commodity_name || '-' }}</div>
                    <div class="secondaryText">{{ item.commodity_id || '-' }}</div>
                  </td>
                  <td>{{ item.stock_sku || '-' }}</td>
                  <td>{{ item.fba_sku || '-' }}</td>
                  <td>{{ item.qty }} × {{ item.variant_qty }}</td>
                  <td>{{ item.shipment_total_qty }}</td>
                  <td>{{ item.stock_occupied_qty }}</td>
                  <td>
                    <v-chip :color="item.inventory_ready ? 'success' : 'error'" size="small" variant="tonal">
                      {{ item.inventory_ready ? $t('wms.deliveryManagement.inventoryReady') : $t('wms.deliveryManagement.inventoryMismatch') }}
                    </v-chip>
                  </td>
                </tr>
              </tbody>
            </v-table>
          </div>
        </template>
      </vxe-column>
      <vxe-column width="96">
        <template #default="{ row }">
          <div class="mainImageCell">
            <ProductImage
              :src="row.item_list[0]?.main_image"
              :alt="row.item_list[0]?.commodity_name || row.fba_no"
              :width="64" :height="64"
              :cover="false"
            />
          </div>
        </template>
      </vxe-column>
      <vxe-column :title="$t('wms.deliveryManagement.fbaShipmentInfo')" min-width="230">
        <template #default="{ row }">
          <div class="leftCell">
            <div class="primaryText">{{ row.fba_no || '-' }}</div>
            <div class="secondaryText">{{ row.shipment_name || row.stock_move_no }}</div>
          </div>
        </template>
      </vxe-column>
      <vxe-column :title="$t('wms.deliveryManagement.destinationInfo')" min-width="155">
        <template #default="{ row }">
          <div class="leftCell">
            <div class="primaryText">{{ row.fulfillment_center_id || '-' }}</div>
            <div class="secondaryText">{{ row.marketplace_name || '-' }}</div>
          </div>
        </template>
      </vxe-column>
      <vxe-column :title="$t('wms.deliveryManagement.shopInfo')" min-width="220">
        <template #default="{ row }">
          <div class="leftCell">
            <div class="primaryText">{{ row.shop_name || '-' }}</div>
            <div class="secondaryText">{{ row.fba_status || '-' }}</div>
            <div class="secondaryText">{{ method.formatOwner(row) }}</div>
          </div>
        </template>
      </vxe-column>
      <vxe-column :title="$t('wms.deliveryManagement.productQuantity')" min-width="220" align="left" :show-overflow="false">
        <template #default="{ row }">
          <div class="leftCell combinedInfo">
            <div>{{ $t('wms.deliveryManagement.productLabel') }}：{{ row.product_count }} {{ $t('wms.deliveryManagement.productUnit') }}</div>
            <div>{{ $t('wms.deliveryManagement.quantityLabel') }}：{{ row.shipment_total_qty }} {{ $t('wms.deliveryManagement.pieceUnit') }}</div>
          </div>
        </template>
      </vxe-column>
      <vxe-column :title="$t('wms.deliveryManagement.freightForwarder')" min-width="150">
        <template #default="{ row }">
          {{ row.freight_forwarder_name || row.logistics_name || '-' }}
        </template>
      </vxe-column>
      <vxe-column field="prepared_time" :title="$t('wms.deliveryManagement.preparedTime')" min-width="170"></vxe-column>
      <vxe-column field="creator" :title="$t('wms.deliveryManagement.creator')" min-width="140"></vxe-column>
      <vxe-column :title="$t('wms.deliveryManagement.inventoryStatus')" width="135">
        <template #default="{ row }">
          <v-chip :color="row.inventory_ready ? 'success' : 'warning'" size="small" variant="tonal">
            {{ row.inventory_status_name }}
          </v-chip>
        </template>
      </vxe-column>
      <vxe-column field="operate" :title="$t('system.page.operate')" width="130" fixed="right" :resizable="false">
        <template #default="{ row }">
          <v-btn
            color="primary"
            size="small"
            variant="flat"
            :disabled="!row.inventory_ready || data.preparingId !== null"
            :loading="data.preparingId === row.stock_move_id"
            @click="method.preparePicking(row)"
          >
            {{ $t('wms.deliveryManagement.preparePicking') }}
          </v-btn>
        </template>
      </vxe-column>
    </vxe-table>
    <custom-pager
      :current-page="data.tablePage.pageIndex" :page-size="data.tablePage.pageSize"
      perfect :total="data.tablePage.total" :page-sizes="PAGE_SIZE" :layouts="PAGE_LAYOUT"
      @page-change="method.handlePageChange"
    ></custom-pager>
  </div>
</template>

<script lang="ts" setup>
import { computed, onMounted, reactive, ref, watch } from 'vue'
import type { VxePagerEvents } from 'vxe-table'
import { getFbaShipmentPage, prepareFbaShipmentPicking } from '@/api/wms/fbaShipment'
import { hookComponent } from '@/components/system'
import BtnGroup from '@/components/system/btnGroup.vue'
import ProductImage from '@/components/system/product-image.vue'
import customPager from '@/components/custom-pager.vue'
import { computedCardHeight, computedTableHeight } from '@/constant/style'
import { DEFAULT_PAGE_SIZE, PAGE_LAYOUT, PAGE_SIZE } from '@/constant/vxeTable'
import { DEBOUNCE_TIME } from '@/constant/system'
import i18n from '@/languages/i18n'
import type { FbaShipmentVO } from '@/types/DeliveryManagement/FbaShipment'
import type { btnGroupItem, SearchObject } from '@/types/System/Form'
import { getMenuAuthorityList, setSearchObject } from '@/utils/common'
import { exportData } from '@/utils/exportTable'

const xTable = ref()
const emit = defineEmits<{ statusChanged: [] }>()
const data = reactive({
  searchForm: { dept_name: '', order_user_name: '', keyword: '' },
  tableData: ref<FbaShipmentVO[]>([]),
  preparingId: null as number | null,
  tablePage: reactive({
    total: 0,
    pageIndex: 1,
    pageSize: DEFAULT_PAGE_SIZE,
    searchObjects: ref<SearchObject[]>([])
  }),
  timer: ref<ReturnType<typeof setTimeout> | null>(null),
  btnList: [] as btnGroupItem[],
  authorityList: getMenuAuthorityList()
})

const method = reactive({
  formatOwner: (row: FbaShipmentVO) => {
    const owner = [row.dept_name, row.order_user_name].filter(Boolean).join('/')
    return owner || '-'
  },
  preparePicking: (row: FbaShipmentVO) => {
    hookComponent.$dialog({
      content: i18n.global.t('wms.deliveryManagement.preparePickingConfirm'),
      handleConfirm: async () => {
        data.preparingId = row.stock_move_id
        try {
          const { data: res } = await prepareFbaShipmentPicking(row.stock_move_id)
          if (!res.isSuccess) {
            hookComponent.$message({ type: 'error', content: res.errorMessage })
            return
          }
          hookComponent.$message({
            type: 'success',
            content: i18n.global.t('wms.deliveryManagement.preparePickingSuccess')
          })
          await method.getPage()
          emit('statusChanged')
        } finally {
          data.preparingId = null
        }
      }
    })
  },
  getPage: async () => {
    const { data: res } = await getFbaShipmentPage(data.tablePage)
    if (!res.isSuccess) {
      hookComponent.$message({ type: 'error', content: res.errorMessage })
      return
    }
    data.tableData = res.data.rows
    data.tablePage.total = res.data.totals
  },
  refresh: () => method.getPage(),
  handlePageChange: ref<VxePagerEvents.PageChange>(({ currentPage, pageSize }) => {
    data.tablePage.pageIndex = currentPage
    data.tablePage.pageSize = pageSize
    method.getPage()
  }),
  sureSearch: () => {
    data.tablePage.pageIndex = 1
    data.tablePage.searchObjects = setSearchObject(data.searchForm)
    method.getPage()
  },
  exportTable: () => {
    exportData({
      table: xTable.value,
      filename: i18n.global.t('wms.deliveryManagement.fbaShipment'),
      columnFilterMethod({ column }: any) {
        return !['expand'].includes(column?.type) && column?.field !== 'operate'
      }
    })
  }
})

onMounted(() => {
  data.btnList = [
    { name: i18n.global.t('system.page.refresh'), icon: 'mdi-refresh', code: '', click: method.refresh },
    { name: i18n.global.t('system.page.export'), icon: 'mdi-export-variant', code: 'invoice-export', click: method.exportTable }
  ]
  method.getPage()
})

const cardHeight = computed(() => computedCardHeight({ hasTab: false, hasOperateBtn: false }))
const tableHeight = computed(() => computedTableHeight({ hasTab: false, hasOperateBtn: false }))

watch(
  () => data.searchForm,
  () => {
    if (data.timer) clearTimeout(data.timer)
    data.timer = setTimeout(() => {
      data.timer = null
      method.sureSearch()
    }, DEBOUNCE_TIME)
  },
  { deep: true }
)

defineExpose({ getFbaShipment: method.getPage })
</script>

<style lang="less" scoped>
.flowTip {
  display: flex;
  gap: 8px;
  align-items: center;
  margin-bottom: 12px;
  padding: 10px 14px;
  border-radius: 6px;
  color: rgb(var(--v-theme-info));
  background: rgba(var(--v-theme-info), 0.1);
}

.operateArea {
  width: 100%;
  min-width: 760px;
  padding: 0 10px;
  border-radius: 10px;
}

.col,
.mainImageCell {
  display: flex;
  align-items: center;
}

.mainImageCell {
  justify-content: center;
  padding: 6px 0;
}

.leftCell,
.detailProductCell {
  text-align: left;
}

.primaryText {
  color: rgba(var(--v-theme-on-surface), 0.87);
  font-weight: 500;
}

.secondaryText {
  margin-top: 4px;
  color: rgba(var(--v-theme-on-surface), 0.6);
  font-size: 12px;
}

.combinedInfo {
  padding: 8px 0;
  line-height: 22px;
  overflow-wrap: anywhere;
  white-space: normal;
}

.productDetail {
  padding: 12px 72px;
}

.detailImageCell {
  width: 76px;
  padding: 6px 10px !important;
}

.emptyState {
  display: flex;
  flex-direction: column;
  gap: 8px;
  align-items: center;
  padding: 28px;
  color: rgba(var(--v-theme-on-surface), 0.55);
}
</style>
