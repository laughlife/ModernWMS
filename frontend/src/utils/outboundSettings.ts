export const OUTBOUND_VOLUME_DIVISORS = [5000, 6000, 7000, 8000] as const

type BoxVolume = {
  box_no: string
  weighing_volume: number
}

export const calculateBoxVolumetricWeights = (boxes: BoxVolume[], divisor: number) =>
  boxes.map(box => {
    const sourceVolume = Number(box.weighing_volume || 0)
    const volumeCm3 = Number.isFinite(sourceVolume) && sourceVolume > 0 ? sourceVolume : 0
    return {
      box_no: box.box_no,
      volume_cm3: volumeCm3,
      volumetric_weight: Number((volumeCm3 / divisor).toFixed(2))
    }
  })

type BoxVolumetricResult = ReturnType<typeof calculateBoxVolumetricWeights>[number]

export const formatBoxVolumetricFormula = (box: BoxVolumetricResult, divisor: number) =>
  `${box.volume_cm3.toFixed(2)} cm³ ÷ ${divisor} = ${box.volumetric_weight.toFixed(2)}`
