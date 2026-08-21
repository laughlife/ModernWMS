import { flushPromises, shallowMount } from '@vue/test-utils'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import ErpReceiptConfirm from './erp-receipt-confirm.vue'

const api = vi.hoisted(() => ({
  getWarehouseAreaSelect: vi.fn(),
  getGoodsLocation: vi.fn(),
  getOwnerOfCargoAll: vi.fn()
}))

vi.mock('@/api/base/warehouseSetting', () => ({
  getWarehouseAreaSelect: api.getWarehouseAreaSelect,
  getGoodsLocation: api.getGoodsLocation
}))

vi.mock('@/api/base/ownerOfCargo', () => ({
  getOwnerOfCargoAll: api.getOwnerOfCargoAll
}))

vi.mock('@/api/wms/stockAsn', () => ({
  confirmErpReceipt: vi.fn()
}))

vi.mock('@/components/system', () => ({
  hookComponent: { $message: vi.fn() }
}))

describe('erp receipt confirmation', () => {
  beforeEach(() => {
    api.getWarehouseAreaSelect.mockResolvedValue({
      data: { isSuccess: true, data: [{ id: 7, area_name: '4.飞黄腾达', is_valid: true }] }
    })
    api.getGoodsLocation.mockResolvedValue({
      data: { isSuccess: true, data: [{ id: 6, warehouse_area_id: 7, location_name: '4.飞黄腾达', is_valid: true }] }
    })
    api.getOwnerOfCargoAll.mockResolvedValue({ data: { isSuccess: true, data: [] } })
  })

  it('shows the group area and unique location that will be allocated on confirmation', async () => {
    const wrapper = shallowMount(ErpReceiptConfirm, {
      global: {
        config: {
          compilerOptions: {
            isCustomElement: (tag) => tag.startsWith('v-')
          }
        },
        mocks: {
          $t: (key: string) => key
        },
        stubs: {
          'product-image': true,
          'erp-receipt-image-upload': true
        }
      }
    })

    await wrapper.vm.openDialog({
      id: 2067,
      source_version: 1,
      wms_warehouse_id: 1,
      purchase_no: 'PO2608100027',
      warehouse_name: '有座山深圳仓',
      shipment_qty: 600,
      source_freight_payment_type: 'FREE_SHIPPING',
      product_list: [{
        source_item_key: '3470:2580:2766214:pifutanxingbuchongji:0',
        commodity_id: 2766214,
        sku: 'pifutanxingbuchongji',
        product_name: '皮肤弹性补充剂',
        quantity: 600,
        dept_name: '飞黄腾达',
        order_user_name: '张冬艳',
        default_warehouse_area_id: 7,
        default_warehouse_area_name: '4.飞黄腾达',
        default_goods_location_id: null,
        default_goods_location_name: '',
        default_goods_owner_id: 0,
        default_goods_owner_name: '飞黄腾达 / 张冬艳'
      }]
    } as any)
    await flushPromises()

    expect(wrapper.text()).toContain('已按小组自动分配库区：4.飞黄腾达')
    expect(wrapper.text()).toContain('确认入库时将自动使用唯一库位：4.飞黄腾达')
  })
})
