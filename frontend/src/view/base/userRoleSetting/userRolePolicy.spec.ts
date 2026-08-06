import { describe, expect, it } from 'vitest'
import { isAdminRole } from './userRolePolicy'

describe('user role protection policy', () => {
  it.each(['admin', 'ADMIN', ' Admin '])('treats %s as the reserved admin role', (roleName) => {
    expect(isAdminRole(roleName)).toBe(true)
  })

  it.each(['administrator', 'manager', '', undefined])('does not restrict non-admin role %s', (roleName) => {
    expect(isAdminRole(roleName)).toBe(false)
  })
})
