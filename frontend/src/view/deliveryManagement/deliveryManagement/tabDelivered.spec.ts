import { flushPromises, mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { computed, defineComponent, h, inject, provide } from 'vue'
import TabDelivered from './tabDelivered.vue'

const { getDispatchOrderPage, getDispatchOrder, getDispatchTaskBoxes } = vi.hoisted(() => ({
  getDispatchOrderPage: vi.fn(),
  getDispatchOrder: vi.fn(),
  getDispatchTaskBoxes: vi.fn()
}))

vi.mock('@/api/wms/dispatchWorkflow', () => ({
  confirmDispatchOutbound: vi.fn(),
  decideDispatchSourceChange: vi.fn(),
  getDispatchOrder,
  getDispatchOrderPage,
  getDispatchTaskBoxes
}))
vi.mock('@/components/system', () => ({ hookComponent: { $message: vi.fn(), $dialog: vi.fn() } }))
vi.mock('@/utils/common', () => ({ getMenuAuthorityList: () => [] }))
vi.mock('@/store/module/dispatchWarehouse', () => ({
  useDispatchWarehouseStore: () => ({ warehouseOptions: [{ id: 320118, name: '广州仓' }] })
}))
vi.mock('@/components/system/btnGroup.vue', () => ({ default: { template: '<div />' } }))
vi.mock('@/components/system/product-image.vue', () => ({ default: { template: '<div />' } }))
vi.mock('@/components/tooltip-btn.vue', () => ({ default: { template: '<button />' } }))
vi.mock('./dispatch-search-filters.vue', () => ({ default: { template: '<div />' } }))
vi.mock('./dispatch-carrier-dialog.vue', () => ({ default: { template: '<div />' } }))
vi.mock('@/components/custom-pager.vue', () => ({ default: { template: '<div />' } }))

const tableRowKey = Symbol('table-row')
const VxeTableStub = defineComponent({
  props: { data: { type: Array, default: () => [] } },
  setup(props, { slots }) {
    provide(tableRowKey, computed(() => props.data[0]))
    return () => h('div', { class: 'vxe-table-stub' }, slots.default?.())
  }
})
const VxeColumnStub = defineComponent({
  props: { title: { type: String, default: '' } },
  setup(props, { slots }) {
    const row = inject<any>(tableRowKey)
    return () => h('section', { class: 'vxe-column-stub', 'data-title': props.title }, row?.value
      ? [slots.default?.({ row: row.value }), slots.content?.({ row: row.value })]
      : [])
  }
})
const VChipStub = defineComponent({
  props: { color: { type: String, default: '' }, variant: { type: String, default: '' } },
  setup(props, { slots }) {
    return () => h('span', { class: 'v-chip-stub', 'data-color': props.color, 'data-variant': props.variant }, slots.default?.())
  }
})

describe('待出库主表信息', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    getDispatchOrderPage.mockResolvedValue({
      isSuccess: true,
      data: {
        totals: 1,
        rows: [{
          id: 31, dispatch_no: 'PK20260818161145678491', warehouse_id: 320118,
          status: 'PENDING_OUTBOUND', packing_task_nos: ['CW2608170032'], creator: 'admin',
          create_time: '2026-08-19 09:00:00', last_update_time: '2026-08-19 09:10:00',
          source_change_pending: false, pending_source_version: '', source_change_snapshot: '',
          accepted_source_version: 'v1', outbound_source_anomaly: false, outbound_source_anomaly_snapshot: '',
          signed_qty: null, damaged_qty: null, signed_at: null, signed_by_name: '',
          notification_status: 'NONE', notification_last_error: '', row_version: 1,
          carrier_warehouse_id: 9, carrier_unit: '广州自建仓'
        }]
      }
    })
    getDispatchOrder.mockResolvedValue({
      isSuccess: true,
      data: {
        id: 31, dispatch_no: 'PK20260818161145678491', warehouse_id: 320118,
        status: 'PENDING_OUTBOUND', packing_task_nos: ['CW2608170032'], packing_tasks: [{
          id: 101, source_task_id: 1001, source_task_no: 'CW2608170032', status: 'PENDING_OUTBOUND',
          source_version: 'v1', expected_box_count: 1, measured_box_count: 1,
          items: [{
            id: 1, source_item_id: 11, source_commodity_id: null, wms_sku_id: 21,
            commodity_sku: 'SKU-A', commodity_name: '商品A', main_image: '', fn_sku: 'FN-A', msku: 'M-A',
            task_qty: 1, required_qty: 5, source_stock_available: null
          }]
        }], source_version: 'v1', source_change_pending: false, pending_source_version: '',
        source_change_snapshot: '', accepted_source_version: 'v1', outbound_source_anomaly: false,
        outbound_source_anomaly_snapshot: '', signed_qty: null, damaged_qty: null, signed_at: null,
        signed_by_name: '', notification_status: 'NONE', notification_last_error: '', row_version: 1,
        creator: 'admin', create_time: '2026-08-19 09:00:00', last_update_time: '2026-08-19 09:10:00'
      }
    })
    getDispatchTaskBoxes.mockResolvedValue({
      isSuccess: true,
      data: [{
        id: 1, packing_task_id: 101, source_box_identity: 'A-1', box_sequence: 1,
        weight: 10, length: 40, width: 30, height: 20, measurement_status: 'MEASURED',
        copied_from_box_id: null, row_version: 1, items: [{ packing_task_item_id: 1, task_qty: 0 }]
      }]
    })
  })

  it('合并单号信息并在承运信息前显示带差异提示的计划和实际装货量', async () => {
    const wrapper = mount(TabDelivered, {
      props: { warehouseId: null },
      global: {
        stubs: {
          'vxe-table': VxeTableStub,
          'vxe-column': VxeColumnStub,
          'v-chip': VChipStub
        },
        mocks: { $t: (key: string) => key },
        config: { warnHandler: () => undefined }
      }
    })
    await wrapper.setProps({ warehouseId: 320118 })
    await flushPromises()
    await wrapper.vm.$nextTick()

    expect(getDispatchOrder).toHaveBeenCalledWith(31)
    expect(getDispatchTaskBoxes).toHaveBeenCalledWith(31, 101)
    expect(wrapper.find('.order-detail').text()).toContain('装箱任务')

    const numberInfo = wrapper.get('[data-title="单号信息"]')
    expect(numberInfo.text()).toContain('PK20260818161145678491')
    expect(numberInfo.text()).toContain('CW2608170032')
    expect(numberInfo.text()).toContain('广州仓')
    expect(wrapper.find('[data-title="WMS拣货单"]').exists()).toBe(false)
    expect(wrapper.find('[data-title="装箱任务"]').exists()).toBe(false)

    const planned = wrapper.get('[data-title="计划装货量"]')
    const actual = wrapper.get('[data-title="实际装货量"]')
    const carrier = wrapper.get('[data-title="承运信息"]')
    expect(planned.text()).toContain('5 件')
    expect(actual.text()).toContain('0 件')
    expect(planned.get('[data-color="error"]').attributes('data-variant')).toBe('flat')
    expect(actual.get('[data-color="error"]').attributes('data-variant')).toBe('flat')
    expect(planned.element.compareDocumentPosition(actual.element) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy()
    expect(actual.element.compareDocumentPosition(carrier.element) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy()
  })
})
