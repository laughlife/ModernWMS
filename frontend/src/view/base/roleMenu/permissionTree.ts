import type { MenuOption, RoleMenuBatchPayload, RoleMenuDetailVo } from '@/types/Base/RoleMenu'

export type PermissionNodeType = 'module' | 'menu' | 'access' | 'action'

export interface PermissionTreeNode {
  id: string
  title: string
  type: PermissionNodeType
  menuId?: number
  menuName?: string
  actionCode?: string
  children: PermissionTreeNode[]
}

export interface PermissionCheckState {
  checked: boolean
  indeterminate: boolean
}

export const ACCESS_NODE_PREFIX = 'access:'
export const ACTION_NODE_PREFIX = 'action:'
export const MENU_NODE_PREFIX = 'menu:'
export const MODULE_NODE_PREFIX = 'module:'

const getMenuId = (nodeId: string) => Number(nodeId.split(':')[1])

const getActionParts = (nodeId: string) => {
  const [, menuId, ...actionParts] = nodeId.split(':')
  return {
    menuId: Number(menuId),
    actionCode: actionParts.join(':')
  }
}

export const accessNodeId = (menuId: number) => `${ ACCESS_NODE_PREFIX }${ menuId }`
export const actionNodeId = (menuId: number, actionCode: string) => `${ ACTION_NODE_PREFIX }${ menuId }:${ actionCode }`

const normalizeActions = (actions: string[] = []) => Array.from(new Set(actions.map((action) => action?.trim()).filter(Boolean)))

export const resolveMenuActions = (menu: MenuOption, fallbackActions: string[] = []) => {
  const backendActions = normalizeActions(menu.menu_actions)

  return backendActions.length > 0 ? backendActions : normalizeActions(fallbackActions)
}

export const getMenuActions = (menu: MenuOption) => normalizeActions(menu.menu_actions)

export const buildPermissionTree = (menus: MenuOption[], details: RoleMenuDetailVo[] = []): PermissionTreeNode[] => {
  const detailMenuIds = new Set(details.map((item) => item.menu_id).filter((menuId): menuId is number => Boolean(menuId)))
  const sortedMenus = [...menus].sort((a, b) => (a.sort ?? 0) - (b.sort ?? 0) || a.menu_name.localeCompare(b.menu_name))
  const moduleMap = new Map<string, PermissionTreeNode>()

  sortedMenus.forEach((menu) => {
    const moduleName = menu.module || menu.menu_name || String(menu.id)
    const moduleId = `${ MODULE_NODE_PREFIX }${ moduleName }`
    const moduleNode = moduleMap.get(moduleId) ?? {
      id: moduleId,
      title: moduleName,
      type: 'module' as const,
      children: []
    }

    const actions = getMenuActions(menu)
    const menuNode: PermissionTreeNode = {
      id: `${ MENU_NODE_PREFIX }${ menu.id }`,
      title: menu.menu_name,
      type: 'menu',
      menuId: menu.id,
      menuName: menu.menu_name,
      children: [
        {
          id: accessNodeId(menu.id),
          title: 'access',
          type: 'access',
          menuId: menu.id,
          menuName: menu.menu_name,
          children: []
        },
        ...actions.map((action) => ({
          id: actionNodeId(menu.id, action),
          title: action,
          type: 'action' as const,
          menuId: menu.id,
          menuName: menu.menu_name,
          actionCode: action,
          children: []
        }))
      ]
    }

    if (actions.length === 0 && detailMenuIds.has(menu.id)) {
      menuNode.children = [menuNode.children[0]]
    }

    moduleNode.children.push(menuNode)
    moduleMap.set(moduleId, moduleNode)
  })

  return Array.from(moduleMap.values())
}

export const flattenPermissionTree = (nodes: PermissionTreeNode[]): PermissionTreeNode[] => {
  const result: PermissionTreeNode[] = []
  const walk = (items: PermissionTreeNode[]) => {
    items.forEach((item) => {
      result.push(item)
      if (item.children.length) {
        walk(item.children)
      }
    })
  }

  walk(nodes)
  return result
}

export const createInitialPermissionState = (details: RoleMenuDetailVo[]) => {
  const selected = new Set<string>()

  details.forEach((detail) => {
    if (!detail.menu_id) {
      return
    }
    selected.add(accessNodeId(detail.menu_id))
    ;(detail.menu_actions_authority ?? []).forEach((action) => selected.add(actionNodeId(detail.menu_id!, action)))
  })

  return selected
}

export const getSelectableNodeIds = (menus: MenuOption[]) => {
  const ids: string[] = []

  menus.forEach((menu) => {
    ids.push(accessNodeId(menu.id))
    getMenuActions(menu).forEach((action) => ids.push(actionNodeId(menu.id, action)))
  })

  return ids
}

export const getMenuNodeIds = (menu: MenuOption) => [
  accessNodeId(menu.id),
  ...getMenuActions(menu).map((action) => actionNodeId(menu.id, action))
]

export const getNodeDescendantIds = (node: PermissionTreeNode): string[] => {
  if (node.type === 'access' || node.type === 'action') {
    return [node.id]
  }

  return node.children.flatMap((child) => getNodeDescendantIds(child))
}

export const getPermissionNodeCheckState = (node: PermissionTreeNode, selected: Set<string>): PermissionCheckState => {
  const leafIds = getNodeDescendantIds(node)
  const checkedCount = leafIds.filter((id) => selected.has(id)).length

  return {
    checked: leafIds.length > 0 && checkedCount === leafIds.length,
    indeterminate: checkedCount > 0 && checkedCount < leafIds.length
  }
}

export const getSelectedMenuCount = (selected: Set<string>) => Array.from(selected).filter((id) => id.startsWith(ACCESS_NODE_PREFIX)).length

export const normalizePermissionSelection = (selected: Set<string>, menus: MenuOption[]) => {
  const normalized = new Set<string>()
  const menusById = new Map(menus.map((menu) => [menu.id, menu]))

  selected.forEach((id) => {
    if (id.startsWith(ACCESS_NODE_PREFIX)) {
      const menuId = getMenuId(id)
      if (menusById.has(menuId)) {
        normalized.add(id)
      }
      return
    }

    if (id.startsWith(ACTION_NODE_PREFIX)) {
      const { menuId, actionCode } = getActionParts(id)
      const menu = menusById.get(menuId)
      if (menu && getMenuActions(menu).includes(actionCode)) {
        normalized.add(accessNodeId(menuId))
        normalized.add(id)
      }
    }
  })

  return normalized
}

export const togglePermissionNode = ({
  nodeId,
  checked,
  selected,
  menus
}: {
  nodeId: string
  checked: boolean
  selected: Set<string>
  menus: MenuOption[]
}) => {
  const next = new Set(selected)

  if (nodeId.startsWith(ACCESS_NODE_PREFIX)) {
    const menuId = getMenuId(nodeId)
    if (checked) {
      next.add(accessNodeId(menuId))
    } else {
      const menu = menus.find((item) => item.id === menuId)
      if (menu) {
        getMenuNodeIds(menu).forEach((id) => next.delete(id))
      }
    }
    return normalizePermissionSelection(next, menus)
  }

  if (nodeId.startsWith(ACTION_NODE_PREFIX)) {
    const { menuId } = getActionParts(nodeId)
    if (checked) {
      next.add(accessNodeId(menuId))
      next.add(nodeId)
    } else {
      next.delete(nodeId)
    }
    return normalizePermissionSelection(next, menus)
  }

  return normalizePermissionSelection(next, menus)
}

export const setPermissionNodeCascade = ({
  node,
  checked,
  selected,
  menus
}: {
  node: PermissionTreeNode
  checked: boolean
  selected: Set<string>
  menus: MenuOption[]
}) => {
  let next = new Set(selected)

  getNodeDescendantIds(node).forEach((nodeId) => {
    next = togglePermissionNode({
      nodeId,
      checked,
      selected: next,
      menus
    })
  })

  return normalizePermissionSelection(next, menus)
}

export const serializePermissionPayload = ({
  userroleId,
  menus,
  selected
}: {
  userroleId: number
  menus: MenuOption[]
  selected: Set<string>
}): RoleMenuBatchPayload => {
  return {
    userrole_id: userroleId,
    detailList: menus
      .filter((menu) => selected.has(accessNodeId(menu.id)))
      .map((menu) => {
        return {
          menu_id: menu.id,
          menu_actions_authority: getMenuActions(menu).filter((action) => selected.has(actionNodeId(menu.id, action)))
        }
      })
  }
}
