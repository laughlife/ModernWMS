import { expect, test, type Page } from 'playwright/test'

const deliveryMenu = [{
  id: 30,
  menu_name: 'deliveryManagement',
  module: 'deliveryManagement',
  vue_path: 'deliveryManagement',
  vue_path_detail: '',
  vue_directory: 'deliveryManagement/deliveryManagement',
  sort: 1,
  menu_actions: ['read', 'weighed-weigh', 'delivered-setCarrier', 'delivered-delivery', 'signedIn-export']
}]

async function mockBackend(page: Page) {
  await page.route('http://127.0.0.1:21011/**', async (route) => {
    const path = new URL(route.request().url()).pathname
    let data: unknown = { rows: [], totals: 0 }

    if (path.endsWith('/login')) {
      data = {
        access_token: 'delivery-test-token',
        refresh_token: 'delivery-test-refresh-token',
        expire: 60,
        userrole_id: 1,
        user_name: 'admin'
      }
    } else if (path.endsWith('/rolemenu/authority')) {
      data = deliveryMenu
    } else if (path.endsWith('/warehouse/access-options')) {
      data = { warehouses: [{ id: 320118, name: '深圳自建仓' }], default_warehouse_id: 320118 }
    } else if (path.endsWith('/packing-task-query/page')) {
      data = { rows: [], totals: 2 }
    } else if (path.endsWith('/dispatch-workflow/counts')) {
      data = { PENDING_PICK: 3, PICKED: 1, WEIGHING: 4, PENDING_OUTBOUND: 5, OUTBOUND: 0 }
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

test('delivery workflow shows packing-task and workflow counts from the formal APIs', async ({ page }) => {
  await mockBackend(page)
  await page.goto('/#/login')
  await page.locator('input[type="text"]').fill('admin')
  await page.locator('input[type="password"]').fill('test-password')
  await page.locator('.loginBtn').click()
  await expect(page).toHaveURL(/#\/homepage$/)
  const accessResponse = page.waitForResponse((response) =>
    new URL(response.url()).pathname === '/warehouse/access-options')
  const countResponse = page.waitForResponse((response) =>
    new URL(response.url()).pathname === '/dispatch-workflow/counts')
  await page.evaluate(() => { window.location.hash = '#/deliveryManagement' })
  await accessResponse
  await page.locator('.warehouse-selector').click()
  await page.getByRole('option', { name: '深圳自建仓' }).click()
  await countResponse

  for (const [tab, count] of [
    ['tabFbaShipment', '2'],
    ['tabGoodsToBePicked', '3'],
    ['tabPicked', '1'],
    ['tabWeighed', '4'],
    ['tabDelivered', '5']
  ] as const) {
    await expect(page.locator(`[data-status-tab="${tab}"] .status-count-badge`)).toContainText(count)
  }
  await expect(page.locator('[data-status-tab="tabCompleted"] .status-count-badge')).toContainText('0')
})
