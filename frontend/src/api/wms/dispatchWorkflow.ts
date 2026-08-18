import http from '@/utils/http/request'
import type { AxiosRequestConfig } from 'axios'
import { unwrapApiResult } from '@/utils/http/apiResult'
import type { ApiResult } from '@/types/System/ApiResult'
import type {
  CompletePickingRequest,
  CompletePickingResult,
  CopyWeighingBoxRequest,
  CreateDispatchOrderRequest,
  DispatchOrderDetail,
  DispatchOrderPage,
  DispatchOrderPageRequest,
  DispatchStatusCounts,
  OutboundCommandRequest,
  OutboundCommandResult,
  PackingPlan,
  SavePackingPlanRequest,
  ConfirmActualPackingRequest,
  PackingTaskPage,
  PackingTaskPageRequest,
  PageData,
  RollbackPendingPickRequest,
  RollbackPendingPickResult,
  SaveWeighingBoxRequest,
  SignDispatchOrderRequest,
  SignDispatchOrderResult,
  SourceDecisionRequest,
  SourceDecisionResult,
  WarehouseAccess,
  WeighingBox,
  WeighingCommandResult,
  WeighingOrderCommandRequest
} from '@/types/DeliveryManagement/DispatchWorkflow'
import type {
  PackingTaskStockPageRequest,
  PackingTaskStockSelectRequest,
  SelectableStockVO
} from '@/types/DeliveryManagement/PackingTask'

const request = <T>(config: AxiosRequestConfig): Promise<ApiResult<T>> =>
  http(config).then((response) => {
    try {
      return unwrapApiResult<T>(response)
    } catch (error) {
      const axiosErrorResponse = (response as { response?: unknown } | null)?.response
      if (axiosErrorResponse) return unwrapApiResult<T>(axiosErrorResponse)
      throw error
    }
  })

export interface DispatchCarrierOption {
  id: number
  name: string
}

export interface SetDispatchCarrierRequest {
  order_ids: number[]
  carrier_warehouse_id: number
}

export interface SetDispatchCarrierResult {
  updated_order_count: number
  carrier_warehouse_id: number
  carrier_unit: string
}

export const getDispatchWarehouseAccess = () => request<WarehouseAccess>({
  url: '/warehouse/access-options', method: 'get'
})

export const getWorkflowPackingTaskPage = (data: PackingTaskPageRequest, hideLoading = false) => request<PackingTaskPage>({
  url: '/packing-task-query/page', method: 'post', data,
  ...(hideLoading ? { hideLoading: true } : {})
})

export const createDispatchOrder = (data: CreateDispatchOrderRequest) => request<DispatchOrderDetail>({
  url: '/dispatch-workflow', method: 'post', data
})

export const getDispatchOrderPage = (data: DispatchOrderPageRequest) => request<DispatchOrderPage>({
  url: '/dispatch-workflow/page', method: 'post', data
})

export const getDispatchStatusCounts = (warehouseId: number) => request<DispatchStatusCounts>({
  url: '/dispatch-workflow/counts', method: 'get', params: { warehouse_id: warehouseId }
})

export const getDispatchOrder = (orderId: number, hideLoading = false) => request<DispatchOrderDetail>({
  url: `/dispatch-workflow/${orderId}`, method: 'get',
  ...(hideLoading ? { hideLoading: true } : {})
})

export const reconcileDispatchOrder = (orderId: number) => request<DispatchOrderDetail>({
  url: `/dispatch-workflow/${orderId}/reconcile`, method: 'post'
})

export const getDispatchOrderPrint = (orderId: number) => request<DispatchOrderDetail>({
  url: `/dispatch-workflow/${orderId}/print`, method: 'get'
})

export const completeDispatchPicking = (orderId: number, data: CompletePickingRequest) => request<CompletePickingResult>({
  url: `/dispatch-workflow/${orderId}/complete-picking`, method: 'post', data
})

export const rollbackPendingPick = (orderId: number, data: RollbackPendingPickRequest) => request<RollbackPendingPickResult>({
  url: `/dispatch-workflow/${orderId}/rollback-pending-pick`, method: 'post', data
})

export const startDispatchWeighing = (orderId: number, data: WeighingOrderCommandRequest) => request<WeighingCommandResult>({
  url: `/dispatch-workflow/${orderId}/start-weighing`, method: 'post', data
})

export const getDispatchTaskBoxes = (orderId: number, packingTaskId: number) => request<WeighingBox[]>({
  url: `/dispatch-workflow/${orderId}/packing-tasks/${packingTaskId}/boxes`, method: 'get'
})

export const getDispatchPackingPlan = (orderId: number, packingTaskId: number, hideLoading = false) => request<PackingPlan>({
  url: `/dispatch-workflow/${orderId}/packing-tasks/${packingTaskId}/packing-plan`, method: 'get',
  ...(hideLoading ? { hideLoading: true } : {})
})

export const saveDispatchPackingPlan = (orderId: number, packingTaskId: number, data: SavePackingPlanRequest) =>
  request<PackingPlan>({ url: `/dispatch-workflow/${orderId}/packing-tasks/${packingTaskId}/packing-plan`, method: 'put', data })

export const confirmDispatchPacking = (orderId: number, packingTaskId: number, data: ConfirmActualPackingRequest) =>
  request<PackingPlan>({ url: `/dispatch-workflow/${orderId}/packing-tasks/${packingTaskId}/confirm-packing`, method: 'post', data })

export const confirmDispatchActualPacking = (orderId: number, packingTaskId: number, data: ConfirmActualPackingRequest) =>
  request<PackingPlan>({ url: `/dispatch-workflow/${orderId}/packing-tasks/${packingTaskId}/confirm-actual`, method: 'post', data })

export const saveDispatchWeighingBox = (orderId: number, boxId: number, data: SaveWeighingBoxRequest) =>
  request<WeighingCommandResult>({ url: `/dispatch-workflow/${orderId}/boxes/${boxId}`, method: 'put', data })

export const copyDispatchWeighingBox = (orderId: number, targetBoxId: number, data: CopyWeighingBoxRequest) =>
  request<WeighingCommandResult>({ url: `/dispatch-workflow/${orderId}/boxes/${targetBoxId}/copy`, method: 'post', data })

export const completeDispatchTaskWeighing = (
  orderId: number,
  packingTaskId: number,
  data: WeighingOrderCommandRequest
) => request<WeighingCommandResult>({
  url: `/dispatch-workflow/${orderId}/packing-tasks/${packingTaskId}/complete-weighing`, method: 'post', data
})

export const completeDispatchOrderWeighing = (orderId: number, data: WeighingOrderCommandRequest) =>
  request<WeighingCommandResult>({ url: `/dispatch-workflow/${orderId}/complete-weighing`, method: 'post', data })

export const decideDispatchSourceChange = (orderId: number, data: SourceDecisionRequest) =>
  request<SourceDecisionResult>({ url: `/dispatch-workflow/${orderId}/source-decision`, method: 'post', data })

export const confirmDispatchOutbound = (orderId: number, data: OutboundCommandRequest) =>
  request<OutboundCommandResult>({ url: `/dispatch-workflow/${orderId}/confirm-outbound`, method: 'post', data })

export const getDispatchCarrierOptions = () => request<DispatchCarrierOption[]>({
  url: '/dispatch-workflow/carrier-options', method: 'get', hideLoading: true
})

export const setDispatchCarrier = (data: SetDispatchCarrierRequest) => request<SetDispatchCarrierResult>({
  url: '/dispatch-workflow/carrier', method: 'put', data, hideLoading: true
})

export const cancelDispatchOutbound = (orderId: number, data: OutboundCommandRequest) =>
  request<OutboundCommandResult>({ url: `/dispatch-workflow/${orderId}/cancel-outbound`, method: 'post', data })

export const signDispatchOrder = (orderId: number, data: SignDispatchOrderRequest) =>
  request<SignDispatchOrderResult>({ url: `/dispatch-workflow/${orderId}/sign`, method: 'post', data })

export const getPackingTaskSelectableStock = (data: PackingTaskStockPageRequest) =>
  request<PageData<SelectableStockVO>>({ url: '/packing-task-query/selectable-stock', method: 'post', data })

export const selectPackingTaskStock = (data: PackingTaskStockSelectRequest) =>
  request<boolean>({ url: '/packing-task-query/select-stock', method: 'post', data })

export const deletePackingTaskStockSelection = (data: PackingTaskStockSelectRequest) =>
  request<boolean>({ url: '/packing-task-query/delete-selection', method: 'post', data })
