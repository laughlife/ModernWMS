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
      <vxe-column field="main_image" :title="$t('wms.deliveryManagement.productImage')" width="92">
        <template #default="{ row }">
          <ProductImage :src="row.main_image" :alt="row.commodity_name || row.spu_name" :width="56" :height="56" />
        </template>
      </vxe-column>
      <vxe-column field="commodity_name" :title="$t('wms.deliveryManagement.spu_name')" min-width="220">
        <template #default="{ row }">{{ row.commodity_name || row.spu_name }}</template>
      </vxe-column>
      <vxe-column field="fba_sku" :title="$t('wms.deliveryManagement.fnSku')" min-width="160"></vxe-column>
      <vxe-column field="dept_name" :title="$t('wms.deliveryManagement.deptName')" min-width="140"></vxe-column>
      <vxe-column field="order_user_name" :title="$t('wms.deliveryManagement.operatorName')" min-width="140"></vxe-column>
      <vxe-column field="qty" :title="$t('wms.deliveryManagement.quantityLabel')" width="100"></vxe-column>
      <vxe-date-column
        field="prepared_time"
        width="170"
        format="yyyy-MM-dd HH:mm"
        :title="$t('wms.deliveryManagement.packingCreatedTime')"
      ></vxe-date-column>
      <vxe-column field="operate" :title="$t('system.page.operate')" width="220" :resizable="false">
        <template #default="{ row }">
          <div class="row-actions">
            <v-btn
              size="small"
              color="warning"
              variant="tonal"
              :disabled="!data.authorityList.includes('picked-revoke')"
              @click="method.repickRow(row)"
            >{{ $t('wms.deliveryManagement.repick') }}</v-btn>
            <v-btn
              size="small"
              color="primary"
              variant="tonal"
              :disabled="!data.authorityList.includes('weighed-weigh')"
              @click="method.startWeighingRow(row)"
            >{{ $t('wms.deliveryManagement.goToWeighing') }}</v-btn>
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
    ></custom-pager>
  </div>
</template>

<script lang="ts" setup>
import { computed, onMounted, reactive, ref, watch } from 'vue'
import type { VxePagerEvents } from 'vxe-table'
import { getPicked, repick, startWeighing } from '@/api/wms/deliveryManagement'
import { hookComponent } from '@/components/system'
import BtnGroup from '@/components/system/btnGroup.vue'
import ProductImage from '@/components/system/product-image.vue'
import customPager from '@/components/custom-pager.vue'
import { computedCardHeight, computedTableHeight } from '@/constant/style'
import { DEBOUNCE_TIME } from '@/constant/system'
import { DEFAULT_PAGE_SIZE, PAGE_LAYOUT, PAGE_SIZE } from '@/constant/vxeTable'
import i18n from '@/languages/i18n'
import type { DeliveryManagementDetailVO } from '@/types/DeliveryManagement/DeliveryManagement'
import type { btnGroupItem, TablePage } from '@/types/System/Form'
import { getMenuAuthorityList, setSearchObject } from '@/utils/common'

const emit = defineEmits<{ goToWeighing: []; goToPicking: [] }>()
const xTable = ref()

const data = reactive({
  searchForm: { spu_name: '' },
  timer: ref<ReturnType<typeof setTimeout> | null>(null),
  tableData: [] as DeliveryManagementDetailVO[],
  tablePage: {
    total: 0,
    pageIndex: 1,
    pageSize: DEFAULT_PAGE_SIZE,
    searchObjects: []
  } as TablePage,
  btnList: [] as btnGroupItem[],
  authorityList: getMenuAuthorityList()
})

const method = reactive({
  refresh: () => method.getPicked(),
  getPicked: async () => {
    const { data: res } = await getPicked(data.tablePage)
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
    method.getPicked()
  }),
  sureSearch: () => {
    data.tablePage.searchObjects = setSearchObject(data.searchForm)
    method.getPicked()
  },
  repickRow: (row: DeliveryManagementDetailVO) => {
    hookComponent.$dialog({
      content: i18n.global.t('wms.deliveryManagement.repickConfirm'),
      handleConfirm: async () => {
        const { data: res } = await repick(row.id)
        if (!res.isSuccess) {
          hookComponent.$message({ type: 'error', content: res.errorMessage })
          return
        }
        hookComponent.$message({ type: 'success', content: res.data })
        emit('goToPicking')
      }
    })
  },
  startWeighingRow: (row: DeliveryManagementDetailVO) => {
    hookComponent.$dialog({
      content: i18n.global.t('wms.deliveryManagement.goToWeighingConfirm'),
      handleConfirm: async () => {
        const { data: res } = await startWeighing(row.id)
        if (!res.isSuccess) {
          hookComponent.$message({ type: 'error', content: res.errorMessage })
          return
        }
        hookComponent.$message({ type: 'success', content: res.data })
        emit('goToWeighing')
      }
    })
  }
})

onMounted(() => {
  data.btnList = [{ name: i18n.global.t('system.page.refresh'), icon: 'mdi-refresh', code: '', click: method.refresh }]
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

defineExpose({ getPicked: method.getPicked })
</script>

<style lang="less" scoped>
.operateArea { width: 100%; min-width: 760px; display: flex; align-items: center; border-radius: 10px; padding: 0 10px; }
.col { display: flex; align-items: center; }
.row-actions { display: flex; justify-content: center; gap: 8px; }
</style>
