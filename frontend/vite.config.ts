import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'
import { fileURLToPath, URL } from 'node:url'
import vuetify from 'vite-plugin-vuetify'

// https://vitejs.dev/config/
export default defineConfig({
  plugins: [
    vue(),
    vuetify({ autoImport: true })
  ],
  resolve: {
    alias: {
      // 配置别名指向src目录
      '@': fileURLToPath(new URL('./src', import.meta.url))
    },
    // 使用别名的文件后缀
    extensions: ['.js', '.json', '.ts']
  },
  css: {
    preprocessorOptions: {
      less: {
        javascriptEnabled: true
      }
    }
  },
  // ...其他配置项
  optimizeDeps: {
    // Vuetify components are auto-imported from lazy-loaded pages. Pre-bundle
    // every deep component import up front so Vite does not invalidate the
    // dependency hash while the user is navigating between menus.
    include: ['jquery', 'vuetify/components/**']
  }
})
