import type {
  CopyWeighingBoxRequest,
  DispatchOrderSummary,
  DispatchSourceDecision,
  SaveWeighingBoxRequest,
  SourceDecisionRequest,
  WeighingBox
} from '@/types/DeliveryManagement/DispatchWorkflow'

export interface SaveMeasurementCommand {
  orderId: number
  boxId: number
  payload: SaveWeighingBoxRequest
}

export interface CopyMeasurementCommand {
  orderId: number
  targetBoxId: number
  payload: CopyWeighingBoxRequest
}

export interface WeighingListRequestIdentity {
  sequence: number
  warehouseId: number
  keyword: string
  pageIndex: number
  pageSize: number
}

export interface DialogRequestIdentity {
  generation: number
  orderId: number
}

export interface CurrentDialogIdentity extends DialogRequestIdentity {
  visible: boolean
}

export const isCurrentWeighingListRequest = (
  request: WeighingListRequestIdentity,
  current: WeighingListRequestIdentity
): boolean => request.sequence === current.sequence
  && request.warehouseId === current.warehouseId
  && request.keyword === current.keyword
  && request.pageIndex === current.pageIndex
  && request.pageSize === current.pageSize

export const isCurrentDialogRequest = (
  request: DialogRequestIdentity,
  current: CurrentDialogIdentity
): boolean => current.visible
  && request.generation === current.generation
  && request.orderId === current.orderId

const positive = (value: number | null): value is number => Number.isFinite(Number(value)) && Number(value) > 0

export const isBoxMeasurementComplete = (box: WeighingBox): boolean =>
  positive(box.weight) && positive(box.length) && positive(box.width) && positive(box.height)

export const isTaskMeasurementComplete = (boxes: WeighingBox[]): boolean =>
  boxes.length > 0 && boxes.every(isBoxMeasurementComplete)

export const getMeasurementCapabilityError = (boxes: WeighingBox[]): string | null => {
  if (boxes.length === 0) return '装箱任务没有可称重的来源箱，已停止称重。'
  if (boxes.some((box) => !box.source_box_identity.trim())) {
    return '来源箱缺少稳定标识，已停止称重。'
  }
  return null
}

export const buildSaveMeasurementCommand = (
  order: DispatchOrderSummary,
  box: WeighingBox,
  requestId: string
): SaveMeasurementCommand => {
  if (!isBoxMeasurementComplete(box)) throw new Error('重量和长宽高必须全部大于 0')
  return {
    orderId: order.id,
    boxId: box.id,
    payload: {
      request_id: requestId,
      row_version: order.row_version,
      box_row_version: box.row_version,
      weight: Number(box.weight),
      length: Number(box.length),
      width: Number(box.width),
      height: Number(box.height)
    }
  }
}

const assertCopyTarget = (source: WeighingBox, target: WeighingBox): void => {
  if (source.id === target.id) throw new Error('目标箱不能与来源箱相同')
  if (source.packing_task_id !== target.packing_task_id) throw new Error('只能复制到同一装箱任务的箱子')
  if (!source.source_box_identity.trim() || !target.source_box_identity.trim()) {
    throw new Error('来源箱或目标箱缺少稳定标识')
  }
  if (!isBoxMeasurementComplete(source)) throw new Error('来源箱的实测数据未填写完整')
}

export const buildCopyMeasurementCommand = (
  order: DispatchOrderSummary,
  source: WeighingBox,
  target: WeighingBox,
  requestId: string
): CopyMeasurementCommand => {
  assertCopyTarget(source, target)
  return {
    orderId: order.id,
    targetBoxId: target.id,
    payload: {
      request_id: requestId,
      row_version: order.row_version,
      source_box_id: source.id,
      target_box_row_version: target.row_version
    }
  }
}

export const applyCopiedMeasurement = (
  boxes: WeighingBox[],
  source: WeighingBox,
  target: WeighingBox
): WeighingBox[] => {
  assertCopyTarget(source, target)
  return boxes.map((box) => box.id === target.id
    ? {
        ...box,
        weight: Number(source.weight),
        length: Number(source.length),
        width: Number(source.width),
        height: Number(source.height),
        measurement_status: 'MEASURED',
        copied_from_box_id: source.id
      }
    : box)
}

export const mergeRefreshedBoxesPreservingDirtyDrafts = (
  currentBoxes: WeighingBox[],
  refreshedBoxes: WeighingBox[],
  dirtyBoxIds: ReadonlySet<number>,
  refreshedBoxId: number
): WeighingBox[] => {
  const currentById = new Map(currentBoxes.map((box) => [box.id, box]))
  return refreshedBoxes.map((box) => {
    const draft = currentById.get(box.id)
    if (!draft || box.id === refreshedBoxId || !dirtyBoxIds.has(box.id)) return { ...box }
    return {
      ...box,
      weight: draft.weight,
      length: draft.length,
      width: draft.width,
      height: draft.height
    }
  })
}

export const buildWeighingSourceDecisionCommand = (
  order: DispatchOrderSummary,
  decision: DispatchSourceDecision,
  reason: string,
  requestId: string
): SourceDecisionRequest => {
  const normalizedReason = reason.trim()
  if (!normalizedReason) throw new Error('处理原因不能为空')
  const pendingSourceVersion = order.pending_source_version.trim()
  if (!pendingSourceVersion) throw new Error('待裁决来源版本不存在，请刷新后重试')
  return {
    decision,
    source_version: pendingSourceVersion,
    reason: normalizedReason,
    request_id: requestId,
    row_version: order.row_version
  }
}
