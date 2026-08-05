import { describe, expect, it } from 'vitest'
import { isRefreshResponseCurrent } from './refreshSession'

describe('refresh session guard', () => {
  it('rejects a refresh response after logout cleared the session', () => {
    expect(isRefreshResponseCurrent('', '', 'access-token', 'refresh-token')).toBe(false)
  })

  it('accepts a response for the unchanged session', () => {
    expect(isRefreshResponseCurrent('access-token', 'refresh-token', 'access-token', 'refresh-token')).toBe(true)
  })
})
