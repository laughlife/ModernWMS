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
  ['customer', 'baseModule', 'base/customer'],
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
  tenant_id: 1,
  menu_actions: ['read', 'save', 'import', 'export', 'resetPwd', 'stock-export']
}))

async function mockBackend(page: Page) {
  await page.route('http://127.0.0.1:21011/**', async (route) => {
    const path = new URL(route.request().url()).pathname
    const data = path.endsWith('/login')
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

    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({ isSuccess: true, data, errorMessage: '' })
    })
  })
}

test('critical pages remain navigable', async ({ page }) => {
  const pageErrors: string[] = []
  page.on('pageerror', (error) => pageErrors.push(error.message))
  await mockBackend(page)

  const baselineLabel = process.env.VISUAL_BASELINE_LABEL
  const baselineDir = baselineLabel
    ? resolve('visual-baselines', baselineLabel)
    : undefined
  if (baselineDir) mkdirSync(baselineDir, { recursive: true })

  await page.goto('/#/login')
  await expect(page.locator('input[type="text"]')).toHaveCount(1)
  if (baselineDir) await page.screenshot({ path: resolve(baselineDir, '01-login.png'), fullPage: true })

  await page.locator('.loginBtn').click()
  await expect(page).toHaveURL(/#\/homepage$/)

  const pages = [
    ['02-homepage', 'homepage', 'Home Page'],
    ['03-user', 'userManagement', 'User Management'],
    ['04-warehouse', 'warehouseSetting', 'Warehouse Setting'],
    ['05-sku', 'commodityManagement', 'Commodity Management'],
    ['06-asn', 'stockAsn', 'Receiving Management'],
    ['07-outbound', 'deliveryManagement', 'Delivery Management'],
    ['08-stock', 'stockManagement', 'Stock Management'],
    ['09-print-template', 'print', 'Print Settings']
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
