import { beforeEach, describe, expect, it, vi } from 'vitest'
import {
  cancelDispatchOutbound,
  completeDispatchOrderWeighing,
  completeDispatchPicking,
  completeDispatchTaskWeighing,
  confirmDispatchOutbound,
  copyDispatchWeighingBox,
  createDispatchOrder,
  decideDispatchSourceChange,
  getDispatchOrder,
  getDispatchOrderPage,
  getDispatchOrderPrint,
  getDispatchStatusCounts,
  getDispatchTaskBoxes,
  getDispatchWarehouseAccess,
  getWorkflowPackingTaskPage,
  reconcileDispatchOrder,
  saveDispatchWeighingBox,
  signDispatchOrder,
  startDispatchWeighing
} from './dispatchWorkflow'

const { httpMock } = vi.hoisted(() => ({ httpMock: vi.fn() }))

vi.mock('@/utils/http/request', () => ({ default: httpMock }))

const expectLastRequest = (config: unknown): void => {
  expect(httpMock).toHaveBeenLastCalledWith(config)
}

describe('dispatch workflow api contract', () => {
  beforeEach(() => {
    httpMock.mockReset()
    httpMock.mockResolvedValue({ isSuccess: true, code: 0, errorMessage: '', data: {} })
  })

  it('locks warehouse, packing-task source and order query routes', () => {
    getDispatchWarehouseAccess()
    expectLastRequest({ url: '/warehouse/access-options', method: 'get' })

    const packingPage = {
      pageIndex: 1,
      pageSize: 20,
      searchObjects: [{ name: 'warehouse_id' as const, text: '320118', value: '320118' }]
    }
    getWorkflowPackingTaskPage(packingPage)
    expectLastRequest({ url: '/packing-task-query/page', method: 'post', data: packingPage })

    const createPayload = {
      warehouse_id: 320118,
      source_task_ids: [501, 502],
      idempotency_key: 'create-1'
    }
    createDispatchOrder(createPayload)
    expectLastRequest({ url: '/dispatch-workflow', method: 'post', data: createPayload })

    const orderPage = {
      status: 'PENDING_PICK' as const,
      warehouse_id: 320118,
      keyword: 'CW2608',
      pageIndex: 2,
      pageSize: 20
    }
    getDispatchOrderPage(orderPage)
    expectLastRequest({ url: '/dispatch-workflow/page', method: 'post', data: orderPage })

    getDispatchStatusCounts(320118)
    expectLastRequest({
      url: '/dispatch-workflow/counts', method: 'get', params: { warehouse_id: 320118 }
    })
  })

  it('locks order detail, reconciliation and request-time print routes to the WMS order id', () => {
    getDispatchOrder(12)
    expectLastRequest({ url: '/dispatch-workflow/12', method: 'get' })

    reconcileDispatchOrder(12)
    expectLastRequest({ url: '/dispatch-workflow/12/reconcile', method: 'post' })

    getDispatchOrderPrint(12)
    expectLastRequest({ url: '/dispatch-workflow/12/print', method: 'get' })
  })

  it('locks picking and every weighing route with order, task, box and concurrency identities', () => {
    const pickingPayload = { request_id: 'pick-1', row_version: 7 }
    completeDispatchPicking(12, pickingPayload)
    expectLastRequest({ url: '/dispatch-workflow/12/complete-picking', method: 'post', data: pickingPayload })

    const startPayload = { request_id: 'start-1', row_version: 8 }
    startDispatchWeighing(12, startPayload)
    expectLastRequest({ url: '/dispatch-workflow/12/start-weighing', method: 'post', data: startPayload })

    getDispatchTaskBoxes(12, 34)
    expectLastRequest({ url: '/dispatch-workflow/12/packing-tasks/34/boxes', method: 'get' })

    const savePayload = {
      request_id: 'save-1', row_version: 9, box_row_version: 3,
      weight: 1.2, length: 10, width: 8, height: 6
    }
    saveDispatchWeighingBox(12, 56, savePayload)
    expectLastRequest({ url: '/dispatch-workflow/12/boxes/56', method: 'put', data: savePayload })

    const copyPayload = {
      request_id: 'copy-1', row_version: 10, source_box_id: 56, target_box_row_version: 2
    }
    copyDispatchWeighingBox(12, 57, copyPayload)
    expectLastRequest({ url: '/dispatch-workflow/12/boxes/57/copy', method: 'post', data: copyPayload })

    const taskCompletePayload = { request_id: 'task-complete-1', row_version: 11 }
    completeDispatchTaskWeighing(12, 34, taskCompletePayload)
    expectLastRequest({
      url: '/dispatch-workflow/12/packing-tasks/34/complete-weighing',
      method: 'post',
      data: taskCompletePayload
    })

    const orderCompletePayload = { request_id: 'order-complete-1', row_version: 12 }
    completeDispatchOrderWeighing(12, orderCompletePayload)
    expectLastRequest({ url: '/dispatch-workflow/12/complete-weighing', method: 'post', data: orderCompletePayload })
  })

  it('locks source decision, outbound, cancellation and signing command payloads', () => {
    const decisionPayload = {
      decision: 'CONTINUE' as const,
      source_version: 'v2',
      reason: '人工确认继续',
      request_id: 'decision-1',
      row_version: 13
    }
    decideDispatchSourceChange(12, decisionPayload)
    expectLastRequest({ url: '/dispatch-workflow/12/source-decision', method: 'post', data: decisionPayload })

    const confirmPayload = { request_id: 'outbound-1', row_version: 14 }
    confirmDispatchOutbound(12, confirmPayload)
    expectLastRequest({ url: '/dispatch-workflow/12/confirm-outbound', method: 'post', data: confirmPayload })

    const cancelPayload = { request_id: 'cancel-outbound-1', row_version: 15 }
    cancelDispatchOutbound(12, cancelPayload)
    expectLastRequest({ url: '/dispatch-workflow/12/cancel-outbound', method: 'post', data: cancelPayload })

    const signPayload = { request_id: 'sign-1', row_version: 16, damaged_qty: 2 }
    signDispatchOrder(12, signPayload)
    expectLastRequest({ url: '/dispatch-workflow/12/sign', method: 'post', data: signPayload })
  })

  it('preserves backend workflow error codes returned through the axios error object', async () => {
    httpMock.mockResolvedValueOnce({
      response: {
        data: {
          isSuccess: false,
          code: 409,
          errorMessage: 'SOURCE_CHANGE_PENDING',
          data: null
        }
      }
    })

    const result = await decideDispatchSourceChange(12, {
      decision: 'CONTINUE',
      source_version: 'v2',
      reason: '人工确认继续',
      request_id: 'decision-error-1',
      row_version: 17
    })

    expect(result.isSuccess).toBe(false)
    expect(result.errorMessage).toBe('SOURCE_CHANGE_PENDING')
  })
})
