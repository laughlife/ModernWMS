import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'
import { fileURLToPath, URL } from 'node:url'

// https://vitejs.dev/config/
export default defineConfig({
  plugins: [
    vue()
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
    include: ['jquery']
  }
})
