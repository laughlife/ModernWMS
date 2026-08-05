import { expect, test } from 'playwright/test'

test('login page renders account and password inputs', async ({ page }) => {
  await page.goto('/#/login')

  await expect(page.locator('input[type="text"]')).toHaveCount(1)
  await expect(page.locator('input[type="password"]')).toHaveCount(1)
})

test('login page presents the approved logistics layout', async ({ page }) => {
  await page.goto('/#/login')

  await expect(page.getByRole('heading', { name: '南阳有座山物流管理系统' })).toBeVisible()
  await expect(page.getByRole('heading', { name: '欢迎登录' })).toBeVisible()
  await expect(page.getByTestId('login-hero-image')).toBeVisible()
  await expect(page.getByTestId('login-submit')).toBeVisible()
  await expect(page.getByText('SSO单点登录')).toHaveCount(0)
})
