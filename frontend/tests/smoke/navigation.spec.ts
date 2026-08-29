import { mkdirSync } from 'node:fs'
import { resolve } from 'node:path'
import { expect, test, type Page } from 'playwright/test'

const menus = [
  ['companySetting', 'baseModule', 'base/companySetting'],
  ['userRoleSetting', 'baseModule', 'base/userRoleSetting'],
  ['roleMenu', 'baseModule', 'base/roleMenu'],
  ['userManagement', 'baseModule', 'base/userManagement'],
  ['commodityCategorySetting', 'baseModule', 'base/commodityCategorySetting'],
  ['commodityManagement', 'baseModule', 'base/commodityManagement'],
  ['supplier', 'baseModule', 'base/supplier'],
  ['warehouseSetting', 'baseModule', 'base/warehouseSetting'],
  ['ownerOfCargo', 'baseModule', 'base/ownerOfCargo'],
  ['freightSetting', 'baseModule', 'base/freightSetting'],
  ['print', 'baseModule', 'base/print'],
  ['stockManagement', '', 'wms/stockManagement'],
  ['warehouseProcessing', 'warehouseWorkingModule', 'warehouseWorking/warehouseProcessing'],
  ['warehouseMove', 'warehouseWorkingModule', 'warehouseWorking/warehouseMove'],
  ['warehouseFreeze', 'warehouseWorkingModule', 'warehouseWorking/warehouseFreeze'],
  ['warehouseAdjust', 'warehouseWorkingModule', 'warehouseWorking/warehouseAdjust'],
  ['warehouseTaking', 'warehouseWorkingModule', 'warehouseWorking/warehouseTaking'],
  ['stockAsn', '', 'wms/stockAsn'],
  ['deliveryManagement', 'deliveryManagement', 'deliveryManagement/deliveryManagement']
].map(([vue_path, module, vue_directory], index) => ({
  id: index + 20,
  menu_name: vue_path,
  module,
  vue_path,
  vue_path_detail: '',
  vue_directory,
  sort: index + 1,
  menu_actions: ['read', 'save', 'import', 'export', 'resetPwd', 'stock-export']
}))

async function mockBackend(page: Page) {
  await page.route('http://127.0.0.1:21011/**', async (route) => {
    const path = new URL(route.request().url()).pathname
    let data: unknown = path.endsWith('/login')
      ? {
          access_token: 'visual-test-token',
          refresh_token: 'visual-test-refresh-token',
          expire: 60,
          userrole_id: 1,
          user_name: 'admin'
        }
      : path.endsWith('/rolemenu/authority')
        ? menus
        : { rows: [], totals: 0 }
    if (path.endsWith('/warehouse/access-options')) {
      data = { warehouses: [{ id: 320118, name: '深圳自建仓' }], default_warehouse_id: 320118 }
    } else if (path.endsWith('/all') || path.endsWith('/select-item') || path.endsWith('-options')) {
      data = []
    }

    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({ isSuccess: true, code: 200, data, errorMessage: '' })
    })
  })
}

test('sidebar menu click navigates to the selected page', async ({ page }) => {
  const pageErrors: string[] = []
  page.on('pageerror', (error) => pageErrors.push(error.message))
  await mockBackend(page)

  await page.goto('/#/login')
  await page.locator('input[type="text"]').fill('admin')
  await page.locator('input[type="password"]').fill('test-password')
  await page.locator('.loginBtn').click()
  await expect(page).toHaveURL(/#\/homepage$/)

  await page.getByText('基础设置', { exact: true }).click()
  await page.getByText('用户管理', { exact: true }).click()

  await expect(page).toHaveURL(/#\/userManagement$/)
  await expect(page.locator('.v-breadcrumbs')).toContainText('用户管理')
  await expect(page.locator('.sideBarMenus .menuItems').filter({ hasText: '用户管理' })).toHaveClass(/activeMenuItems/)
  expect(pageErrors).toEqual([])
})

test('critical pages remain navigable', async ({ page }) => {
  const pageErrors: string[] = []
  page.on('pageerror', (error) => pageErrors.push(error.message))
  await mockBackend(page)
  await page.addInitScript(() => {
    localStorage.setItem('modernwms:system', JSON.stringify({ language: 'en' }))
    localStorage.setItem('language', 'en')
  })

  const baselineLabel = process.env.VISUAL_BASELINE_LABEL
  const baselineDir = baselineLabel
    ? resolve('visual-baselines', baselineLabel)
    : undefined
  if (baselineDir) mkdirSync(baselineDir, { recursive: true })

  await page.goto('/#/login')
  await expect(page.locator('html')).toHaveAttribute('lang', 'zh-CN')
  await expect(page.locator('input[type="text"]')).toHaveCount(1)
  await expect(page.locator('.titleText')).toContainText('欢迎登录')
  await expect(page.locator('.languageIcon')).toHaveCount(0)
  if (baselineDir) await page.screenshot({ path: resolve(baselineDir, '01-login.png'), fullPage: true })

  await page.locator('input[type="text"]').fill('admin')
  await page.locator('input[type="password"]').fill('test-password')
  const loginResponse = page.waitForResponse((response) => new URL(response.url()).pathname === '/login')
  await page.locator('.loginBtn').click()
  await loginResponse
  await expect(page).toHaveURL(/#\/homepage$/)
  await expect(page.locator('.warehouseImage')).toBeVisible()
  await expect(page.locator('.mainTitle')).toHaveCSS('color', 'rgb(23, 105, 232)')
  await expect(page.locator('.languageIcon')).toHaveCount(0)
  await expect(page.getByAltText('Gitee')).toHaveCount(0)
  await expect(page.getByAltText('API')).toHaveCount(0)
  await expect(page.locator('a[href*="gitee.com"], a[href*="github.com"], a[href*="apifox.com"]')).toHaveCount(0)

  const pages = [
    ['02-homepage', 'homepage', '首页'],
    ['03-user', 'userManagement', '用户管理'],
    ['04-warehouse', 'warehouseSetting', '仓库设置'],
    ['05-sku', 'commodityManagement', '商品管理'],
    ['06-asn', 'stockAsn', '收货管理'],
    ['07-outbound', 'deliveryManagement', '发货管理'],
    ['08-stock', 'stockManagement', '库存管理'],
    ['09-print-template', 'print', '打印设置']
  ] as const

  for (const [name, routeName, breadcrumb] of pages) {
    await page.evaluate((nextRoute) => {
      window.location.hash = `#/${nextRoute}`
    }, routeName)
    await expect(page).toHaveURL(new RegExp(`#/${routeName}$`))
    await expect(page.locator('.v-breadcrumbs')).toContainText(breadcrumb)
    if (baselineDir) await page.screenshot({ path: resolve(baselineDir, `${name}.png`), fullPage: true })
  }

  expect(pageErrors).toEqual([])
})
