export const parseFeatureFlag = (value: unknown): boolean => String(value).toLowerCase() === 'true'

export const PACKING_TASK_FIRST_STEP_ENABLED = parseFeatureFlag(
  import.meta.env.VITE_PACKING_TASK_FIRST_STEP_ENABLED
)

export const loadPackingTaskFirstStep = <T>(
  enabled: boolean,
  packingTaskLoader: () => T,
  fbaShipmentLoader: () => T
): T => enabled ? packingTaskLoader() : fbaShipmentLoader()
