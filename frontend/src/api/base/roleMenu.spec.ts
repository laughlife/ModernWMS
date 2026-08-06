import { beforeEach, describe, expect, it, vi } from 'vitest'
import { updateRoleMenuBatch } from './roleMenu'

const { httpMock } = vi.hoisted(() => ({
  httpMock: vi.fn()
}))

vi.mock('@/utils/http/request', () => ({
  default: httpMock
}))

describe('role menu api', () => {
  beforeEach(() => {
    httpMock.mockReset()
  })

  it('submits current role final permission tree to batch endpoint', () => {
    const payload = {
      userrole_id: 1,
      detailList: [
        {
          menu_id: 20,
          menu_actions_authority: ['save']
        }
      ]
    }

    updateRoleMenuBatch(payload)

    expect(httpMock).toHaveBeenCalledWith({
      url: '/rolemenu/batch',
      method: 'put',
      data: payload
    })
  })
})
