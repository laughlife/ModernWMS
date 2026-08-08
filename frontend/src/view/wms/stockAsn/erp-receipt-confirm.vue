<template>
  <v-dialog v-model="data.showDialog" width="720" transition="dialog-top-transition" persistent>
    <v-card class="formCard receiptDialog">
      <v-toolbar color="white" :title="$t('wms.erpPendingReceipt.receipt_title')">
        <template #append>
          <v-btn icon="mdi-close" variant="text" @click="method.closeDialog"></v-btn>
        </template>
      </v-toolbar>

      <v-divider></v-divider>

      <v-card-text class="receiptContent">
        <v-form ref="formRef">
          <div class="receiptInfoRow">
            <span class="receiptLabel">{{ $t('wms.erpPendingReceipt.purchase_no') }}</span>
            <span class="receiptValue">{{ data.currentRow?.purchase_no || '-' }}</span>
          </div>
          <div class="receiptInfoRow">
            <span class="receiptLabel">{{ $t('wms.erpPendingReceipt.source_document') }}</span>
            <span class="receiptValue">{{ method.sourceDocument() }}</span>
          </div>
          <div class="receiptInfoRow">
            <span class="receiptLabel">{{ $t('wms.erpPendingReceipt.forwarder_or_warehouse') }}</span>
            <span class="receiptValue">{{ data.currentRow?.freight_forwarder_name || data.currentRow?.warehouse_name || '-' }}</span>
          </div>
          <div class="receiptInfoRow">
            <span class="receiptLabel">{{ $t('wms.erpPendingReceipt.shipment_qty') }}</span>
            <span class="receiptValue">{{ data.currentRow?.shipment_qty ?? 0 }}</span>
          </div>

          <v-text-field
            v-model.number="data.form.actualReceiptQty"
            type="number"
            min="0"
            :max="data.currentRow?.shipment_qty ?? undefined"
            step="1"
            variant="outlined"
            density="comfortable"
            :label="$t('wms.erpPendingReceipt.actual_receipt_qty')"
            :rules="data.rules.actualReceiptQty"
          ></v-text-field>
          <div class="receiptQtyTip">{{ $t('wms.erpPendingReceipt.receipt_qty_tip') }}</div>

          <div class="receiptInfoRow">
            <span class="receiptLabel">{{ $t('wms.erpPendingReceipt.source_freight_payment_type') }}</span>
            <span class="receiptValue">{{ method.sourceFreightPaymentType() }}</span>
          </div>

          <div class="receiptFormRow">
            <span class="receiptLabel requiredLabel">{{ $t('wms.erpPendingReceipt.receipt_freight_payment_status') }}</span>
            <v-btn-toggle v-model="data.form.receiptFreightPaymentStatus" color="primary" mandatory divided>
              <v-btn value="NO_PAY">{{ $t('wms.erpPendingReceipt.no_pay') }}</v-btn>
              <v-btn value="PAY">{{ $t('wms.erpPendingReceipt.pay') }}</v-btn>
            </v-btn-toggle>
          </div>

          <template v-if="shouldPayFreight">
            <v-text-field
              v-model.number="data.form.receiptFreightAmount"
              type="number"
              min="0.01"
              step="0.01"
              variant="outlined"
              density="comfortable"
              :label="$t('wms.erpPendingReceipt.receipt_freight_amount')"
              :rules="data.rules.receiptFreightAmount"
            ></v-text-field>
            <v-file-input
              v-model="data.form.receiptFreightFiles"
              accept="image/*"
              multiple
              chips
              clearable
              variant="outlined"
              density="comfortable"
              prepend-icon="mdi-receipt-text-outline"
              :label="$t('wms.erpPendingReceipt.receipt_freight_attachments')"
              :hint="$t('wms.erpPendingReceipt.attachment_tip')"
              persistent-hint
            ></v-file-input>
          </template>

          <template v-if="showLossFields">
            <div class="receiptInfoRow">
              <span class="receiptLabel">{{ $t('wms.erpPendingReceipt.loss_qty') }}</span>
              <span class="receiptValue lossValue">{{ lossQty }}</span>
            </div>
            <v-textarea
              v-model="data.form.lossReason"
              variant="outlined"
              rows="3"
              maxlength="500"
              counter
              :label="$t('wms.erpPendingReceipt.loss_reason')"
              :rules="data.rules.lossReason"
            ></v-textarea>
            <v-file-input
              v-model="data.form.lossFiles"
              accept="image/*"
              multiple
              chips
              clearable
              variant="outlined"
              density="comfortable"
              prepend-icon="mdi-image-multiple-outline"
              :label="$t('wms.erpPendingReceipt.loss_attachments')"
              :hint="$t('wms.erpPendingReceipt.attachment_tip')"
              persistent-hint
            ></v-file-input>
          </template>

          <v-file-input
            v-model="data.form.receiptFiles"
            accept="image/*"
            multiple
            chips
            clearable
            variant="outlined"
            density="comfortable"
            prepend-icon="mdi-image-multiple-outline"
            :label="$t('wms.erpPendingReceipt.receipt_attachments')"
            :hint="$t('wms.erpPendingReceipt.attachment_tip')"
            persistent-hint
          ></v-file-input>

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
            {{ $t('wms.erpPendingReceipt.receipt_submit_pending') }}
          </v-alert>
        </v-form>
      </v-card-text>

      <v-divider></v-divider>

      <v-card-actions class="justify-end px-6 py-4">
        <v-btn variant="text" @click="method.closeDialog">{{ $t('system.page.close') }}</v-btn>
        <v-btn color="primary" variant="flat" disabled>{{ $t('system.page.confirm') }}</v-btn>
      </v-card-actions>
    </v-card>
  </v-dialog>
</template>

<script lang="ts" setup>
import { computed, nextTick, reactive, ref } from 'vue'
import i18n from '@/languages/i18n'
import type { ErpPendingReceiptVO } from '@/types/WMS/StockAsn'

type ReceiptFreightPaymentStatus = 'NO_PAY' | 'PAY'

const formRef = ref()

const data = reactive({
  showDialog: false,
  currentRow: null as ErpPendingReceiptVO | null,
  form: {
    actualReceiptQty: 0,
    receiptFreightPaymentStatus: 'NO_PAY' as ReceiptFreightPaymentStatus,
    receiptFreightAmount: null as number | null,
    receiptFreightFiles: [] as File[],
    receiptFiles: [] as File[],
    lossReason: '',
    lossFiles: [] as File[],
    receiptRemark: ''
  },
  rules: {
    actualReceiptQty: [
      (value: number) => Number.isInteger(Number(value)) || i18n.global.t('wms.erpPendingReceipt.receipt_qty_integer'),
      (value: number) => Number(value) >= 0 || i18n.global.t('wms.erpPendingReceipt.receipt_qty_non_negative'),
      (value: number) => Number(value) <= (data.currentRow?.shipment_qty ?? 0) || i18n.global.t('wms.erpPendingReceipt.receipt_qty_exceeded')
    ],
    receiptFreightAmount: [
      (value: number | null) => !shouldPayFreight.value || Number(value) > 0 || i18n.global.t('wms.erpPendingReceipt.freight_amount_required')
    ],
    lossReason: [
      (value: string) => !showLossFields.value || !!value.trim() || i18n.global.t('wms.erpPendingReceipt.loss_reason_required')
    ]
  }
})

const shouldPayFreight = computed(() => data.form.receiptFreightPaymentStatus === 'PAY')
const lossQty = computed(() => Math.max(0, (data.currentRow?.shipment_qty ?? 0) - Number(data.form.actualReceiptQty || 0)))
const showLossFields = computed(() => data.currentRow?.source_type === 'STOCK_DISPATCH' && lossQty.value > 0)

const method = reactive({
  openDialog: (row: ErpPendingReceiptVO) => {
    data.currentRow = row
    data.form.actualReceiptQty = row.shipment_qty
    data.form.receiptFreightPaymentStatus = row.source_freight_payment_type === 'COD' ? 'PAY' : 'NO_PAY'
    data.form.receiptFreightAmount = null
    data.form.receiptFreightFiles = []
    data.form.receiptFiles = []
    data.form.lossReason = ''
    data.form.lossFiles = []
    data.form.receiptRemark = ''
    data.showDialog = true
    nextTick(() => formRef.value?.resetValidation?.())
  },
  closeDialog: () => {
    data.showDialog = false
  },
  sourceDocument: () => {
    if (!data.currentRow) return '-'
    return data.currentRow.source_type === 'STOCK_DISPATCH'
      ? data.currentRow.source_stock_move_no || data.currentRow.shipment_batch_no || '-'
      : data.currentRow.purchase_no || data.currentRow.shipment_batch_no || '-'
  },
  sourceFreightPaymentType: () => {
    const labels: Record<string, string> = {
      FREE_SHIPPING: i18n.global.t('wms.erpPendingReceipt.free_shipping'),
      SELF_PAID: i18n.global.t('wms.erpPendingReceipt.self_paid'),
      COD: i18n.global.t('wms.erpPendingReceipt.cod')
    }
    const type = data.currentRow?.source_freight_payment_type ?? ''
    return labels[type] || type || '-'
  }
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
  grid-template-columns: 140px minmax(0, 1fr);
  align-items: center;
  min-height: 48px;
  margin-bottom: 8px;
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
  margin: -14px 0 14px 140px;
}

.lossValue {
  color: rgb(var(--v-theme-error));
}
</style>
