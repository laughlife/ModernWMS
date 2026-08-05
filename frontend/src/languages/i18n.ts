import { createI18n } from 'vue-i18n'
import zhCN from 'vxe-table/lib/locale/lang/zh-CN'
import cn from './langsJson/cn.json'

const i18n = createI18n({
  legacy: false,
  globalInjection: true,
  locale: 'zh_CN',
  fallbackLocale: 'zh_CN',
  messages: {
    zh_CN: { ...cn, ...zhCN }
  }
})

export default i18n
