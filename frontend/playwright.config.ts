import { defineConfig } from 'playwright/test'

const port = Number(process.env.PLAYWRIGHT_PORT ?? 4173)
const baseURL = `http://127.0.0.1:${port}`

export default defineConfig({
  testDir: './tests/smoke',
  workers: 2,
  use: {
    baseURL,
    channel: process.env.CI ? undefined : 'chrome'
  },
  webServer: {
    command: `npm run dev -- --host 127.0.0.1 --port ${port} --strictPort`,
    url: baseURL,
    reuseExistingServer: false,
    env: {
      ...process.env,
      VITE_BASE_PATH: 'http://127.0.0.1',
      VITE_SERVER_PORT: '21011'
    }
  }
})
