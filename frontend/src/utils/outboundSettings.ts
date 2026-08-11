export const OUTBOUND_VOLUME_DIVISORS = [5000, 6000, 7000, 8000] as const

type BoxVolume = {
  box_no: string
  weighing_volume: number
}

export const calculateBoxVolumetricWeights = (boxes: BoxVolume[], divisor: number) =>
  boxes.map(box => ({
    box_no: box.box_no,
    volumetric_weight: Number((Number(box.weighing_volume || 0) / divisor).toFixed(2))
  }))
