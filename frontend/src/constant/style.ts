import { pinia } from '@/store'
import { useSystemStore } from '@/store/module/system'

interface pageHasElement {
  hasPager?: boolean
  hasTab?: boolean
  hasOperateBtn?: boolean
  hasToolBar?: boolean
}

export const primaryColor = '#1769E8'
export const primaryLightColor = '#EAF2FF'
export const lightGrey = '#999999'
export const errorColor = '#BA2828'

// The low resolution of the selection box ADAPTS to solve the problem that the scroll bar of the selection box button causes the button to disappear
let select_table_height = 445
if (window?.innerHeight) {
  if (window.innerHeight > 740) {
    select_table_height = 500
  } else {
    const computed_table_height = window.innerHeight - 48 - 64 - 52 - 24 - 74

    if (computed_table_height > 200) {
      select_table_height = computed_table_height
    }
  }
}

export const SYSTEM_HEIGHT = {
  HEADER: 60,
  TAB: 70,
  OPERATE_BAR: 52,
  VXE_PAGER: 48,
  SELECT_TABLE: select_table_height,
  TOOLBAR: 40
}

// The height of the content card
export const computedCardHeight = ({ hasTab = true, hasOperateBtn = true }: pageHasElement) => {
  const clientHeight = useSystemStore(pinia).clientHeight
  const EXTRA_MARGIN = 100

  let res = clientHeight - SYSTEM_HEIGHT.HEADER - EXTRA_MARGIN

  if (hasTab) {
    res -= SYSTEM_HEIGHT.TAB
  }
  if (hasOperateBtn) {
    res -= SYSTEM_HEIGHT.OPERATE_BAR
  }

  return `${ res }px`
}

// The height of the table
export const computedTableHeight = ({ hasPager = true, hasTab = true, hasOperateBtn = true }: pageHasElement) => {
  const clientHeight = useSystemStore(pinia).clientHeight
  const EXTRA_MARGIN = 100

  let res = clientHeight - SYSTEM_HEIGHT.HEADER - EXTRA_MARGIN

  if (hasPager) {
    res -= SYSTEM_HEIGHT.VXE_PAGER
  }
  if (hasTab) {
    res -= SYSTEM_HEIGHT.TAB
  }
  if (hasOperateBtn) {
    res -= SYSTEM_HEIGHT.OPERATE_BAR
  }

  return `${ res }px`
}

// The height of the table container in select modal.
export const computedSelectTableSearchHeight = ({ hasPager = true, hasToolBar = false }: pageHasElement) => {
  let res = SYSTEM_HEIGHT.SELECT_TABLE

  if (hasPager) {
    res += SYSTEM_HEIGHT.VXE_PAGER
  }
  if (hasToolBar) {
    res += SYSTEM_HEIGHT.TOOLBAR
  }

  return `${ res }px`
}
