import { createPinia } from 'pinia'
import { persistStorePlugin } from './persistence'

export const pinia = createPinia()
pinia.use(persistStorePlugin)
