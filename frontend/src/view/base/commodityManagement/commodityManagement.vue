<template>
  <div class="container">
    <v-card class="mt-5">
      <v-card-text>
        <div class="catalogToolbar">
          <v-row no-gutters align="center">
            <v-col cols="12" md="9">
              <v-row no-gutters @keyup.enter="method.sureSearch">
                <v-col cols="12" sm="6" lg="4">
                  <v-text-field
                    v-model="data.searchForm.sku_name"
                    clearable
                    hide-details
                    density="comfortable"
                    class="searchInput"
                    :label="$t('base.commodityManagement.catalog_name')"
                    variant="solo"
                  ></v-text-field>
                </v-col>
                <v-col cols="12" sm="6" lg="4">
                  <v-text-field
                    v-model="data.searchForm.sku_code"
                    clearable
                    hide-details
                    density="comfortable"
                    class="searchInput"
                    :label="$t('base.commodityManagement.sku')"
                    variant="solo"
                  ></v-text-field>
                </v-col>
              </v-row>
            </v-col>
            <v-col cols="12" md="3" class="refreshArea">
              <v-btn prepend-icon="mdi-refresh" variant="tonal" color="primary" @click="method.refresh">
                {{ $t('system.page.refresh') }}
              </v-btn>
            </v-col>
          </v-row>
        </div>

        <div class="mt-5" :style="{ height: cardHeight }">
          <vxe-table
            :data="data.tableData"
            :height="tableHeight"
            :column-config="{ minWidth: '140px' }"
            align="center"
          >
            <template #empty>
              {{ $t('system.page.noData') }}
            </template>

            <vxe-column field="product_image" width="112" :title="$t('base.commodityManagement.product_image')">
              <template #default="{ row }">
                <div class="imageCell">
                  <product-image
                    :src="row.product_image"
                    :alt="row.sku_name"
                    :width="64"
                    :height="64"
                  ></product-image>
                </div>
              </template>
            </vxe-column>

            <vxe-column field="sku_name" min-width="280" :title="$t('base.commodityManagement.product_info')">
              <template #default="{ row }">
                <div class="productInfo">
                  <span class="productName">{{ row.sku_name || '-' }}</span>
                  <span class="productSku">SKU：{{ row.sku_code || '-' }}</span>
                </div>
              </template>
            </vxe-column>

            <vxe-column field="volume_cm3" width="180" :title="$t('base.commodityManagement.volume_cm3')">
              <template #default="{ row }">
                <span class="numericValue">{{ formatVolume(row.volume_cm3) }}</span>
              </template>
            </vxe-column>

            <vxe-column field="cost" width="160" :title="$t('base.commodityManagement.cost')">
              <template #default="{ row }">
                <span class="costValue">{{ formatCost(row.cost) }}</span>
              </template>
            </vxe-column>

            <vxe-column field="ownerships" min-width="300" :title="$t('base.commodityManagement.ownership')">
              <template #default="{ row }">
                <div v-if="row.ownerships?.length" class="ownershipList">
                  <div
                    v-for="owner in row.ownerships"
                    :key="`${owner.dept_name}-${owner.order_user_name}`"
                    class="ownershipItem"
                  >
                    <span class="ownershipLabel">{{ $t('base.commodityManagement.dept_name') }}</span>
                    <span>{{ owner.dept_name || '-' }}</span>
                    <span class="ownershipDivider"></span>
                    <span class="ownershipLabel">{{ $t('base.commodityManagement.order_user_name') }}</span>
                    <span>{{ owner.order_user_name || '-' }}</span>
                  </div>
                </div>
                <span v-else class="emptyValue">-</span>
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
      </v-card-text>
    </v-card>
  </div>
</template>

<script lang="ts" setup>
import { computed, onBeforeUnmount, onMounted, reactive, ref, watch } from 'vue'
import type { VxePagerEvents } from 'vxe-table'
import { computedCardHeight, computedTableHeight } from '@/constant/style'
import { PAGE_SIZE, PAGE_LAYOUT, DEFAULT_PAGE_SIZE } from '@/constant/vxeTable'
import { DEBOUNCE_TIME } from '@/constant/system'
import { getCommodityCatalog } from '@/api/base/commodityManagementSetting'
import type { CommodityCatalogVO } from '@/types/Base/CommodityManagement'
import { hookComponent } from '@/components/system'
import ProductImage from '@/components/system/product-image.vue'
import CustomPager from '@/components/custom-pager.vue'
import { setSearchObject } from '@/utils/common'

const data = reactive({
  searchForm: {
    sku_name: '',
    sku_code: ''
  },
  tableData: [] as CommodityCatalogVO[],
  tablePage: {
    total: 0,
    pageIndex: 1,
    pageSize: DEFAULT_PAGE_SIZE,
    searchObjects: [] as unknown[]
  },
  timer: null as ReturnType<typeof setTimeout> | null
})

const formatVolume = (value?: number) => `${Number(value || 0).toLocaleString('zh-CN', { maximumFractionDigits: 3 })} cm³`
const formatCost = (value?: number) => `¥${Number(value || 0).toFixed(2)}`

const method = reactive({
  sureSearch: () => {
    data.tablePage.pageIndex = 1
    data.tablePage.searchObjects = setSearchObject(data.searchForm)
    method.getCatalog()
  },
  getCatalog: async () => {
    const { data: res } = await getCommodityCatalog(data.tablePage)
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
  refresh: () => method.getCatalog(),
  handlePageChange: ref<VxePagerEvents.PageChange>(({ currentPage, pageSize }) => {
    data.tablePage.pageIndex = currentPage
    data.tablePage.pageSize = pageSize
    method.getCatalog()
  })
})

const cardHeight = computed(() => computedCardHeight({ hasTab: false }))
const tableHeight = computed(() => computedTableHeight({ hasTab: false }))

watch(
  () => [data.searchForm.sku_name, data.searchForm.sku_code],
  () => {
    if (data.timer) {
      clearTimeout(data.timer)
    }
    data.timer = setTimeout(() => {
      data.timer = null
      method.sureSearch()
    }, DEBOUNCE_TIME)
  }
)

onMounted(method.getCatalog)
onBeforeUnmount(() => {
  if (data.timer) {
    clearTimeout(data.timer)
  }
})
</script>

<style scoped lang="less">
.catalogToolbar {
  width: 100%;
}

.searchInput {
  margin: 4px 12px 4px 0;
}

.refreshArea {
  display: flex;
  justify-content: flex-end;
  padding: 4px 0;
}

.imageCell {
  align-items: center;
  display: flex;
  justify-content: center;
  min-height: 72px;
}

.productInfo {
  align-items: flex-start;
  display: flex;
  flex-direction: column;
  gap: 6px;
  text-align: left;
}

.productName {
  color: rgba(var(--v-theme-on-surface), 0.92);
  font-size: 14px;
  font-weight: 600;
}

.productSku,
.emptyValue {
  color: rgba(var(--v-theme-on-surface), 0.6);
}

.numericValue,
.costValue {
  font-variant-numeric: tabular-nums;
}

.costValue {
  color: rgb(var(--v-theme-primary));
  font-weight: 600;
}

.ownershipList {
  display: flex;
  flex-direction: column;
  gap: 6px;
  padding: 6px 0;
}

.ownershipItem {
  align-items: center;
  display: flex;
  flex-wrap: wrap;
  gap: 6px;
  text-align: left;
}

.ownershipLabel {
  color: rgba(var(--v-theme-on-surface), 0.55);
  font-size: 12px;
}

.ownershipDivider {
  background: rgba(var(--v-border-color), var(--v-border-opacity));
  height: 14px;
  margin: 0 4px;
  width: 1px;
}

@media (max-width: 959px) {
  .refreshArea {
    justify-content: flex-start;
  }
}
</style>
