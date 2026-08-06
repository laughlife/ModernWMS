import { defineConfig } from 'playwright/test'

export default defineConfig({
  testDir: './tests/smoke',
  workers: 2,
  use: {
    baseURL: 'http://localhost:5173',
    channel: process.env.CI ? undefined : 'chrome'
  },
  webServer: {
    command: 'npm run dev -- --host localhost',
    url: 'http://localhost:5173',
    reuseExistingServer: true
  }
})
