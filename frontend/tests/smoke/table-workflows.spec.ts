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
  tenant_id: 1,
  menu_actions: ['read', 'save', 'import', 'export', 'resetPwd']
}))

async function mockBackend(page: Page, userRequests: Array<Record<string, unknown>>) {
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
    } else if (path.endsWith('/select-item') || path.endsWith('/all')) {
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

  await page.getByRole('textbox', { name: 'User Num', exact: true }).fill('U001')
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
    'User Num': 'U002',
    'User Name': 'Imported User',
    'Contact Tel': '13900000000',
    'User Role': 'Operator',
    Sex: 'Male'
  }]), 'Users')
  const content = XLSX.write(workbook, { bookType: 'xlsx', type: 'buffer' })
  await importDialog.locator('input[type="file"]').setInputFiles({ name: 'users.xlsx', mimeType: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet', buffer: content })
  await expect(importDialog.locator('.vxe-table')).toContainText('Imported User')

  expect(pageErrors).toEqual([])
})

test('VXE popup table cell editing remains usable', async ({ page }) => {
  const pageErrors: string[] = []
  page.on('pageerror', (error) => pageErrors.push(error.message))
  await mockBackend(page, [])
  await login(page)

  await page.evaluate(() => { window.location.hash = '#/commodityManagement' })
  await expect(page.locator('.v-breadcrumbs')).toContainText('Commodity Management')
  await page.locator('.mdi-plus').first().click()

  const dialog = page.locator('.v-dialog')
  await expect(dialog.locator('.vxe-table')).toBeVisible()
  await dialog.locator('.mdi-plus').click()
  await dialog.locator('.vxe-body--column').nth(1).click()
  await expect(dialog.locator('.vxe-input--inner').first()).toBeVisible()
  await dialog.locator('.vxe-input--inner').first().fill('SKU-EDIT-001')

  expect(pageErrors).toEqual([])
})
