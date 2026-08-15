import { describe, expect, it, vi } from 'vitest'
import { loadPackingTaskFirstStep, parseFeatureFlag } from './packingTaskFeature'

describe('packing-task feature flag', () => {
  it('is enabled only by an explicit true value', () => {
    expect(parseFeatureFlag('true')).toBe(true)
    expect(parseFeatureFlag('TRUE')).toBe(true)
    expect(parseFeatureFlag(undefined)).toBe(false)
    expect(parseFeatureFlag('false')).toBe(false)
    expect(parseFeatureFlag('1')).toBe(false)
  })

  it('calls only the API selected by the flag', () => {
    const packingTaskLoader = vi.fn(() => 'packing')
    const fbaShipmentLoader = vi.fn(() => 'fba')

    expect(loadPackingTaskFirstStep(false, packingTaskLoader, fbaShipmentLoader)).toBe('fba')
    expect(packingTaskLoader).not.toHaveBeenCalled()
    expect(fbaShipmentLoader).toHaveBeenCalledOnce()

    expect(loadPackingTaskFirstStep(true, packingTaskLoader, fbaShipmentLoader)).toBe('packing')
    expect(packingTaskLoader).toHaveBeenCalledOnce()
  })
})
