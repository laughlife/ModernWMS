import { flushPromises, mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { defineComponent } from 'vue'
import type { PackingPlan } from '@/types/DeliveryManagement/DispatchWorkflow'
import PackingTaskWeighingEditor from './packing-task-weighing-editor.vue'

const { getDispatchPackingPlan, getDispatchActualPackingStock } = vi.hoisted(() => ({
  getDispatchPackingPlan: vi.fn(),
  getDispatchActualPackingStock: vi.fn()
}))

vi.mock('@/api/wms/dispatchWorkflow', () => ({
  getDispatchPackingPlan,
  getDispatchActualPackingStock,
  saveDispatchPackingPlan: vi.fn(),
  confirmDispatchPacking: vi.fn(),
  confirmDispatchActualPacking: vi.fn(),
  completeDispatchTaskWeighing: vi.fn(),
  completeDispatchOrderWeighing: vi.fn()
}))

vi.mock('@/components/system', () => ({
  hookComponent: {
    $message: vi.fn(),
    $dialog: vi.fn()
  }
}))

const plan = (): PackingPlan => ({
  order_id: 1,
  packing_task_id: 2,
  packing_task_no: 'CW-1',
  packing_plan_status: 'DRAFT',
  row_version: 0,
  task_row_version: 0,
  items: [{
    id: 11,
    commodity_sku: 'SKU-1',
    commodity_name: '商品1',
    fn_sku: 'FNSKU-1',
    msku: 'MSKU-1',
    main_image: '',
    task_qty: 5,
    variant_qty: 1,
    required_qty: 5,
    actual_packed_task_qty: null,
    actual_packed_required_qty: null
  }],
  boxes: []
})

const VTextFieldStub = defineComponent({
  inheritAttrs: false,
  props: { modelValue: { type: String, default: '' } },
  emits: ['update:modelValue', 'blur'],
  setup(_props, { emit }) {
    const update = (event: Event) => emit('update:modelValue', (event.target as HTMLInputElement).value)
    return { update }
  },
  template: '<input v-bind="$attrs" :value="modelValue" @input="update" @blur="$emit(\'blur\')">'
})

const mountEditor = async () => {
  const wrapper = mount(PackingTaskWeighingEditor, {
    props: { orderId: 1, packingTaskId: 2 },
    global: {
      stubs: {
        ProductImage: true,
        'v-text-field': VTextFieldStub
      },
      config: { warnHandler: () => undefined }
    }
  })
  await flushPromises()
  return wrapper
}

describe('装箱编辑器计划箱数布局', () => {
  beforeEach(() => {
    getDispatchPackingPlan.mockResolvedValue({ isSuccess: true, data: plan() })
    getDispatchActualPackingStock.mockResolvedValue({ isSuccess: true, data: [] })
  })

  it('将计划箱数和新增箱放在装箱进度与箱子明细之间', async () => {
    const wrapper = await mountEditor()

    const progress = wrapper.find('.packing-progress')
    const toolbar = wrapper.find('.box-toolbar')
    const plannedBoxCount = wrapper.find('.planned-box-count')
    const boxCard = wrapper.find('.box-card')

    expect(plannedBoxCount.exists()).toBe(true)
    expect(toolbar.classes()).toContain('justify-center')
    expect(progress.element.compareDocumentPosition(toolbar.element) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy()
    expect(toolbar.element.compareDocumentPosition(boxCard.element) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy()
  })

  it('输入计划箱数后立即补齐对应箱子并过滤非数字', async () => {
    const wrapper = await mountEditor()
    const input = wrapper.get('input.planned-box-count')

    await input.setValue('a3')

    expect((input.element as HTMLInputElement).value).toBe('3')
    expect(wrapper.findAll('.box-card')).toHaveLength(3)
  })

  it('新建计划的第一箱不伪造实际商品并提供添加入口', async () => {
    const wrapper = await mountEditor()

    expect(wrapper.findAll('.box-item-row')).toHaveLength(0)
    expect(wrapper.text()).toContain('添加实际商品')
  })
})
