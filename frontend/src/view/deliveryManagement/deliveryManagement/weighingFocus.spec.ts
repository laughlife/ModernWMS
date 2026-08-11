import { describe, expect, it } from 'vitest'
import { getNextWeighingField } from './weighingFocus'

describe('FBA box weighing focus order', () => {
  it('moves through weight, length, width and height within one box', () => {
    expect(getNextWeighingField(0, 'weight', 3)).toEqual({ rowIndex: 0, field: 'length' })
    expect(getNextWeighingField(0, 'length', 3)).toEqual({ rowIndex: 0, field: 'width' })
    expect(getNextWeighingField(0, 'width', 3)).toEqual({ rowIndex: 0, field: 'height' })
  })

  it('moves from one box height to the next box weight', () => {
    expect(getNextWeighingField(0, 'height', 3)).toEqual({ rowIndex: 1, field: 'weight' })
    expect(getNextWeighingField(1, 'height', 3)).toEqual({ rowIndex: 2, field: 'weight' })
  })

  it('stops after the final box height', () => {
    expect(getNextWeighingField(2, 'height', 3)).toBeNull()
  })
})
