<template>
  <div class="operateArea">
    <v-row no-gutters>
      <!-- Operate Btn -->
      <v-col cols="3" class="col">
        <!-- <tooltip-btn icon="mdi-refresh" :tooltip-text="$t('system.page.refresh')" @click="method.refresh"></tooltip-btn>
        <tooltip-btn icon="mdi-export-variant" :tooltip-text="$t('system.page.export')" @click="method.exportTable"> </tooltip-btn> -->

        <BtnGroup :authority-list="data.authorityList" :btn-list="data.btnList" />
      </v-col>

      <!-- Search Input -->
      <v-col cols="9">
        <v-row no-gutters @keyup.enter="method.sureSearch">
          <v-col cols="4">
            <v-text-field
              v-model="data.searchForm.dispatch_no"
              clearable
              hide-details
              density="comfortable"
              class="searchInput ml-5 mt-1"
              :label="$t('wms.deliveryManagement.dispatch_no')"
              variant="solo"
            >
            </v-text-field>
          </v-col>
          <v-col cols="4">
            <v-text-field
              v-model="data.searchForm.spu_name"
              clearable
              hide-details
              density="comfortable"
              class="searchInput ml-5 mt-1"
              :label="$t('wms.deliveryManagement.spu_name')"
              variant="solo"
            >
            </v-text-field>
          </v-col>
        </v-row>
      </v-col>
    </v-row>
  </div>

  <!-- Table -->
  <div
    class="mt-5"
    :style="{
      height: cardHeight
    }"
  >
    <vxe-table
      ref="xTable"
      :column-config="{ minWidth: '100px' }"
      :checkbox-config="{
        checkMethod: method.getCheckBoxDisableState,
        visibleMethod: method.getCheckBoxDisableState
      }"
      :data="data.tableData"
      :height="tableHeight"
      align="center"
    >
      <template #empty>
        {{ i18n.global.t('system.page.noData') }}
      </template>
      <vxe-column type="seq" width="60"></vxe-column>
      <vxe-column type="checkbox" width="50"></vxe-column>
      <vxe-column :title="$t('wms.deliveryManagement.state')">
        <template #default="{ row }">
          <span>{{ `${row.is_todo ? $t('wms.deliveryManagement.weighTodo') : $t('wms.deliveryManagement.weighReady')}` }}</span>
        </template>
      </vxe-column>
      <vxe-column field="main_image" :title="$t('wms.deliveryManagement.productImage')" width="92">
        <template #default="{ row }">
          <ProductImage :src="row.main_image" :alt="row.commodity_name || row.spu_name" :width="56" :height="56" />
        </template>
      </vxe-column>
      <vxe-column field="commodity_name" :title="$t('wms.deliveryManagement.productInfo')" min-width="280" align="left" header-align="left">
        <template #default="{ row }">
          <div class="product-info-cell">
            <div class="primary-text">{{ row.commodity_name || row.spu_name || '-' }}</div>
            <div class="secondary-text">{{ $t('wms.deliveryManagement.fnSku') }}：{{ row.fba_sku || '-' }}</div>
            <div class="secondary-text">{{ row.shop_name || '-' }}</div>
          </div>
        </template>
      </vxe-column>
      <vxe-column field="dept_name" :title="$t('wms.deliveryManagement.shippingPersonnel')" min-width="180">
        <template #default="{ row }">
          <div class="primary-text">{{ row.dept_name || '-' }}</div>
          <div class="secondary-text">{{ row.order_user_name || '-' }}</div>
        </template>
      </vxe-column>
      <vxe-column field="qty" :title="$t('wms.deliveryManagement.quantityVariant')" width="130">
        <template #default="{ row }">
          <div class="primary-text">{{ row.qty ?? 0 }} {{ $t('wms.deliveryManagement.pieceUnit') }}</div>
          <div class="secondary-text">{{ row.variant_qty ?? 1 }} {{ $t('wms.deliveryManagement.variantLabel') }}</div>
        </template>
      </vxe-column>
      <vxe-column field="weighing_weight" :title="$t('wms.deliveryManagement.weighing_weight')" width="130">
        <template #default="{ row }">{{ method.formatWeight(row) }}</template>
      </vxe-column>
      <vxe-column field="weighing_length" :title="$t('wms.deliveryManagement.dimensionsCm')" min-width="180">
        <template #default="{ row }">
          {{ row.weighing_length && row.weighing_width && row.weighing_height
            ? `${row.weighing_length} × ${row.weighing_width} × ${row.weighing_height}`
            : '-' }}
        </template>
      </vxe-column>
      <vxe-column field="weighing_volume" :title="$t('wms.deliveryManagement.volumeCm3')" width="140">
        <template #default="{ row }">{{ row.weighing_volume || '-' }}</template>
      </vxe-column>
      <vxe-column field="operate" :title="$t('system.page.operate')" width="140" :resizable="false" show-overflow>
        <template #default="{ row }">
          <div style="width: 100%; display: flex; justify-content: center">
            <tooltip-btn :flat="true" icon="mdi-eye-outline" :tooltip-text="$t('system.page.view')" @click="method.viewRow(row)"></tooltip-btn>
            <tooltip-btn
              :flat="true"
              icon="mdi-arrow-u-left-top"
              :tooltip-text="$t('wms.deliveryManagement.backToThePreviousStep')"
              :disabled="!data.authorityList.includes('weighed-revoke')"
              @click="method.backToThePreviousStep(row)"
            ></tooltip-btn>
          </div>
        </template>
      </vxe-column>
    </vxe-table>
    <custom-pager
      :current-page="data.tablePage.pageIndex"
      :page-size="data.tablePage.pageSize"
      perfect
      :total="data.tablePage.total"
      :page-sizes="PAGE_SIZE"
      :layouts="PAGE_LAYOUT"
      @page-change="method.handlePageChange"
    >
    </custom-pager>
    <SearchDeliveredDetail :id="data.showDeliveredDetailID" :show-dialog="data.showDeliveredDetail" @close="method.closeDeliveredDetail" />
    <WeightConfirm ref="WeightConfirmRef" :dialog-title="$t('wms.deliveryManagement.weigh')" :is-weight="true" @submit="method.dialogSubmit" />
  </div>
</template>

<script lang="ts" setup>
import { computed, ref, reactive, watch, onMounted } from 'vue'
import { VxePagerEvents } from 'vxe-table'
import { computedCardHeight, computedTableHeight } from '@/constant/style'
import { DeliveryManagementDetailVO, ConfirmItem } from '@/types/DeliveryManagement/DeliveryManagement'
import { PAGE_SIZE, PAGE_LAYOUT, DEFAULT_PAGE_SIZE } from '@/constant/vxeTable'
import { hookComponent } from '@/components/system'
import { getWeighed, undoWeighing, handleWeigh } from '@/api/wms/deliveryManagement'
import tooltipBtn from '@/components/tooltip-btn.vue'
import i18n from '@/languages/i18n'
import customPager from '@/components/custom-pager.vue'
import { setSearchObject, getMenuAuthorityList } from '@/utils/common'
import { TablePage, btnGroupItem } from '@/types/System/Form'
import SearchDeliveredDetail from './search-delivered-detail.vue'
import { exportData } from '@/utils/exportTable'
import { DEBOUNCE_TIME } from '@/constant/system'
import BtnGroup from '@/components/system/btnGroup.vue'
import ProductImage from '@/components/system/product-image.vue'
import WeightConfirm from './package-confirm.vue'
import { httpCodeJudge } from '@/utils/http/httpCodeJudge'

const xTable = ref()
const WeightConfirmRef = ref()

const data = reactive({
  showDeliveredDetailID: 0,
  showDeliveredDetail: false,
  showDialog: false,
  dialogForm: {
    id: 0
  },
  searchForm: {
    dispatch_no: '',
    spu_name: ''
  },
  timer: ref<any>(null),
  activeTab: null,
  tableData: ref<DeliveryManagementDetailVO[]>([]),
  tablePage: ref<TablePage>({
    total: 0,
    pageIndex: 1,
    pageSize: DEFAULT_PAGE_SIZE,
    searchObjects: []
  }),
  btnList: [] as btnGroupItem[],
  // Menu operation permissions
  authorityList: getMenuAuthorityList()
})

const method = reactive({
  formatWeight: (row: DeliveryManagementDetailVO): string => {
    if (!row.weighing_weight) return '-'
    return `${row.weighing_weight} kg`
  },
  closeDeliveredDetail: () => {
    data.showDeliveredDetail = false
  },
  viewRow: (row: DeliveryManagementDetailVO) => {
    data.showDeliveredDetailID = row.id
    data.showDeliveredDetail = true
  },
  // Back to the previous step
  backToThePreviousStep(row: DeliveryManagementDetailVO) {
    hookComponent.$dialog({
      content: `${ i18n.global.t('wms.deliveryManagement.confirmBack') }?`,
      handleConfirm: async () => {
        const { data: res } = await undoWeighing(row.id)
        if (!res.isSuccess) {
          hookComponent.$message({
            type: 'error',
            content: res.errorMessage
          })
          return
        }
        hookComponent.$message({
          type: 'success',
          content: res.data
        })
        method.refresh()
      }
    })
  },
  // Refresh data
  refresh: () => {
    method.getWeighed()
  },
  getWeighed: async () => {
    const { data: res } = await getWeighed(data.tablePage)
    if (!res.isSuccess) {
      hookComponent.$message({
        type: 'error',
        content: res.errorMessage
      })
      return
    }
    data.tableData = res.data.rows
    data.tablePage.total = res.data.totals
  },
  handlePageChange: ref<VxePagerEvents.PageChange>(({ currentPage, pageSize }) => {
    data.tablePage.pageIndex = currentPage
    data.tablePage.pageSize = pageSize

    method.getWeighed()
  }),
  exportTable: () => {
    const $table = xTable.value
    exportData({
      table: $table,
      filename: i18n.global.t('wms.deliveryManagement.weighed'),
      columnFilterMethod({ column }: any) {
        return !['checkbox'].includes(column?.type) && !['operate'].includes(column?.field)
      }
    })
  },
  sureSearch: () => {
    data.tablePage.searchObjects = setSearchObject(data.searchForm)
    method.getWeighed()
  },
  handleWeigh: async () => {
    const $table = xTable.value
    const checkTableList = $table.getCheckboxRecords()
    const confirmList: ConfirmItem[] = []
    if (checkTableList.length > 0) {
      // Processing the data required by the window
      for (const item of checkTableList) {
        confirmList.push({
          id: item.id,
          spu_name: item.spu_name,
          spu_code: item.spu_code,
          sku_code: item.sku_code,
          maxQty: item.unweighing_qty,
          qty: item.unweighing_qty,
          weight: 0,
          weighing_length: 0,
          weighing_width: 0,
          weighing_height: 0,
          weighing_volume: 0,
          dispatch_no: item.dispatch_no,
          dispatch_status: item.dispatch_status,
          picked_qty: item.picked_qty
        })
      }
      // data.confirmList = confirmList
      // data.showDialog = true

      WeightConfirmRef.value.openDialog(confirmList)
    } else {
      hookComponent.$message({
        type: 'error',
        content: `${ i18n.global.t('base.userManagement.checkboxIsNull') }`
      })
    }
    // data.weighedRow = row
    // data.dialogWeightUnit = row.weight_unit !== undefined ? GetUnit('weight', row.weight_unit) : ''
    // data.dialogMaxQty = row.unweighing_qty ? row.unweighing_qty : 0
    // data.defaultWeight = row.weight ? row.weight : 0
    // data.showDialog = true
  },
  // Callback after entering packaging value
  dialogSubmit: async (list: ConfirmItem[]) => {
    const packList = list.map((item) => ({
      id: item.id,
      dispatch_no: item.dispatch_no,
      dispatch_status: item.dispatch_status,
      weighing_qty: item.qty,
      weighing_weight: item.weight,
      weighing_length: item.weighing_length,
      weighing_width: item.weighing_width,
      weighing_height: item.weighing_height,
      picked_qty: item.picked_qty
    }))
    // if (data.weighedRow) {
    const { data: res } = await handleWeigh(packList)
    if (!res.isSuccess) {
      // 2023-12-06 Add automatic refresh of expired data
      if (httpCodeJudge(res.errorMessage)) {
        method.refresh()

        WeightConfirmRef.value.closeDialog()

        return
      }

      hookComponent.$message({
        type: 'error',
        content: res.errorMessage
      })
      return
    }
    hookComponent.$message({
      type: 'success',
      content: res.data
    })
    // method.dialogClose()
    WeightConfirmRef.value.closeDialog()

    method.refresh()
    // }
  },
  // Check if the checkbox can be checked
  getCheckBoxDisableState: ({ row }: { row: DeliveryManagementDetailVO }): boolean => row.is_todo
})

onMounted(() => {
  data.btnList = [
    {
      name: i18n.global.t('system.page.refresh'),
      icon: 'mdi-refresh',
      code: '',
      click: method.refresh
    },
    {
      name: i18n.global.t('system.page.export'),
      icon: 'mdi-export-variant',
      code: 'weighed-export',
      click: method.exportTable
    },
    {
      name: i18n.global.t('wms.deliveryManagement.weigh'),
      icon: 'mdi-weight',
      code: 'weighed-weigh',
      click: method.handleWeigh
    }
  ]
})

const cardHeight = computed(() => computedCardHeight({}))
const tableHeight = computed(() => computedTableHeight({}))

watch(
  () => data.searchForm,
  () => {
    // debounce
    if (data.timer) {
      clearTimeout(data.timer)
    }
    data.timer = setTimeout(() => {
      data.timer = null
      method.sureSearch()
    }, DEBOUNCE_TIME)
  },
  {
    deep: true
  }
)

defineExpose({
  getWeighed: method.getWeighed
})
</script>

<style lang="less" scoped>
.operateArea {
  width: 100%;
  min-width: 760px;
  display: flex;
  align-items: center;
  border-radius: 10px;
  padding: 0 10px;
}

.col {
  display: flex;
  align-items: center;
}

.product-info-cell { line-height: 22px; }
.primary-text { font-weight: 600; color: rgba(var(--v-theme-on-surface), 0.9); }
.secondary-text { margin-top: 2px; color: rgba(var(--v-theme-on-surface), 0.62); }
</style>
