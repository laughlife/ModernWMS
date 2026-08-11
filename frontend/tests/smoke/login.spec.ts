import { expect, test } from 'playwright/test'

test('login page renders account and password inputs', async ({ page }) => {
  await page.goto('/#/login')

  const accountInput = page.locator('input[type="text"]')
  const passwordInput = page.locator('input[type="password"]')

  await expect(accountInput).toHaveCount(1)
  await expect(passwordInput).toHaveCount(1)
  await expect(accountInput).toHaveValue('')
  await expect(passwordInput).toHaveValue('')
})

test('login page presents the approved logistics layout', async ({ page }) => {
  await page.goto('/#/login')

  await expect(page.getByRole('heading', { name: '南阳有座山物流管理系统' })).toBeVisible()
  await expect(page.getByRole('heading', { name: '欢迎登录' })).toBeVisible()
  await expect(page.getByTestId('login-hero-image')).toBeVisible()
  await expect(page.getByTestId('login-submit')).toBeVisible()
  await expect(page.getByText('SSO单点登录')).toHaveCount(0)

  const footer = page.locator('.login-footer')
  await expect(footer).toHaveCSS('color', 'rgb(23, 105, 232)')
  await expect(footer.locator('.login-footer-registration')).toHaveCSS('color', 'rgb(23, 105, 232)')
  await expect(footer.locator('.registration-icon')).toHaveCount(2)
  await expect(footer.locator('.registration-icon').first()).toHaveAttribute('src', /china.*\.png/)
})
