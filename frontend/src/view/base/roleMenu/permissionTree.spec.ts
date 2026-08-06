import { describe, expect, it } from 'vitest'
import {
  buildPermissionTree,
  createInitialPermissionState,
  resolveMenuActions,
  serializePermissionPayload,
  togglePermissionNode
} from './permissionTree'
import type { MenuOption, RoleMenuDetailVo } from '@/types/Base/RoleMenu'

const menus: MenuOption[] = [
  {
    id: 20,
    menu_name: 'companySetting',
    module: 'baseModule',
    sort: 1,
    menu_actions: ['save', 'delete']
  },
  {
    id: 21,
    menu_name: 'stockManagement',
    module: '',
    sort: 2,
    menu_actions: ['stock-export']
  }
]

const details: RoleMenuDetailVo[] = [
  {
    id: 7,
    menu_id: 20,
    menu_name: 'companySetting',
    authority: 1,
    menu_actions_authority: ['save']
  }
]

describe('permission tree state', () => {
  it('builds module-menu-access-action tree items and initial selected keys', () => {
    const tree = buildPermissionTree(menus, details)
    const selected = createInitialPermissionState(details)

    expect(tree).toHaveLength(2)
    expect(tree[0].id).toBe('module:baseModule')
    expect(tree[0].children[0].children.map((item) => item.id)).toEqual(['access:20', 'action:20:save', 'action:20:delete'])
    expect(selected.has('access:20')).toBe(true)
    expect(selected.has('action:20:save')).toBe(true)
  })

  it('selecting an action automatically grants menu access', () => {
    const selected = createInitialPermissionState([])
    const next = togglePermissionNode({
      nodeId: 'action:20:delete',
      checked: true,
      selected,
      menus
    })

    expect(next.has('access:20')).toBe(true)
    expect(next.has('action:20:delete')).toBe(true)
  })

  it('clearing menu access clears all operation permissions for that menu', () => {
    const selected = createInitialPermissionState(details)
    selected.add('action:20:delete')

    const next = togglePermissionNode({
      nodeId: 'access:20',
      checked: false,
      selected,
      menus
    })

    expect(next.has('access:20')).toBe(false)
    expect(next.has('action:20:save')).toBe(false)
    expect(next.has('action:20:delete')).toBe(false)
  })

  it('serializes only the final selected menu permissions without relying on old row ids', () => {
    const selected = createInitialPermissionState(details)
    selected.add('access:21')
    selected.add('action:21:stock-export')

    const payload = serializePermissionPayload({
      userroleId: 3,
      menus,
      selected
    })

    expect(payload).toEqual({
      userrole_id: 3,
      detailList: [
        {
          menu_id: 20,
          menu_actions_authority: ['save']
        },
        {
          menu_id: 21,
          menu_actions_authority: ['stock-export']
        }
      ]
    })
  })

  it('keeps non-empty backend menu actions before falling back to actionDict defaults', () => {
    expect(resolveMenuActions({
      id: 30,
      menu_name: 'supplier',
      module: 'baseModule',
      menu_actions: ['save', 'approve']
    }, ['save', 'delete'])).toEqual(['save', 'approve'])

    expect(resolveMenuActions({
      id: 31,
      menu_name: 'stockManagement',
      module: '',
      menu_actions: []
    }, ['stock-export'])).toEqual(['stock-export'])
  })
})
