export type WeighingField = 'weight' | 'length' | 'width' | 'height'

export type WeighingFocusTarget = {
  rowIndex: number
  field: WeighingField
}

export const getNextWeighingField = (
  rowIndex: number,
  field: WeighingField,
  rowCount: number
): WeighingFocusTarget | null => {
  const fieldOrder: WeighingField[] = ['weight', 'length', 'width', 'height']
  const fieldIndex = fieldOrder.indexOf(field)

  if (fieldIndex < fieldOrder.length - 1) {
    return { rowIndex, field: fieldOrder[fieldIndex + 1] }
  }
  if (rowIndex < rowCount - 1) {
    return { rowIndex: rowIndex + 1, field: 'weight' }
  }
  return null
}
