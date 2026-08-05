import { hookComponent } from '@/components/system'
import i18n from '@/languages/i18n'
/**
 * Export table
 * Default type is 'xlsx'
 */
export const exportData = ({ table, filename, columnFilterMethod, mode = 'all' }: IExportTable): void => {
  const showExportError = () => {
    hookComponent.$message({
      type: 'error',
      content: `${ i18n.global.t('system.page.export') }${ i18n.global.t('system.tips.fail') }`
    })
  }

  if (!table?.exportData) {
    showExportError()
    return
  }

  void table.exportData({
    type: 'xlsx',
    filename,
    mode: 'current',
    isHeader: true,
    data: mode === 'header' ? [] : undefined,
    columnFilterMethod
  }).catch(showExportError)
}

interface IExportTable {
  table: any
  filename?: string
  exceptIndex?: Array<number>
  mode?: 'all' | 'header'
  columnFilterMethod?: FilterMEthod
}

type FilterMEthod = ({ column, row }: any) => boolean
