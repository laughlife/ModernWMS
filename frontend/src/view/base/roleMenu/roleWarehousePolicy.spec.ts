import { describe, expect, it } from 'vitest'
import { buildRoleWarehousePayload } from './roleWarehousePolicy'

describe('roleWarehousePolicy', () => {
  it('sorts and deduplicates warehouse ids in the replacement payload', () => {
    expect(buildRoleWarehousePayload(7, [320118, 9, 320118])).toEqual({
      userrole_id: 7,
      warehouse_ids: [9, 320118]
    })
  })
})
