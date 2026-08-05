import { createApp } from 'vue'
import './style.css' // Global Styles
import print from 'vue3-print-nb'
import { setup } from 'yk-vue-plugin-hiprint'
import DataVVue3 from '@kjgl77/datav-vue3'
import { installVxeExportPlugin, setVxeLanguage, VxePCUI, VxeUITable } from '@/plugins/VXETable/index'
import { vuetify } from '@/plugins/vuetify/index'
import i18n from './languages/i18n'
import App from './App.vue'
import '@/assets/fonts/iconfont.css'

// import router
import { router } from './router'
import { pinia } from './store/index'
import hookComponent from '@/components/system/index'

import VxeDateColumn from '@/components/table/vxe-date-column.vue'

const app = createApp(App)
app.config.globalProperties.hiprint = setup()

setVxeLanguage(i18n.global.locale.value)

app.use(print)
app.use(pinia)
app.use(router)
app.use(vuetify)
app.use(i18n)
app.use(hookComponent)
app.use(VxePCUI)
app.use(VxeUITable)
installVxeExportPlugin()
app.use(DataVVue3)

// 自定义组件挂载
app.component('VxeDateColumn', VxeDateColumn)

app.mount('#app')
