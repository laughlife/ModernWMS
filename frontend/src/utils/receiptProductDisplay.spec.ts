import { describe, expect, it } from 'vitest'
import { buildReceiptProductDisplay } from './receiptProductDisplay'

describe('receipt product display', () => {
  it('places product name and quantity above the SKU', () => {
    expect(buildReceiptProductDisplay({
      product_name: '测试产品',
      quantity: 12,
      sku: 'SKU-001'
    })).toEqual({
      title: '测试产品 × 12',
      sku: 'SKU-001'
    })
  })

  it('uses visible fallbacks for missing product data', () => {
    expect(buildReceiptProductDisplay({
      product_name: '',
      quantity: null,
      sku: ''
    })).toEqual({
      title: '- × 0',
      sku: '-'
    })
  })
})
