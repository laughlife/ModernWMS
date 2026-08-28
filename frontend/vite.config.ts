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
  server: {
    // 允许通过本机 IP 直接访问
    host: true,
    // ERP 已使用 80 端口，WMS 开发服务固定使用 81 端口
    port: 81,
    // 端口被占用时直接报错，不自动回退到其他端口
    strictPort: true,
    watch: {
      // Windows 下原生文件事件可能因编辑器的原子替换而漏报；
      // 轮询能让 Vite 在不重启进程的情况下稳定触发 HMR。
      usePolling: true,
      interval: 500
    }
  },
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
