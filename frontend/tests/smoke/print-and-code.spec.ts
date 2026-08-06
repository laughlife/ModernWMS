import { expect, test, type Page } from 'playwright/test'

test.describe.configure({ timeout: 60_000 })

const menus = [
  ['commodityManagement', 'baseModule', 'base/commodityManagement'],
  ['print', 'baseModule', 'base/print'],
  ['largeScreen', '', 'largeScreen/largeScreen']
].map(([vue_path, module, vue_directory], index) => ({
  id: index + 80,
  menu_name: vue_path,
  module,
  vue_path,
  vue_path_detail: '',
  vue_directory,
  sort: index + 1,
  tenant_id: 1,
  menu_actions: ['read', 'save', 'export', 'printQrCode', 'printBarCode', 'print']
}))

async function mockBackend(page: Page) {
  await page.route('http://127.0.0.1:21011/**', async (route) => {
    const path = new URL(route.request().url()).pathname
    let data: unknown = { rows: [], totals: 0 }

    if (path.endsWith('/login')) {
      data = {
        access_token: 'print-test-token',
        refresh_token: 'print-test-refresh-token',
        expire: 60,
        userrole_id: 1,
        user_name: 'admin'
      }
    } else if (path.endsWith('/rolemenu/authority')) {
      data = menus
    } else if (path.endsWith('/PrintSolution/list')) {
      data = {
        rows: [{ id: 1, vue_path: 'commodityManagement', tab_page: 'print_page_main', solution_name: 'SKU label', config_json: '{}', report_length: 100, report_width: 100, report_direction: 'st' }],
        totals: 1
      }
    } else if (path.endsWith('/spu/list')) {
      data = {
        rows: [{
          id: 1,
          spu_code: 'SPU-001',
          spu_name: 'Demo Product',
          length_unit: 1,
          volume_unit: 0,
          weight_unit: 1,
          detailList: [{ id: 201, sku_code: 'SKU-001', sku_name: 'Blue', unit: 'piece', bar_code: '6901234567890', weight: 1, lenght: 1, width: 1, height: 1, volume: 1, cost: 1, price: 2 }]
        }],
        totals: 1
      }
    } else if (path.endsWith('/all') || path.endsWith('/select-item')) {
      data = []
    }

    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({ isSuccess: true, data, errorMessage: '' })
    })
  })
}

async function login(page: Page) {
  await page.goto('/#/login')
  await page.waitForLoadState('networkidle')
  await page.locator('.loginBtn').click()
  await expect(page).toHaveURL(/#\/homepage$/, { timeout: 10_000 })
}

test('print templates, QR codes and barcodes render through upgraded plugins', async ({ page }) => {
  const pageErrors: string[] = []
  page.on('pageerror', (error) => pageErrors.push(error.message))
  await mockBackend(page)
  await login(page)

  await page.evaluate(() => { window.location.hash = '#/print' })
  await expect(page.locator('.v-breadcrumbs')).toContainText('打印设置')
  await expect(page.locator('.vxe-table')).toContainText('SKU label')

  await page.evaluate(() => { window.location.hash = '#/commodityManagement' })
  await expect(page.locator('.vxe-table')).toContainText('Demo Product')
  await page.locator('.vxe-header--column .mdi-menu-right:visible').click()
  const skuRows = page.locator('.vxe-body--row').filter({ hasText: 'SKU-001' })
  await expect(skuRows.first()).toBeVisible()
  await skuRows.locator('.vxe-cell--checkbox:visible').click()

  await page.locator('.btn-group .mdi-qrcode').click()
  const qrDialog = page.locator('.v-dialog').filter({ hasText: '预览' })
  await expect(qrDialog.locator('#printArea .code-container')).toBeVisible()
  await expect(qrDialog.locator('#printArea canvas, #printArea img')).toHaveCount(1)
  await page.waitForTimeout(300)
  await qrDialog.getByRole('button', { name: '关闭' }).click()

  await page.locator('.btn-group .mdi-barcode').click()
  const barcodeDialog = page.locator('.v-dialog').filter({ hasText: '预览' })
  await expect(barcodeDialog.locator('#printBarCode201')).toBeVisible()
  await expect(barcodeDialog.locator('#printBarCode201 rect, #printBarCode201 path')).not.toHaveCount(0)
  await barcodeDialog.getByRole('button', { name: '关闭' }).click()

  expect(pageErrors).toEqual([])
})

test('ECharts 6 renders the warehouse large screen', async ({ page }) => {
  const pageErrors: string[] = []
  page.on('pageerror', (error) => pageErrors.push(error.message))
  await mockBackend(page)
  await login(page)

  await page.evaluate(() => { window.location.hash = '#/largeScreen' })
  await expect(page.locator('#container')).toHaveAttribute('data-screen', 'false')
  await expect(page.locator('#container .mask')).toBeHidden({ timeout: 10_000 })
  await expect(page.locator('#container .chat')).toHaveCount(3)
  await expect(page.locator('#container .chat canvas')).toHaveCount(3)

  expect(pageErrors).toEqual([])
})
