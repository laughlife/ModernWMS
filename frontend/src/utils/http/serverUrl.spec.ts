import { describe, expect, it } from 'vitest'
import { buildServerUrl } from './serverUrl'

describe('buildServerUrl', () => {
  it('joins the configured API host and port', () => {
    expect(buildServerUrl('http://127.0.0.1', '21011')).toBe('http://127.0.0.1:21011')
  })

  it('removes a trailing slash before appending the port', () => {
    expect(buildServerUrl('http://127.0.0.1/', '21011')).toBe('http://127.0.0.1:21011')
  })

  it('supports same-origin reverse proxy paths without a port', () => {
    expect(buildServerUrl('/api/', '')).toBe('/api')
  })
})
