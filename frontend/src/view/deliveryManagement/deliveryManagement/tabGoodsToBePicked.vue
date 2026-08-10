<template>
  <div class="operateArea">
    <v-row no-gutters>
      <v-col cols="4" class="col">
        <BtnGroup :authority-list="data.authorityList" :btn-list="data.btnList" />
      </v-col>
      <v-col cols="8">
        <v-row no-gutters @keyup.enter="method.sureSearch">
          <v-col cols="6">
            <v-text-field
              v-model="data.searchForm.spu_name"
              clearable
              hide-details
              density="comfortable"
              class="searchInput ml-5 mt-1"
              :label="$t('wms.deliveryManagement.spu_name')"
              variant="solo"
            ></v-text-field>
          </v-col>
        </v-row>
      </v-col>
    </v-row>
  </div>

  <div class="mt-5" :style="{ height: cardHeight }">
    <vxe-table ref="xTable" :column-config="{ minWidth: '120px' }" :data="data.tableData" :height="tableHeight" align="center">
      <template #empty>{{ i18n.global.t('system.page.noData') }}</template>
      <vxe-column type="checkbox" width="52"></vxe-column>
      <vxe-column field="main_image" :title="$t('wms.deliveryManagement.productImage')" width="132">
        <template #default="{ row }">
          <ProductImage :src="row.main_image" :alt="row.commodity_name || row.spu_name" :width="112" :height="112" />
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
      <vxe-column field="qty" :title="$t('wms.deliveryManagement.quantityLabel')" width="100"></vxe-column>
      <vxe-column field="variant_qty" :title="$t('wms.deliveryManagement.variantLabel')" width="100">
        <template #default="{ row }">{{ row.variant_qty ?? 1 }} {{ $t('wms.deliveryManagement.variantLabel') }}</template>
      </vxe-column>
      <vxe-date-column
        field="prepared_time"
        width="170"
        format="yyyy-MM-dd HH:mm"
        :title="$t('wms.deliveryManagement.packingCreatedTime')"
      ></vxe-date-column>
    </vxe-table>
    <custom-pager
      :current-page="data.tablePage.pageIndex"
      :page-size="data.tablePage.pageSize"
      perfect
      :total="data.tablePage.total"
      :page-sizes="PAGE_SIZE"
      :layouts="PAGE_LAYOUT"
      @page-change="method.handlePageChange"
    ></custom-pager>
  </div>

  <button ref="printButtonRef" v-print="'#pickingPrintArea'" class="print-trigger" type="button"></button>
  <div id="pickingPrintArea" class="print-area">
    <h2>{{ $t('wms.deliveryManagement.pickingList') }}</h2>
    <table>
      <thead>
        <tr>
          <th>{{ $t('wms.deliveryManagement.productImage') }}</th>
          <th class="product-info-cell">{{ $t('wms.deliveryManagement.productInfo') }}</th>
          <th>{{ $t('wms.deliveryManagement.shippingPersonnel') }}</th>
          <th>{{ $t('wms.deliveryManagement.quantityLabel') }}</th>
          <th>{{ $t('wms.deliveryManagement.variantLabel') }}</th>
          <th>{{ $t('wms.deliveryManagement.packingCreatedTime') }}</th>
        </tr>
      </thead>
      <tbody>
        <tr v-for="row in data.printRows" :key="row.id">
          <td><img v-if="row.main_image" :src="row.main_image" referrerpolicy="no-referrer" :alt="row.commodity_name || row.spu_name" /></td>
          <td class="product-info-cell">
            <div class="primary-text">{{ row.commodity_name || row.spu_name || '-' }}</div>
            <div class="secondary-text">{{ $t('wms.deliveryManagement.fnSku') }}：{{ row.fba_sku || '-' }}</div>
            <div class="secondary-text">{{ row.shop_name || '-' }}</div>
          </td>
          <td>
            <div class="primary-text">{{ row.dept_name || '-' }}</div>
            <div class="secondary-text">{{ row.order_user_name || '-' }}</div>
          </td>
          <td>{{ row.qty }}</td>
          <td>{{ row.variant_qty ?? 1 }} {{ $t('wms.deliveryManagement.variantLabel') }}</td>
          <td>{{ method.formatDateTime(row.prepared_time) }}</td>
        </tr>
      </tbody>
    </table>
  </div>
</template>

<script lang="ts" setup>
import { computed, nextTick, onMounted, reactive, ref, watch } from 'vue'
import type { VxePagerEvents } from 'vxe-table'
import { completePicking, getGoodsToBePicked } from '@/api/wms/deliveryManagement'
import { hookComponent } from '@/components/system'
import BtnGroup from '@/components/system/btnGroup.vue'
import ProductImage from '@/components/system/product-image.vue'
import { computedCardHeight, computedTableHeight } from '@/constant/style'
import { DEBOUNCE_TIME } from '@/constant/system'
import { DEFAULT_PAGE_SIZE, PAGE_LAYOUT, PAGE_SIZE } from '@/constant/vxeTable'
import i18n from '@/languages/i18n'
import type { btnGroupItem, TablePage } from '@/types/System/Form'
import type { DeliveryManagementDetailVO } from '@/types/DeliveryManagement/DeliveryManagement'
import { getMenuAuthorityList, setSearchObject } from '@/utils/common'
import customPager from '@/components/custom-pager.vue'

const xTable = ref()
const printButtonRef = ref<HTMLButtonElement>()

const data = reactive({
  searchForm: { spu_name: '' },
  timer: ref<ReturnType<typeof setTimeout> | null>(null),
  tableData: [] as DeliveryManagementDetailVO[],
  printRows: [] as DeliveryManagementDetailVO[],
  tablePage: {
    total: 0,
    pageIndex: 1,
    pageSize: DEFAULT_PAGE_SIZE,
    searchObjects: []
  } as TablePage,
  btnList: [] as btnGroupItem[],
  authorityList: getMenuAuthorityList()
})

const requireSelection = (): DeliveryManagementDetailVO[] => {
  const rows = (xTable.value?.getCheckboxRecords() ?? []) as DeliveryManagementDetailVO[]
  if (rows.length === 0) {
    hookComponent.$message({ type: 'error', content: i18n.global.t('base.userManagement.checkboxIsNull') })
  }
  return rows
}

const method = reactive({
  refresh: () => method.getGoodsToBePicked(),
  getGoodsToBePicked: async () => {
    const { data: res } = await getGoodsToBePicked(data.tablePage)
    if (!res.isSuccess) {
      hookComponent.$message({ type: 'error', content: res.errorMessage })
      return
    }
    data.tableData = res.data.rows
    data.tablePage.total = res.data.totals
  },
  handlePageChange: ref<VxePagerEvents.PageChange>(({ currentPage, pageSize }) => {
    data.tablePage.pageIndex = currentPage
    data.tablePage.pageSize = pageSize
    method.getGoodsToBePicked()
  }),
  sureSearch: () => {
    data.tablePage.searchObjects = setSearchObject(data.searchForm)
    method.getGoodsToBePicked()
  },
  printSelected: async () => {
    const rows = requireSelection()
    if (rows.length === 0) return
    data.printRows = [...rows]
    await nextTick()
    printButtonRef.value?.click()
  },
  completeSelected: () => {
    const rows = requireSelection()
    if (rows.length === 0) return
    hookComponent.$dialog({
      content: i18n.global.t('wms.deliveryManagement.completePickingConfirm'),
      handleConfirm: async () => {
        const { data: res } = await completePicking(rows.map((row) => row.id))
        if (!res.isSuccess) {
          hookComponent.$message({ type: 'error', content: res.errorMessage })
          return
        }
        hookComponent.$message({ type: 'success', content: res.data })
        await method.getGoodsToBePicked()
      }
    })
  },
  formatDateTime: (value?: string) => {
    if (!value) return ''
    const date = new Date(value)
    return Number.isNaN(date.getTime()) ? value : date.toLocaleString('zh-CN', { hour12: false })
  }
})

onMounted(() => {
  data.btnList = [
    { name: i18n.global.t('system.page.refresh'), icon: 'mdi-refresh', code: '', click: method.refresh },
    { name: i18n.global.t('system.page.print'), icon: 'mdi-printer', code: '', click: method.printSelected },
    {
      name: i18n.global.t('wms.deliveryManagement.completePicking'),
      icon: 'mdi-check-all',
      code: 'picked-confirm',
      click: method.completeSelected
    }
  ]
})

const cardHeight = computed(() => computedCardHeight({}))
const tableHeight = computed(() => computedTableHeight({}))

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

defineExpose({ getGoodsToBePicked: method.getGoodsToBePicked })
</script>

<style lang="less" scoped>
.operateArea { width: 100%; min-width: 760px; display: flex; align-items: center; border-radius: 10px; padding: 0 10px; }
.col { display: flex; align-items: center; }
.print-trigger { position: fixed; left: -10000px; width: 1px; height: 1px; opacity: 0; }
.print-area { position: fixed; left: -10000px; top: 0; width: 1000px; padding: 20px; background: white; color: #000; }
.print-area h2 { margin: 0 0 16px; text-align: center; }
.print-area table { width: 100%; border-collapse: collapse; }
.print-area th, .print-area td { padding: 8px; border: 1px solid #333; text-align: center; vertical-align: middle; }
.print-area .product-info-cell { text-align: left; }
.primary-text { font-weight: 500; }
.secondary-text { margin-top: 4px; font-size: 12px; opacity: 0.72; }
.print-area img { width: 112px; height: 112px; object-fit: contain; }

@media print {
  .print-area {
    position: static;
    left: auto;
    top: auto;
    width: 100%;
    padding: 0;
  }
}
</style>
