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
            </v-row>
          </div>

          <div
            class="mt-5"
            :style="{
              height: cardHeight
            }"
          >
            <vxe-table ref="xTable" :data="data.tableData" :height="tableHeight" align="center">
              <template #empty>
                {{ i18n.global.t('system.page.noData') }}
              </template>
              <vxe-column field="sequence" width="100" :title="$t('base.commodityCategorySetting.sequence')"></vxe-column>
              <vxe-column field="group_name" :title="$t('base.commodityCategorySetting.group_name')"></vxe-column>
              <vxe-column field="leader_name" :title="$t('base.commodityCategorySetting.leader_name')"></vxe-column>
              <vxe-column field="phone" :title="$t('base.commodityCategorySetting.phone')"></vxe-column>
            </vxe-table>
          </div>
        </v-card-text>
      </v-card>
    </div>
  </div>
</template>

<script lang="ts" setup>
import { computed, reactive, onMounted, ref } from 'vue'
import { computedCardHeight, computedTableHeight } from '@/constant/style'
import { DataProps } from '@/types/Base/CommodityCategorySetting'
import { getOperatorGroupAll } from '@/api/base/commodityCategorySetting'
import { hookComponent } from '@/components/system'
import i18n from '@/languages/i18n'
import { exportData } from '@/utils/exportTable'
import BtnGroup from '@/components/system/btnGroup.vue'
import { getMenuAuthorityList } from '@/utils/common'

const xTable = ref()

const data: DataProps = reactive({
  tableData: [],
  btnList: [],
  authorityList: getMenuAuthorityList()
})

const method = reactive({
  getOperatorGroupList: async () => {
    const { data: res } = await getOperatorGroupAll()
    if (!res.isSuccess) {
      hookComponent.$message({
        type: 'error',
        content: res.errorMessage
      })
      return
    }
    data.tableData = res.data
  },
  refresh: () => {
    method.getOperatorGroupList()
  },
  exportTable: () => {
    const $table = xTable.value
    exportData({
      table: $table,
      filename: i18n.global.t('router.sideBar.commodityCategorySetting'),
      columnFilterMethod({ column }: any) {
        return !['checkbox'].includes(column?.type)
      }
    })
  }
})

onMounted(async () => {
  await method.getOperatorGroupList()

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
})

const cardHeight = computed(() => computedCardHeight({ hasTab: false }))

const tableHeight = computed(() => computedTableHeight({ hasTab: false, hasPager: false }))
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
