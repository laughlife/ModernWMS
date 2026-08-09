<template>
  <v-dialog v-model="data.showDialog" width="960" transition="dialog-top-transition" persistent>
    <v-card class="formCard receiptDialog">
      <v-toolbar color="white" :title="$t('wms.erpPendingReceipt.receipt_title')">
        <template #append>
          <v-btn icon="mdi-close" variant="text" @click="method.closeDialog"></v-btn>
        </template>
      </v-toolbar>

      <v-divider></v-divider>

      <v-card-text class="receiptContent">
        <v-form ref="formRef">
          <div class="receiptSummaryGrid">
            <div class="receiptInfoRow">
              <span class="receiptLabel">{{ $t('wms.erpPendingReceipt.purchase_no') }}</span>
              <span class="receiptValue">{{ data.currentRow?.purchase_no || '-' }}</span>
            </div>
            <div class="receiptInfoRow">
              <span class="receiptLabel">{{ $t('wms.erpPendingReceipt.warehouse_name') }}</span>
              <span class="receiptValue">{{ data.currentRow?.warehouse_name || '-' }}</span>
            </div>
            <div class="receiptInfoRow">
              <span class="receiptLabel">{{ $t('wms.erpPendingReceipt.order_user_name') }}</span>
              <span class="receiptValue">{{ orderUserNames }}</span>
            </div>
            <div class="receiptInfoRow">
              <span class="receiptLabel">{{ $t('wms.erpPendingReceipt.dept_name') }}</span>
              <span class="receiptValue">{{ deptNames }}</span>
            </div>
            <div class="receiptInfoRow">
              <span class="receiptLabel">{{ $t('wms.erpPendingReceipt.shipment_qty') }}</span>
              <span class="receiptValue">{{ data.currentRow?.shipment_qty ?? 0 }}</span>
            </div>
            <div class="receiptInfoRow">
              <span class="receiptLabel">{{ $t('wms.erpPendingReceipt.source_freight_payment_type') }}</span>
              <span class="receiptValue">{{ method.sourceFreightPaymentType() }}</span>
            </div>
          </div>

          <div class="receiptItemsBlock">
            <div class="receiptItemsTitle">{{ $t('wms.erpPendingReceipt.item_receipt_title') }}</div>
            <v-table density="compact" class="receiptItemsTable">
              <thead>
                <tr>
                  <th>{{ $t('wms.erpPendingReceipt.product') }}</th>
                  <th>{{ $t('wms.erpPendingReceipt.item_shipment_qty') }}</th>
                  <th>{{ $t('wms.erpPendingReceipt.actual_receipt_qty') }}</th>
                  <th>{{ $t('wms.erpPendingReceipt.loss_qty') }}</th>
                  <th>{{ $t('wms.erpPendingReceipt.inbound_qty') }}</th>
                </tr>
              </thead>
              <tbody>
                <tr v-for="item in data.form.items" :key="item.sourceItemKey">
                  <td>
                    <div class="receiptProduct">
                      <v-img
                        v-if="item.mainImage"
                        :src="item.mainImage"
                        :alt="item.productName || item.commoditySku"
                        width="64"
                        height="64"
                        cover
                        class="receiptProductImage"
                      >
                        <template #error>
                          <div class="receiptProductImageFallback">
                            <v-icon icon="mdi-image-off-outline" size="24"></v-icon>
                          </div>
                        </template>
                      </v-img>
                      <div v-else class="receiptProductImageFallback">
                        <v-icon icon="mdi-image-off-outline" size="24"></v-icon>
                      </div>
                      <div class="receiptProductText">
                        <div class="receiptProductName">{{ item.productName || '-' }}</div>
                        <div class="receiptProductSku">{{ item.commoditySku || '-' }}</div>
                      </div>
                    </div>
                  </td>
                  <td class="receiptNumberCell">{{ item.shipmentQty }}</td>
                  <td>
                    <v-text-field
                      v-model.number="item.actualReceiptQty"
                      type="number" min="0" :max="item.shipmentQty" step="1"
                      variant="outlined" density="compact" hide-details="auto"
                      :rules="method.actualQtyRules(item)"
                    ></v-text-field>
                  </td>
                  <td>
                    <v-text-field
                      v-model.number="item.lossQty"
                      type="number" min="0" :max="item.actualReceiptQty" step="1"
                      variant="outlined" density="compact" hide-details="auto"
                      :rules="method.lossQtyRules(item)"
                    ></v-text-field>
                  </td>
                  <td class="receiptNumberCell receiptInboundCell">{{ method.itemInboundQty(item) }}</td>
                </tr>
              </tbody>
              <tfoot>
                <tr>
                  <th>{{ $t('wms.erpPendingReceipt.total') }}</th>
                  <th class="receiptNumberCell">{{ shipmentQtyTotal }}</th>
                  <th class="receiptNumberCell">{{ actualReceiptQtyTotal }}</th>
                  <th class="receiptNumberCell">{{ lossQtyTotal }}</th>
                  <th class="receiptNumberCell receiptInboundCell">{{ inboundQty }}</th>
                </tr>
              </tfoot>
            </v-table>
            <div class="receiptQtyTip">{{ $t('wms.erpPendingReceipt.receipt_qty_tip') }}</div>
          </div>

          <div class="receiptFormGrid">

            <div class="receiptFormRow">
              <span class="receiptLabel requiredLabel">{{ $t('wms.erpPendingReceipt.receipt_freight_payment_status') }}</span>
              <v-btn-toggle v-model="data.form.receiptFreightPaymentStatus" color="primary" mandatory divided>
                <v-btn value="NO_PAY">{{ $t('wms.erpPendingReceipt.no_pay') }}</v-btn>
                <v-btn value="PAY">{{ $t('wms.erpPendingReceipt.pay') }}</v-btn>
              </v-btn-toggle>
            </div>
          </div>

          <template v-if="shouldPayFreight">
            <div class="receiptFormRow receiptControlRow">
              <span class="receiptLabel requiredLabel">{{ $t('wms.erpPendingReceipt.receipt_freight_amount') }}</span>
              <v-text-field
                v-model.number="data.form.receiptFreightAmount"
                type="number"
                min="0.01"
                step="0.01"
                variant="outlined"
                density="comfortable"
                hide-details="auto"
                :aria-label="$t('wms.erpPendingReceipt.receipt_freight_amount')"
                :rules="data.rules.receiptFreightAmount"
              ></v-text-field>
            </div>
            <div class="receiptFormRow receiptControlRow">
              <span class="receiptLabel">{{ $t('wms.erpPendingReceipt.receipt_freight_attachments') }}</span>
              <erp-receipt-image-upload
                v-model="data.form.receiptFreightFiles"
                :shipment-id="data.currentRow?.id ?? 0"
                category="freight"
                icon="mdi-receipt-text-outline"
                :label="$t('wms.erpPendingReceipt.receipt_freight_attachments')"
                hide-label
              ></erp-receipt-image-upload>
            </div>
          </template>

          <template v-if="showLossFields">
            <div class="receiptFormRow receiptControlRow">
              <span class="receiptLabel requiredLabel">{{ $t('wms.erpPendingReceipt.loss_reason') }}</span>
              <v-textarea
                v-model="data.form.lossReason"
                variant="outlined"
                rows="3"
                maxlength="500"
                counter
                hide-details="auto"
                :aria-label="$t('wms.erpPendingReceipt.loss_reason')"
                :rules="data.rules.lossReason"
              ></v-textarea>
            </div>
            <div class="receiptFormRow receiptControlRow">
              <span class="receiptLabel">{{ $t('wms.erpPendingReceipt.loss_attachments') }}</span>
              <erp-receipt-image-upload
                v-model="data.form.lossFiles"
                :shipment-id="data.currentRow?.id ?? 0"
                category="loss"
                icon="mdi-image-multiple-outline"
                :label="$t('wms.erpPendingReceipt.loss_attachments')"
                hide-label
              ></erp-receipt-image-upload>
            </div>
          </template>

          <div class="receiptFormRow receiptControlRow">
            <span class="receiptLabel">{{ $t('wms.erpPendingReceipt.receipt_attachments') }}</span>
            <erp-receipt-image-upload
              v-model="data.form.receiptFiles"
              :shipment-id="data.currentRow?.id ?? 0"
              category="receipt"
              icon="mdi-image-multiple-outline"
              :label="$t('wms.erpPendingReceipt.receipt_attachments')"
              hide-label
            ></erp-receipt-image-upload>
          </div>

          <v-textarea
            v-model="data.form.receiptRemark"
            variant="outlined"
            rows="3"
            maxlength="500"
            counter
            :label="$t('wms.erpPendingReceipt.receipt_remark')"
            :placeholder="$t('wms.erpPendingReceipt.receipt_remark_placeholder')"
          ></v-textarea>

          <v-alert type="info" variant="tonal" density="compact">
            {{ $t('wms.erpPendingReceipt.receipt_inbound_tip') }}
          </v-alert>
        </v-form>
      </v-card-text>

      <v-divider></v-divider>

      <v-card-actions class="justify-end px-6 py-4">
        <v-btn variant="text" @click="method.closeDialog">{{ $t('system.page.close') }}</v-btn>
        <v-btn color="primary" variant="flat" :loading="data.submitting" @click="method.submit">
          {{ $t('wms.erpPendingReceipt.receipt_confirm_inbound') }}
        </v-btn>
      </v-card-actions>
    </v-card>
  </v-dialog>
</template>

<script lang="ts" setup>
import { computed, nextTick, reactive, ref } from 'vue'
import { confirmErpReceipt, type ErpReceiptOssImage } from '@/api/wms/stockAsn'
import { hookComponent } from '@/components/system'
import i18n from '@/languages/i18n'
import type { ErpPendingReceiptVO } from '@/types/WMS/StockAsn'
import ErpReceiptImageUpload from './erp-receipt-image-upload.vue'

type ReceiptFreightPaymentStatus = 'NO_PAY' | 'PAY'
type ReceiptItemForm = {
  sourceItemKey: string
  commodityId?: number | null
  commoditySku: string
  productName: string
  mainImage: string
  shipmentQty: number
  actualReceiptQty: number
  lossQty: number
}

const formRef = ref()
const emit = defineEmits<{
  (event: 'saved'): void
}>()

const data = reactive({
  showDialog: false,
  submitting: false,
  currentRow: null as ErpPendingReceiptVO | null,
  form: {
    items: [] as ReceiptItemForm[],
    receiptFreightPaymentStatus: 'NO_PAY' as ReceiptFreightPaymentStatus,
    receiptFreightAmount: null as number | null,
    receiptFreightFiles: [] as ErpReceiptOssImage[],
    receiptFiles: [] as ErpReceiptOssImage[],
    lossReason: '',
    lossFiles: [] as ErpReceiptOssImage[],
    receiptRemark: ''
  },
  rules: {
    receiptFreightAmount: [
      (value: number | null) => !shouldPayFreight.value || Number(value) > 0 || i18n.global.t('wms.erpPendingReceipt.freight_amount_required')
    ],
    lossReason: [
      (value: string) => !showLossFields.value || !!value.trim() || i18n.global.t('wms.erpPendingReceipt.loss_reason_required')
    ]
  }
})

const shouldPayFreight = computed(() => data.form.receiptFreightPaymentStatus === 'PAY')
const shipmentQtyTotal = computed(() => data.form.items.reduce((sum, item) => sum + Number(item.shipmentQty || 0), 0))
const actualReceiptQtyTotal = computed(() => data.form.items.reduce((sum, item) => sum + Number(item.actualReceiptQty || 0), 0))
const lossQtyTotal = computed(() => data.form.items.reduce((sum, item) => sum + Number(item.lossQty || 0), 0))
const inboundQty = computed(() => actualReceiptQtyTotal.value - lossQtyTotal.value)
const showLossFields = computed(() => lossQtyTotal.value > 0)
const uniqueText = (values: string[]) => [...new Set(values.map((value) => value.trim()).filter(Boolean))].join('、')
const orderUserNames = computed(() => {
  const productUsers = uniqueText((data.currentRow?.product_list ?? []).map((product) => product.order_user_name))
  return productUsers || data.currentRow?.order_user_text || '-'
})
const deptNames = computed(() => {
  return uniqueText((data.currentRow?.product_list ?? []).map((product) => product.dept_name)) || '-'
})

const method = reactive({
  openDialog: (row: ErpPendingReceiptVO) => {
    data.currentRow = row
    data.form.items = row.product_list.map((product) => ({
      sourceItemKey: product.source_item_key,
      commodityId: product.commodity_id,
      commoditySku: product.sku,
      productName: product.product_name,
      mainImage: product.main_image,
      shipmentQty: Number(product.quantity ?? 0),
      actualReceiptQty: Number(product.quantity ?? 0),
      lossQty: 0
    }))
    data.form.receiptFreightPaymentStatus = row.source_freight_payment_type === 'COD' ? 'PAY' : 'NO_PAY'
    data.form.receiptFreightAmount = null
    data.form.receiptFreightFiles = []
    data.form.receiptFiles = []
    data.form.lossReason = ''
    data.form.lossFiles = []
    data.form.receiptRemark = ''
    data.submitting = false
    data.showDialog = true
    nextTick(() => formRef.value?.resetValidation?.())
  },
  closeDialog: () => {
    data.showDialog = false
  },
  submit: async () => {
    const validation = await formRef.value?.validate?.()
    if (!validation?.valid || !data.currentRow || data.submitting) return

    data.submitting = true
    try {
      const response = await confirmErpReceipt({
        shipment_id: data.currentRow.id,
        source_version: data.currentRow.source_version,
        items: data.form.items.map((item) => ({
          source_item_key: item.sourceItemKey,
          commodity_id: item.commodityId,
          commodity_sku: item.commoditySku,
          shipment_qty: Number(item.shipmentQty),
          actual_receipt_qty: Number(item.actualReceiptQty),
          loss_qty: Number(item.lossQty)
        })),
        receipt_freight_payment_status: data.form.receiptFreightPaymentStatus,
        receipt_freight_amount: shouldPayFreight.value ? data.form.receiptFreightAmount : null,
        receipt_freight_files: shouldPayFreight.value ? data.form.receiptFreightFiles : [],
        receipt_files: data.form.receiptFiles,
        loss_reason: showLossFields.value ? data.form.lossReason : '',
        loss_files: showLossFields.value ? data.form.lossFiles : [],
        receipt_remark: data.form.receiptRemark
      })
      if (!response.isSuccess) {
        hookComponent.$message({ type: 'error', content: response.errorMessage })
        return
      }
      hookComponent.$message({
        type: 'success',
        content: i18n.global.t('wms.erpPendingReceipt.receipt_confirm_success', { qty: response.data })
      })
      data.showDialog = false
      emit('saved')
    } finally {
      data.submitting = false
    }
  },
  sourceFreightPaymentType: () => {
    const labels: Record<string, string> = {
      FREE_SHIPPING: i18n.global.t('wms.erpPendingReceipt.free_shipping'),
      SELF_PAID: i18n.global.t('wms.erpPendingReceipt.self_paid'),
      COD: i18n.global.t('wms.erpPendingReceipt.cod')
    }
    const type = data.currentRow?.source_freight_payment_type ?? ''
    return labels[type] || type || '-'
  },
  itemInboundQty: (item: ReceiptItemForm) => Math.max(0, Number(item.actualReceiptQty || 0) - Number(item.lossQty || 0)),
  actualQtyRules: (item: ReceiptItemForm) => [
    (value: number) => Number.isInteger(Number(value)) || i18n.global.t('wms.erpPendingReceipt.receipt_qty_integer'),
    (value: number) => Number(value) >= 0 || i18n.global.t('wms.erpPendingReceipt.receipt_qty_non_negative'),
    (value: number) => Number(value) <= item.shipmentQty || i18n.global.t('wms.erpPendingReceipt.receipt_qty_exceeded')
  ],
  lossQtyRules: (item: ReceiptItemForm) => [
    (value: number) => Number.isInteger(Number(value)) || i18n.global.t('wms.erpPendingReceipt.loss_qty_integer'),
    (value: number) => Number(value) >= 0 || i18n.global.t('wms.erpPendingReceipt.loss_qty_non_negative'),
    (value: number) => Number(value) <= Number(item.actualReceiptQty || 0) || i18n.global.t('wms.erpPendingReceipt.loss_qty_exceeded')
  ]
})

defineExpose({
  openDialog: method.openDialog,
  closeDialog: method.closeDialog
})
</script>

<style lang="less" scoped>
.receiptDialog {
  max-height: 92vh;
}

.receiptContent {
  max-height: calc(92vh - 138px);
  overflow-y: auto;
  padding: 24px 32px;
}

.receiptInfoRow,
.receiptFormRow {
  display: grid;
  grid-template-columns: 112px minmax(0, 1fr);
  align-items: center;
  min-height: 48px;
  margin-bottom: 8px;
}

.receiptSummaryGrid,
.receiptFormGrid {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  column-gap: 32px;
}

.receiptSummaryGrid {
  border-bottom: 1px solid rgba(var(--v-border-color), var(--v-border-opacity));
  margin-bottom: 20px;
  padding-bottom: 12px;
}

.receiptFormGrid {
  align-items: start;
  grid-template-columns: minmax(0, 1fr);
}

.receiptLabel {
  color: rgba(var(--v-theme-on-surface), 0.68);
  font-size: 14px;
  text-align: right;
  padding-right: 20px;
}

.requiredLabel::before {
  color: rgb(var(--v-theme-error));
  content: '*';
  margin-right: 4px;
}

.receiptValue {
  color: rgb(var(--v-theme-on-surface));
  font-weight: 600;
}

.receiptQtyTip {
  color: rgb(var(--v-theme-error));
  font-size: 12px;
  line-height: 18px;
  margin-top: 4px;
}

.receiptControlRow {
  align-items: start;
}

.receiptControlRow .receiptLabel {
  padding-top: 14px;
}

.receiptControlRow > :last-child {
  min-width: 0;
}

.receiptFieldControl {
  min-width: 0;
}

.receiptItemsBlock {
  margin-bottom: 20px;
}

.receiptItemsTitle {
  color: rgb(var(--v-theme-on-surface));
  font-size: 15px;
  font-weight: 600;
  margin-bottom: 10px;
}

.receiptItemsTable {
  border: 1px solid rgba(var(--v-border-color), var(--v-border-opacity));
}

.receiptItemsTable th,
.receiptItemsTable td {
  min-width: 118px;
  padding: 10px 12px !important;
  vertical-align: middle;
}

.receiptItemsTable th:first-child,
.receiptItemsTable td:first-child {
  min-width: 220px;
}

.receiptProductName {
  color: rgb(var(--v-theme-on-surface));
  font-weight: 500;
}

.receiptProduct {
  display: flex;
  align-items: center;
  gap: 12px;
}

.receiptProductImage,
.receiptProductImageFallback {
  flex: 0 0 64px;
  border: 1px solid rgba(var(--v-border-color), var(--v-border-opacity));
  border-radius: 6px;
  overflow: hidden;
}

.receiptProductImageFallback {
  width: 64px;
  height: 64px;
  display: flex;
  align-items: center;
  justify-content: center;
  color: rgba(var(--v-theme-on-surface), 0.38);
  background: rgba(var(--v-theme-on-surface), 0.04);
}

.receiptProductText {
  min-width: 0;
}

.receiptProductSku {
  color: rgba(var(--v-theme-on-surface), 0.58);
  font-size: 12px;
  margin-top: 2px;
}

.receiptNumberCell {
  text-align: center;
}

.receiptInboundCell {
  color: rgb(var(--v-theme-primary));
  font-weight: 700;
}

@media (max-width: 860px) {
  .receiptSummaryGrid,
  .receiptFormGrid {
    grid-template-columns: 1fr;
  }
}
</style>
