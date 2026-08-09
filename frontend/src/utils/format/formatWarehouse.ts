import { AreaProperty } from '@/types/Base/Warehouse'
import i18n from '@/languages/i18n'

/**
 * 默认仓库名称：不允许删除，也不允许通过界面/接口修改名称（改名只能直接操作数据库）。
 */
export const DEFAULT_WAREHOUSE_NAME = '有座山深圳仓'

export const formatAreaProperty = (value: number) => {
  switch (value) {
    case AreaProperty.picking_area:
      return i18n.global.t('base.warehouseSetting.picking_area')
    case AreaProperty.stocking_area:
      return i18n.global.t('base.warehouseSetting.stocking_area')
    case AreaProperty.receiving_area:
      return i18n.global.t('base.warehouseSetting.receiving_area')
    case AreaProperty.return_area:
      return i18n.global.t('base.warehouseSetting.return_area')
    case AreaProperty.defective_area:
      return i18n.global.t('base.warehouseSetting.defective_area')
    case AreaProperty.inventory_area:
      return i18n.global.t('base.warehouseSetting.inventory_area')
    default:
      return ''
  }
}
