<!-- supplier Setting -->
<template>
  <div class="container">
    <div>
      <v-card class="mt-5">
        <v-card-text>
          <div class="operateArea">
            <v-row no-gutters>
              <v-col cols="12" sm="4" class="col">
                <BtnGroup :authority-list="data.authorityList" :btn-list="data.btnList" />
              </v-col>

              <v-col cols="12" sm="8">
                <v-row no-gutters @keyup.enter="method.sureSearch">
                  <v-col cols="12" sm="4"></v-col>
                  <v-col cols="12" sm="4"></v-col>
                  <v-col cols="12" sm="4">
                    <v-text-field
                      v-model="data.searchForm.name"
                      clearable
                      hide-details
                      density="comfortable"
                      class="searchInput ml-5 mt-1"
                      :label="$t('base.supplier.name')"
                      variant="solo"
                    >
                    </v-text-field>
                  </v-col>
                </v-row>
              </v-col>
            </v-row>
          </div>

          <div
            class="mt-5"
            :style="{
              height: cardHeight
            }"
          >
            <vxe-table
              ref="xTable"
              :column-config="{
                minWidth: '100px'
              }"
              :data="data.tableData"
              :height="tableHeight"
              align="center"
            >
              <template #empty>
                {{ i18n.global.t('system.page.noData') }}
              </template>
              <vxe-column type="seq" width="60"></vxe-column>
              <vxe-column field="id" :title="$t('base.supplier.id')" width="100"></vxe-column>
              <vxe-column field="name" :title="$t('base.supplier.name')" min-width="180"></vxe-column>
              <vxe-column field="linkman" :title="$t('base.supplier.linkman')" width="120"></vxe-column>
              <vxe-column field="telephone_num" :title="$t('base.supplier.telephone_num')" width="150"></vxe-column>
              <vxe-column field="qq" :title="$t('base.supplier.qq')" width="120"></vxe-column>
              <vxe-column field="email" :title="$t('base.supplier.email')" min-width="180"></vxe-column>
              <vxe-column field="province_name" :title="$t('base.supplier.province_name')" width="120"></vxe-column>
              <vxe-column field="city_name" :title="$t('base.supplier.city_name')" width="120"></vxe-column>
              <vxe-column field="address_line" :title="$t('base.supplier.address_line')" min-width="220"></vxe-column>
              <vxe-column field="remark" :title="$t('base.supplier.remark')" min-width="180"></vxe-column>
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
        </v-card-text>
      </v-card>
    </div>
  </div>
</template>

<script lang="tsx" setup>
import { computed, ref, reactive, onMounted, watch } from 'vue'
import { VxePagerEvents } from 'vxe-table'
import { computedCardHeight, computedTableHeight } from '@/constant/style'
import { PAGE_SIZE, PAGE_LAYOUT, DEFAULT_PAGE_SIZE } from '@/constant/vxeTable'
import { hookComponent } from '@/components/system'
import { DEBOUNCE_TIME } from '@/constant/system'
import { setSearchObject, getMenuAuthorityList } from '@/utils/common'
import { SearchObject, btnGroupItem } from '@/types/System/Form'
import i18n from '@/languages/i18n'
import { getSupplierList } from '@/api/base/supplier'
import customPager from '@/components/custom-pager.vue'
import { exportData } from '@/utils/exportTable'
import BtnGroup from '@/components/system/btnGroup.vue'

const xTable = ref()

const data = reactive({
  searchForm: {
    name: ''
  },
  tableData: [],
  tablePage: {
    total: 0,
    pageIndex: 1,
    pageSize: DEFAULT_PAGE_SIZE,
    searchObjects: ref<Array<SearchObject>>([])
  },
  timer: ref<any>(null),
  btnList: [] as btnGroupItem[],
  authorityList: getMenuAuthorityList() as string[]
})

const method = reactive({
  sureSearch: () => {
    data.tablePage.searchObjects = setSearchObject(data.searchForm)
    method.getData()
  },
  refresh: () => {
    method.getData()
  },
  handlePageChange: ref<VxePagerEvents.PageChange>(({ currentPage, pageSize }) => {
    data.tablePage.pageIndex = currentPage
    data.tablePage.pageSize = pageSize
    method.getData()
  }),
  exportTable: () => {
    const $table = xTable.value
    exportData({
      table: $table,
      filename: i18n.global.t('router.sideBar.supplier')
    })
  },
  getData: async () => {
    const { data: res } = await getSupplierList(data.tablePage)
    if (!res.isSuccess) {
      hookComponent.$message({
        type: 'error',
        content: res.errorMessage
      })
      return
    }
    data.tableData = res.data.rows
    data.tablePage.total = res.data.totals
  }
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
      code: 'export',
      click: method.exportTable
    }
  ]

  method.getData()
})

const cardHeight = computed(() => computedCardHeight({ hasTab: false }))
const tableHeight = computed(() => computedTableHeight({ hasTab: false }))

watch(
  () => data.searchForm,
  () => {
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
</script>

<style scoped lang="less">
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
