import { describe, expect, it } from 'vitest'
import { calculateBoxVolumetricWeights, formatBoxVolumetricFormula, OUTBOUND_VOLUME_DIVISORS } from './outboundSettings'

describe('outbound volumetric settings', () => {
  it('offers only the four supported volumetric divisors', () => {
    expect(OUTBOUND_VOLUME_DIVISORS).toEqual([5000, 6000, 7000, 8000])
  })

  it('calculates every box volumetric weight and keeps two decimals', () => {
    expect(calculateBoxVolumetricWeights([
      { box_no: 'BOX-1', weighing_volume: 6172.8 },
      { box_no: 'BOX-2', weighing_volume: 10000 }
    ], 5000)).toEqual([
      { box_no: 'BOX-1', volume_cm3: 6172.8, volumetric_weight: 1.23 },
      { box_no: 'BOX-2', volume_cm3: 10000, volumetric_weight: 2 }
    ])
  })

  it('formats the per-box formula without a weight unit', () => {
    expect(formatBoxVolumetricFormula({
      box_no: 'BOX-1',
      volume_cm3: 6172.8,
      volumetric_weight: 1.23
    }, 5000)).toBe('6172.80 cm³ ÷ 5000 = 1.23')
  })
})
