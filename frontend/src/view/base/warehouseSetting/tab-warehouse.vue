<template>
  <div class="operateArea">
    <v-row no-gutters>
      <!-- Operate Btn -->
      <v-col cols="3" class="col">

        <!-- new version -->
        <BtnGroup :authority-list="data.authorityList" :btn-list="data.btnList" />
      </v-col>

      <!-- Search Input -->
      <v-col cols="9">
        <v-row no-gutters @keyup.enter="method.sureSearch">
          <v-col cols="4">
            <v-text-field
              v-model="data.searchForm.warehouse_name"
              clearable
              hide-details
              density="comfortable"
              class="searchInput ml-5 mt-1"
              :label="'仓库名称'"
              variant="solo"
            >
            </v-text-field>
          </v-col>
          <v-col cols="4">
            <v-text-field
              v-model="data.searchForm.city"
              clearable
              hide-details
              density="comfortable"
              class="searchInput ml-5 mt-1"
              :label="'城市'"
              variant="solo"
            >
            </v-text-field>
          </v-col>
          <v-col cols="4">
            <v-text-field
              v-model="data.searchForm.manager"
              clearable
              hide-details
              density="comfortable"
              class="searchInput ml-5 mt-1"
              :label="'负责人'"
              variant="solo"
            >
            </v-text-field>
          </v-col>
        </v-row>
      </v-col>
    </v-row>
  </div>

  <p class="ml-4 mt-3">仓库名称、地址和联系人由 ERP 统一维护。</p>
  <!-- Table -->
  <div
    class="mt-5"
    :style="{
      height: cardHeight
    }"
  >
    <vxe-table ref="xTableWarehouse" :column-config="{ minWidth: '100px' }" :data="data.tableData" :height="tableHeight" align="center">
      <vxe-column type="seq" width="60"></vxe-column>
      <vxe-column type="checkbox" width="50"></vxe-column>
      <vxe-column field="warehouse_name" :title="'仓库名称'"></vxe-column>
      <vxe-column field="city" :title="'城市'"></vxe-column>
      <vxe-column field="address" :title="'收货地址'"></vxe-column>
      <vxe-column field="contact_tel" :title="'联系电话'"></vxe-column>
      <vxe-column field="email" :title="'邮箱'"></vxe-column>
      <vxe-column field="manager" :title="'负责人'"></vxe-column>
      <vxe-column field="creator" :title="'创建人'"></vxe-column>
      <vxe-date-column
        field="create_time"
        width="170px"
        format="yyyy-MM-dd HH:mm"
        :title="'创建时间'"
      ></vxe-date-column>
      <vxe-column field="is_valid" :title="'有效状态'">
        <template #default="{ row, column }">
          <span>{{ formatIsValid(row[column.property]) }}</span>
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
  </div>
</template>

<script lang="ts" setup>
import { computed, ref, reactive, watch, onMounted } from 'vue'
import { VxePagerEvents } from 'vxe-table'
import { computedCardHeight, computedTableHeight } from '@/constant/style'
import { WarehouseVO } from '@/types/Base/Warehouse'
import { PAGE_SIZE, PAGE_LAYOUT, DEFAULT_PAGE_SIZE } from '@/constant/vxeTable'
import { hookComponent } from '@/components/system'
import { getWarehouseList } from '@/api/base/warehouseSetting'
import { formatIsValid } from '@/utils/format/formatSystem'
import customPager from '@/components/custom-pager.vue'
import { setSearchObject, getMenuAuthorityList } from '@/utils/common'
import { DEBOUNCE_TIME } from '@/constant/system'
import { SearchObject, btnGroupItem } from '@/types/System/Form'
import { exportData } from '@/utils/exportTable'
import BtnGroup from '@/components/system/btnGroup.vue'

const xTableWarehouse = ref()

const data = reactive({
  searchForm: {
    warehouse_name: '',
    city: '',
    manager: ''
  },
  activeTab: null,
  tableData: ref<WarehouseVO[]>([]),
  tablePage: reactive({
    total: 0,
    pageIndex: 1,
    pageSize: DEFAULT_PAGE_SIZE,
    searchObjects: ref<Array<SearchObject>>([])
  }),
  timer: ref<any>(null),
  btnList: [] as btnGroupItem[],
  // Menu operation permissions
  authorityList: getMenuAuthorityList() as string[]
})

const method = reactive({
  // Refresh data
  refresh: () => {
    method.getWarehouseList()
  },
  getWarehouseList: async () => {
    const { data: res } = await getWarehouseList(data.tablePage)
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

    method.getWarehouseList()
  }),
  exportTable: () => {
    const $table = xTableWarehouse.value
    exportData({
      table: $table,
      filename: '仓库设置',
      columnFilterMethod({ column }: any) {
        return !['checkbox'].includes(column?.type) && !['operate'].includes(column?.field)
      }
    })
  },
  sureSearch: () => {
    data.tablePage.searchObjects = setSearchObject(data.searchForm)
    method.getWarehouseList()
  }
})

onMounted(() => {
  data.btnList = [
    {
      name: '刷新',
      icon: 'mdi-refresh',
      code: '',
      click: method.refresh
    },
    {
      name: '导出',
      icon: 'mdi-export-variant',
      code: 'warehouse-export',
      click: method.exportTable
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
  getWarehouseList: method.getWarehouseList
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

</style>
