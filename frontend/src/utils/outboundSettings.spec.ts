import { describe, expect, it } from 'vitest'
import { calculateBoxVolumetricWeights, OUTBOUND_VOLUME_DIVISORS } from './outboundSettings'

describe('outbound volumetric settings', () => {
  it('offers only the four supported volumetric divisors', () => {
    expect(OUTBOUND_VOLUME_DIVISORS).toEqual([5000, 6000, 7000, 8000])
  })

  it('calculates every box volumetric weight and keeps two decimals', () => {
    expect(calculateBoxVolumetricWeights([
      { box_no: 'BOX-1', weighing_volume: 6172.8 },
      { box_no: 'BOX-2', weighing_volume: 10000 }
    ], 5000)).toEqual([
      { box_no: 'BOX-1', volumetric_weight: 1.23 },
      { box_no: 'BOX-2', volumetric_weight: 2 }
    ])
  })
})
