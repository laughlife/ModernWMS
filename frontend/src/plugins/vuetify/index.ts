// Vuetify
import 'vuetify/styles'
import { createVuetify } from 'vuetify'
import { aliases, mdi } from 'vuetify/iconsets/mdi'
// Translations provided by Vuetify
import { zhHans, zhHant, en } from 'vuetify/locale'
import { getSelcectedLangForVuetify } from './method/index'
import { loadPersistedState } from '@/store/persistence'
import type { StateProps } from '@/types/System/Store'

import 'material-design-icons-iconfont/dist/material-design-icons.css'
import '@mdi/font/css/materialdesignicons.css'

const vuetify = createVuetify({
  locale: {
    locale: getStorageLang(),
    messages: { zhHans, zhHant, en }
  },
  icons: {
    defaultSet: 'mdi',
    aliases,
    sets: {
      mdi
    }
  },
  theme: {
    defaultTheme: 'light'
  },
  display: {
    mobileBreakpoint: 'lg',
    thresholds: {
      xs: 0,
      sm: 600,
      md: 960,
      lg: 1280,
      xl: 1920,
      xxl: 2560
    }
  }
})

// get language in storage or default
function getStorageLang() {
  const lang = loadPersistedState<StateProps>('system')?.language ?? localStorage.getItem('language')
  if (lang) {
    return getSelcectedLangForVuetify(lang)
  }
  return 'en'
}

export { vuetify }
