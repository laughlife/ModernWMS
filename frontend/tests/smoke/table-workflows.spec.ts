import { expect, test, type Page } from 'playwright/test'
import * as XLSX from 'xlsx'

test.describe.configure({ timeout: 60_000 })

const menus = [
  ['userManagement', 'baseModule', 'base/userManagement'],
  ['commodityManagement', 'baseModule', 'base/commodityManagement']
].map(([vue_path, module, vue_directory], index) => ({
  id: index + 20,
  menu_name: vue_path,
  module,
  vue_path,
  vue_path_detail: '',
  vue_directory,
  sort: index + 1,
  menu_actions: ['read', 'save', 'import', 'export', 'resetPwd']
}))

async function mockBackend(
  page: Page,
  userRequests: Array<Record<string, unknown>>,
  commodityRequests: Array<Record<string, unknown>> = []
) {
  await page.route('http://127.0.0.1:21011/**', async (route) => {
    const request = route.request()
    const path = new URL(request.url()).pathname
    let data: unknown = { rows: [], totals: 0 }

    if (path.endsWith('/login')) {
      data = {
        access_token: 'table-test-token',
        refresh_token: 'table-test-refresh-token',
        expire: 60,
        userrole_id: 1,
        user_name: 'admin'
      }
    } else if (path.endsWith('/rolemenu/authority')) {
      data = menus
    } else if (path.endsWith('/user/list')) {
      userRequests.push(request.postDataJSON())
      data = {
        rows: [{ id: 1, user_num: 'U001', user_name: 'Administrator', user_role: 'Admin', sex: 'male', contact_tel: '13800000000', is_valid: true }],
        totals: 40
      }
    } else if (path.endsWith('/spu/catalog')) {
      commodityRequests.push(request.postDataJSON())
      data = {
        rows: [{
          sku_id: 201,
          sku_code: 'SKU-001',
          sku_name: 'Demo Product',
          product_image: 'data:image/svg+xml,<svg xmlns="http://www.w3.org/2000/svg" width="64" height="64"></svg>',
          volume_cm3: 24,
          total_qty: 600,
          cost_batches: [
            { batch_date: '2026-08-12T10:00:00', purchaser_name: '王五', unit_cost: 10, quantity: 500 },
            { batch_date: '2026-08-13T11:00:00', purchaser_name: '赵六', unit_cost: 9, quantity: 100 }
          ],
          total_value: 5900,
          ownerships: [
            { dept_name: '北美一组', order_user_name: '张三' },
            { dept_name: '欧洲二组', order_user_name: '李四' }
          ]
        }],
        totals: 1
      }
    } else if (path.endsWith('/select-item') || path.endsWith('/all')) {
      data = []
    }

    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({ isSuccess: true, code: 200, data, errorMessage: '' })
    })
  })
}

async function login(page: Page) {
  await page.goto('/#/login')
  await page.locator('input[type="text"]').fill('admin')
  await page.locator('input[type="password"]').fill('test-password')
  await page.locator('.loginBtn').click()
  await expect(page).toHaveURL(/#\/homepage$/)
}

test('VXE table pagination, filtering, export and import preview remain usable', async ({ page }) => {
  const pageErrors: string[] = []
  const userRequests: Array<Record<string, unknown>> = []
  page.on('pageerror', (error) => pageErrors.push(error.message))
  await mockBackend(page, userRequests)
  await login(page)

  await page.evaluate(() => { window.location.hash = '#/userManagement' })
  await expect(page.locator('.vxe-table')).toContainText('Administrator')

  await page.getByRole('textbox', { name: '用户名', exact: true }).fill('U001')
  await expect.poll(() => userRequests.some((body) => JSON.stringify(body).includes('U001'))).toBe(true)

  await page.locator('.v-pagination .v-btn').filter({ hasText: '2' }).click()
  await expect.poll(() => userRequests.some((body) => body.pageIndex === 2)).toBe(true)

  const downloadPromise = page.waitForEvent('download')
  await page.locator('.mdi-export-variant').first().click()
  const download = await downloadPromise
  expect(download.suggestedFilename()).toMatch(/\.xlsx$/)

  await page.locator('.mdi-database-import-outline').click()
  const importDialog = page.locator('.v-dialog')
  await expect(importDialog.locator('.vxe-table')).toBeVisible()

  const workbook = XLSX.utils.book_new()
  XLSX.utils.book_append_sheet(workbook, XLSX.utils.json_to_sheet([{
    '用户名': 'U002',
    '员工名称': 'Imported User',
    '联系方式': '13900000000',
    '用户角色': 'Operator',
    '性别': '男'
  }]), 'Users')
  const content = XLSX.write(workbook, { bookType: 'xlsx', type: 'buffer' })
  await importDialog.locator('input[type="file"]').setInputFiles({ name: 'users.xlsx', mimeType: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet', buffer: content })
  await expect(importDialog.locator('.vxe-table')).toContainText('Imported User')

  expect(pageErrors).toEqual([])
})

test('commodity management shows the read-only product catalog', async ({ page }) => {
  const pageErrors: string[] = []
  const commodityRequests: Array<Record<string, unknown>> = []
  page.on('pageerror', (error) => pageErrors.push(error.message))
  await mockBackend(page, [], commodityRequests)
  await login(page)

  await page.evaluate(() => { window.location.hash = '#/commodityManagement' })
  await expect(page.locator('.v-breadcrumbs')).toContainText('商品管理')
  const table = page.locator('.vxe-table')
  await expect(table).toContainText('商品图片')
  await expect(table).toContainText('商品信息')
  await expect(table).toContainText('商品体积(cm³)')
  await expect(table).toContainText('商品总数')
  await expect(table).toContainText('商品成本')
  await expect(table).toContainText('商品所属')
  await expect(table).toContainText('Demo Product')
  await expect(table).toContainText('SKU-001')
  await expect(table).toContainText('24 cm³')
  await expect(table).toContainText('600')
  await expect(table).toContainText('08-12（王五）')
  await expect(table).toContainText('¥10.00 × 500')
  await expect(table).toContainText('08-13（赵六）')
  await expect(table).toContainText('¥9.00 × 100')
  await expect(table).toContainText('总价值：¥5900.00')
  await expect(table.locator('.costList')).not.toContainText('采购人')
  const ownershipList = table.locator('.ownershipList')
  await expect(ownershipList).toContainText('北美一组 | 张三')
  await expect(ownershipList).toContainText('欧洲二组 | 李四')
  await expect(ownershipList).not.toContainText('所属小组')
  await expect(ownershipList).not.toContainText('所属人')
  await expect(page.getByRole('button', { name: '查看大图：Demo Product' })).toBeVisible()
  await expect.poll(() => commodityRequests.length).toBeGreaterThan(0)

  await expect(page.locator('.mdi-plus')).toHaveCount(0)
  await expect(page.locator('.mdi-pencil-outline')).toHaveCount(0)
  await expect(page.locator('.mdi-delete-outline')).toHaveCount(0)
  await expect(page.locator('.mdi-alarm-light')).toHaveCount(0)

  expect(pageErrors).toEqual([])
})
