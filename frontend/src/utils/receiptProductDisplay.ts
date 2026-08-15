interface ReceiptProductDisplayInput {
  product_name?: string | null
  quantity?: number | null
  sku?: string | null
}

export const buildReceiptProductDisplay = (product: ReceiptProductDisplayInput): { title: string; sku: string } => ({
  title: `${product.product_name || '-'} × ${product.quantity ?? 0}`,
  sku: product.sku || '-'
})
