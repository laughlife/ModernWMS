import { expect, test } from 'playwright/test'

test('login page renders account and password inputs', async ({ page }) => {
  await page.goto('/#/login')

  await expect(page.locator('input[type="text"]')).toHaveCount(1)
  await expect(page.locator('input[type="password"]')).toHaveCount(1)
})
