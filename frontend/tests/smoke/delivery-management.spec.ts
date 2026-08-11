import { expect, test, type Page } from 'playwright/test'

const deliveryMenu = [{
  id: 30,
  menu_name: 'deliveryManagement',
  module: 'deliveryManagement',
  vue_path: 'deliveryManagement',
  vue_path_detail: '',
  vue_directory: 'deliveryManagement/deliveryManagement',
  sort: 1,
  tenant_id: 1,
  menu_actions: ['read', 'weighed-weigh', 'delivered-setCarrier', 'delivered-delivery', 'signedIn-export']
}]

const pendingRow = {
  id: 101,
  dispatch_no: 'DB-001',
  dispatch_status: 5,
  main_image: '',
  commodity_name: '测试商品',
  fba_sku: 'FNSKU-001',
  qty: 30,
  picked_qty: 30,
  variant_qty: 1,
  box_count: 3,
  fba_shipment_id: 99,
  volume: 4.75,
  weighing_weight: 60,
  creator: 'WMS创建人',
  dept_name: '朝阳启航',
  order_user_name: '李远航',
  volume_divisor: 5000,
  carrier_unit: '测试承运单位'
}

async function mockBackend(page: Page) {
  let pendingTotal = 5
  let completedTotal = 1
  await page.route('http://127.0.0.1:21011/**', async (route) => {
    const request = route.request()
    const path = new URL(request.url()).pathname
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
    } else if (path.endsWith('/fba-shipment/page')) {
      data = { rows: [], totals: 2 }
    } else if (path.endsWith('/dispatchlist/weighing-shipments')) {
      data = { rows: [], totals: 4 }
    } else if (path.endsWith('/dispatchlist/list')) {
      const sqlTitle = String(request.postDataJSON()?.sqlTitle || '')
      if (sqlTitle === 'dispatch_status=2') data = { rows: [], totals: 3 }
      if (sqlTitle === 'dispatch_status=3') data = { rows: [], totals: 1 }
      if (sqlTitle === 'dispatch_status=5') data = { rows: pendingTotal > 0 ? [pendingRow] : [], totals: pendingTotal }
      if (sqlTitle === 'dispatch_status=6') data = { rows: completedTotal > 0 ? [pendingRow] : [], totals: completedTotal }
    } else if (path.endsWith('/dispatchlist/weighing-boxes')) {
      data = [
        { erp_box_id: 1, box_no: 'FBA-BOX-001', weighing_volume: 32000 },
        { erp_box_id: 2, box_no: 'FBA-BOX-002', weighing_volume: 16000 },
        { erp_box_id: 3, box_no: 'FBA-BOX-003', weighing_volume: 8000 }
      ]
    } else if (path.endsWith('/dispatchlist/delivery')) {
      const deliveredRows = Array.isArray(request.postDataJSON()) ? request.postDataJSON().length : 0
      pendingTotal = Math.max(0, pendingTotal - deliveredRows)
      completedTotal += deliveredRows
      data = '出库成功'
    }

    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({ isSuccess: true, data, errorMessage: '' })
    })
  })
}

test('delivery workflow shows counts, box formulas and the completed layout', async ({ page }) => {
  await mockBackend(page)
  await page.goto('/#/login')
  await page.locator('.loginBtn').click()
  await expect(page).toHaveURL(/#\/homepage$/)
  await page.evaluate(() => { window.location.hash = '#/deliveryManagement' })

  for (const [tab, count] of [
    ['tabFbaShipment', '2'],
    ['tabGoodsToBePicked', '3'],
    ['tabPicked', '1'],
    ['tabWeighed', '4'],
    ['tabDelivered', '5']
  ] as const) {
    await expect(page.locator(`[data-status-tab="${tab}"] .status-count-badge`)).toContainText(count)
  }
  await expect(page.locator('[data-status-tab="tabCompleted"] .status-count-badge')).toHaveCount(0)

  await page.locator('[data-status-tab="tabDelivered"]').click()
  await expect(page.locator('.vxe-table')).toContainText('测试商品')
  await page.locator('.vxe-table .mdi-calculator-variant').click()
  const volumeDialog = page.locator('.v-dialog').filter({ hasText: '设置材积比' })
  await expect(volumeDialog).toContainText('FBA-BOX-001：32000.00 cm³ ÷ 5000 = 6.40')
  await expect(volumeDialog).not.toContainText('kg')
  const divisor6000 = volumeDialog.locator('.volume-option').filter({ hasText: '材积比 6000' })
  await divisor6000.getByText('FBA-BOX-002').click()
  await expect(divisor6000).toHaveAttribute('aria-checked', 'true')
  await volumeDialog.getByRole('button', { name: '关闭' }).click()

  const deliveryResponse = page.waitForResponse((response) => new URL(response.url()).pathname === '/dispatchlist/delivery')
  await page.locator('.vxe-table .mdi-send-outline').click()
  await page.getByRole('button', { name: '确认' }).click()
  await deliveryResponse

  await expect(page.locator('[data-status-tab="tabCompleted"]')).toHaveClass(/v-tab--selected/)
  await expect(page.locator('[data-status-tab="tabDelivered"] .status-count-badge')).toContainText('4')
  const completedTable = page.locator('.v-window-item--active .vxe-table')
  await expect(completedTable).toContainText('商品图片')
  await expect(completedTable).toContainText('商品信息')
  await expect(completedTable).toContainText('商品/数量')
  await expect(completedTable).toContainText('所属信息')
  await expect(completedTable).toContainText('体积(m³)')
  await expect(completedTable).toContainText('称重重量')
  await expect(completedTable).toContainText('朝阳启航 | 李远航')
  await expect(completedTable).toContainText('4.75 m³')
  await expect(completedTable).toContainText('60 kg')
  await expect(completedTable).toContainText('WMS创建人')
})
