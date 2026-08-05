import VxeUITable, { VxeUI } from 'vxe-table'
import VxePCUI from 'vxe-pc-ui'
import XEUtils from 'xe-utils'
import { VXETablePluginExportXLSX } from 'vxe-table-plugin-export-xlsx'
import ExcelJS from 'exceljs'
import zhCN from 'vxe-table/lib/locale/lang/zh-CN'
import 'vxe-pc-ui/lib/style.css'
import 'vxe-table/lib/style.css'

/**
 * Format date.
 * @param {String} format: default value is 'yyyy-MM-dd HH:mm:ss'
 */
VxeUI.formats.add('formatDate', {
  cellFormatMethod({ cellValue }, format) {
    const date = new Date(cellValue)
    if (!cellValue || !date || XEUtils.toDateString(date, 'yyyy-MM-dd') === '1900-01-01' || XEUtils.toDateString(date, 'yyyy-MM-dd') === '1000-01-01') {
      return ''
    }
    return XEUtils.toDateString(date, format || 'yyyy-MM-dd HH:mm:ss')
  }
})

VxeUI.setI18n('zh-CN', zhCN)

export function installVxeExportPlugin() {
  VxeUI.use(VXETablePluginExportXLSX, { ExcelJS })
  VxeUI.setConfig({
    table: {
      importConfig: { _typeMaps: { xlsx: 1 } },
      exportConfig: { _typeMaps: { xlsx: 1 } }
    }
  })
}

export function setVxeLanguage() {
  VxeUI.setLanguage('zh-CN')
}

export { VxeUI, VxePCUI, VxeUITable }
