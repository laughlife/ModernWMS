import { flushPromises, mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { defineComponent, h } from 'vue'
import TabCompleted from './tabCompleted.vue'

const {
  getDispatchOrderPage,
  getDispatchOrder,
  getDispatchTaskBoxes,
  setAllRowExpand
} = vi.hoisted(() => ({
  getDispatchOrderPage: vi.fn(),
  getDispatchOrder: vi.fn(),
  getDispatchTaskBoxes: vi.fn(),
  setAllRowExpand: vi.fn()
}))

vi.mock('@/api/wms/dispatchWorkflow', () => ({
  cancelDispatchOutbound: vi.fn(),
  getDispatchOrder,
  getDispatchOrderPage,
  getDispatchTaskBoxes,
  signDispatchOrder: vi.fn()
}))

vi.mock('@/components/system', () => ({
  hookComponent: { $message: vi.fn(), $dialog: vi.fn() }
}))

vi.mock('@/utils/common', () => ({ getMenuAuthorityList: () => [] }))
vi.mock('@/utils/exportTable', () => ({ exportData: vi.fn() }))
vi.mock('@/components/system/btnGroup.vue', () => ({ default: { template: '<div />' } }))
vi.mock('@/components/tooltip-btn.vue', () => ({ default: { template: '<button />' } }))
vi.mock('./dispatch-search-filters.vue', () => ({ default: { template: '<div />' } }))
vi.mock('@/components/custom-pager.vue', () => ({ default: { template: '<div />' } }))

const VxeTableStub = defineComponent({
  setup(_, { expose }) {
    expose({ setAllRowExpand })
    return () => h('div', { class: 'vxe-table-stub' })
  }
})

describe('已出库列表', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    getDispatchOrderPage.mockResolvedValue({
      isSuccess: true,
      data: {
        totals: 1,
        rows: [{
          id: 18,
          dispatch_no: 'WMS-OUT-18',
          warehouse_id: 1,
          status: 'OUTBOUND',
          packing_task_nos: ['CW-18'],
          creator: 'admin',
          create_time: '2026-08-19 09:00:00',
          row_version: 1
        }]
      }
    })
    getDispatchOrder.mockResolvedValue({
      isSuccess: true,
      data: {
        id: 18,
        packing_tasks: [{ id: 31, items: [] }]
      }
    })
    getDispatchTaskBoxes.mockResolvedValue({ isSuccess: true, data: [] })
  })

  it('加载列表后默认展开全部行并加载明细', async () => {
    const wrapper = mount(TabCompleted, {
      props: { warehouseId: 1 },
      global: {
        stubs: {
          'vxe-table': VxeTableStub,
          'vxe-column': true,
          BtnGroup: true,
          DispatchSearchFilters: true,
          TooltipBtn: true,
          ProductImage: true,
          customPager: true
        },
        config: { warnHandler: () => undefined }
      }
    })

    await (wrapper.vm as unknown as { getCompleted: () => Promise<void> }).getCompleted()
    await flushPromises()

    expect(setAllRowExpand).toHaveBeenCalledWith(true)
    expect(getDispatchOrder).toHaveBeenCalledWith(18)
    expect(getDispatchTaskBoxes).toHaveBeenCalledWith(18, 31)
  })
})
