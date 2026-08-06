<template>
  <div class="container">
    <v-card class="mt-5 permission-card">
      <v-card-text>
        <v-row :no-gutters="true" class="permission-layout">
          <v-col cols="12" md="3" class="dataListCol">
            <v-card :height="panelHeight" class="role-panel">
              <NavListVue
                :list-data="data.roleList"
                :title="data.navListOptions.title"
                :label-key="data.navListOptions.labelKey"
                :index-key="data.navListOptions.indexKey"
                :index-value="data.navListOptions.indexValue"
                @item-click="method.navListClick"
              />
            </v-card>
          </v-col>

          <v-col cols="12" md="9">
            <v-card :height="panelHeight" class="permission-panel">
              <div class="permission-toolbar">
                <div class="permission-summary">
                  <div class="permission-title">{{ $t('router.sideBar.roleMenu') }}</div>
                  <div class="permission-subtitle">
                    {{ selectedRoleName || $t('base.roleMenu.selectRoleFirst') }}
                    <span v-if="data.isDirty" class="dirty-dot">{{ $t('base.roleMenu.unsaved') }}</span>
                  </div>
                </div>

                <div class="permission-actions">
                  <v-chip color="primary" variant="tonal" size="small">
                    {{ $t('base.roleMenu.selectedMenuCount') }}：{{ selectedMenuCount }}
                  </v-chip>
                  <v-btn
                    variant="tonal"
                    color="primary"
                    size="small"
                    :disabled="!hasActiveRole || isLoading || data.isSaving"
                    @click="method.toggleSelectAll"
                  >
                    {{ isAllSelected ? $t('base.roleMenu.unselectAll') : $t('base.roleMenu.selectAll') }}
                  </v-btn>
                  <v-btn
                    variant="tonal"
                    color="primary"
                    size="small"
                    :disabled="isLoading || data.isSaving || !permissionTree.length"
                    @click="method.toggleExpandAll"
                  >
                    {{ isAllExpanded ? $t('base.roleMenu.collapseAll') : $t('base.roleMenu.expandAll') }}
                  </v-btn>
                  <v-btn
                    color="primary"
                    size="small"
                    :loading="data.isSaving"
                    :disabled="!hasActiveRole || !data.isDirty || isLoading"
                    @click="method.savePermissions"
                  >
                    {{ $t('base.roleMenu.savePermissions') }}
                  </v-btn>
                </div>
              </div>

              <v-divider />

              <div class="permission-tree-wrap">
                <div v-if="isLoading" class="loading-state">
                  <v-progress-linear indeterminate color="primary" />
                </div>
                <div v-else-if="!hasActiveRole" class="empty-state">{{ $t('base.roleMenu.selectRoleFirst') }}</div>
                <div v-else-if="!permissionTree.length && !isLoading" class="empty-state">{{ $t('system.page.noData') }}</div>
                <v-treeview
                  v-else
                  :items="permissionTree"
                  :opened="data.openedNodeIds"
                  item-title="title"
                  item-value="id"
                  density="compact"
                  open-on-click
                  :disabled="data.isSaving"
                  class="permission-tree"
                  @update:opened="method.updateOpenedNodes"
                >
                  <template #prepend="{ item }">
                    <v-checkbox-btn
                      :model-value="method.getNodeState(item).checked"
                      :indeterminate="method.getNodeState(item).indeterminate"
                      density="compact"
                      color="primary"
                      class="permission-checkbox"
                      @click.stop
                      @update:model-value="(checked) => method.toggleNode(item, Boolean(checked))"
                    />
                  </template>

                  <template #title="{ item }">
                    <div class="permission-node">
                      <span>{{ method.getNodeTitle(item) }}</span>
                      <v-chip v-if="item.type === 'access'" size="x-small" color="primary" variant="tonal">
                        {{ $t('base.roleMenu.menuAccess') }}
                      </v-chip>
                      <v-chip v-else-if="item.type === 'action'" size="x-small" variant="tonal">
                        {{ $t('base.roleMenu.operation') }}
                      </v-chip>
                    </div>
                  </template>
                </v-treeview>
              </div>
            </v-card>
          </v-col>
        </v-row>
      </v-card-text>
    </v-card>
  </div>
</template>

<script lang="ts" setup>
import { computed, onMounted, reactive } from 'vue'
import { computedCardHeight } from '@/constant/style'
import type { DataProps, MenuOption, RoleMenuVO } from '@/types/Base/RoleMenu'
import { getMenus, getUserAuthority, updateRoleMenuBatch } from '@/api/base/roleMenu'
import { getUserRoleAll } from '@/api/base/userRoleSetting'
import { hookComponent } from '@/components/system'
import i18n from '@/languages/i18n'
import NavListVue from '@/components/page/nav-list.vue'
import { actionDict, getActionName } from './actionList'
import {
  buildPermissionTree,
  createInitialPermissionState,
  flattenPermissionTree,
  getPermissionNodeCheckState,
  getSelectableNodeIds,
  getSelectedMenuCount,
  normalizePermissionSelection,
  resolveMenuActions,
  serializePermissionPayload,
  setPermissionNodeCascade,
  type PermissionTreeNode
} from './permissionTree'

const data: DataProps & {
  selectedNodeIds: Set<string>
  originalSelectedNodeIds: Set<string>
  openedNodeIds: string[]
  isLoadingMenus: boolean
  isLoadingRole: boolean
  isSaving: boolean
  isDirty: boolean
} = reactive({
  navListOptions: {
    title: i18n.global.t('base.roleMenu.role_name'),
    labelKey: 'role_name',
    indexKey: 'userrole_id',
    indexValue: ''
  },
  activeRoleMenuForm: {
    userrole_id: 0,
    role_name: '',
    detailList: []
  },
  menuOptions: [],
  roleList: [],
  showDialog: false,
  dialogForm: {
    detailList: []
  },
  btnList: [],
  editMenuDialogCurrentRow: {
    id: -1,
    menu_id: -1,
    menu_name: '',
    menu_actions_authority: []
  },
  selectedNodeIds: new Set<string>(),
  originalSelectedNodeIds: new Set<string>(),
  openedNodeIds: [],
  isLoadingMenus: false,
  isLoadingRole: false,
  isSaving: false,
  isDirty: false
})

const method = reactive({
  showError: (message?: string) => {
    hookComponent.$message({
      type: 'error',
      content: message || i18n.global.t('system.tips.requestFail')
    })
  },
  getRoles: async () => {
    try {
      const response = await getUserRoleAll()
      const res = response?.data
      if (!res?.isSuccess) {
        method.showError(res?.errorMessage)
        return false
      }
      data.roleList = (res.data ?? []).map((role: { id: number; role_name: string; is_valid?: boolean }) => ({
        userrole_id: role.id,
        role_name: role.role_name,
        is_valid: role.is_valid,
        detailList: []
      }))
      return true
    } catch {
      method.showError()
      return false
    }
  },
  getMenuOptions: async () => {
    data.isLoadingMenus = true
    try {
      const response = await getMenus()
      const res = response?.data
      if (!res?.isSuccess) {
        method.showError(res?.errorMessage)
        return false
      }
      data.menuOptions = (res.data ?? []).map((menu: MenuOption) => ({
        ...menu,
        menu_actions: resolveMenuActions(menu, actionDict[menu.menu_name])
      }))
      return true
    } catch {
      method.showError()
      return false
    } finally {
      data.isLoadingMenus = false
    }
  },
  refresh: async () => {
    const [rolesLoaded, menusLoaded] = await Promise.all([method.getRoles(), method.getMenuOptions()])
    if (!rolesLoaded || !menusLoaded) {
      return
    }
    if (data.roleList.findIndex((item) => item.userrole_id === data.activeRoleMenuForm.userrole_id) > -1 && data.activeRoleMenuForm.userrole_id) {
      await method.loadRoleMenus(data.activeRoleMenuForm.userrole_id)
      return
    }
    if (data.roleList.length > 0) {
      await method.selectRole(data.roleList[0])
      return
    }
    method.clearActiveRole()
  },
  loadRoleMenus: async (userrole_id: number) => {
    data.isLoadingRole = true
    const role = data.roleList.find((item) => item.userrole_id === userrole_id)
    try {
      const response = await getUserAuthority(userrole_id)
      const res = response?.data
      if (!res?.isSuccess) {
        method.showError(res?.errorMessage)
        data.navListOptions.indexValue = data.activeRoleMenuForm.userrole_id ? String(data.activeRoleMenuForm.userrole_id) : ''
        return false
      }
      method.applyRolePermissions({
        userrole_id,
        role_name: role?.role_name,
        detailList: (res.data ?? []).map((menu: MenuOption) => ({
          id: 0,
          menu_id: menu.id,
          menu_name: menu.menu_name,
          authority: 1,
          menu_actions_authority: menu.menu_actions ?? []
        }))
      })
      return true
    } catch {
      method.showError()
      data.navListOptions.indexValue = data.activeRoleMenuForm.userrole_id ? String(data.activeRoleMenuForm.userrole_id) : ''
      return false
    } finally {
      data.isLoadingRole = false
    }
  },
  applyRolePermissions: (roleMenu: RoleMenuVO) => {
    data.activeRoleMenuForm = {
      userrole_id: roleMenu.userrole_id,
      role_name: roleMenu.role_name,
      detailList: roleMenu.detailList ?? []
    }
    const selected = normalizePermissionSelection(createInitialPermissionState(data.activeRoleMenuForm.detailList), data.menuOptions)
    data.selectedNodeIds = new Set(selected)
    data.originalSelectedNodeIds = new Set(selected)
    data.navListOptions.indexValue = roleMenu.userrole_id ? String(roleMenu.userrole_id) : ''
    data.isDirty = false
  },
  clearActiveRole: () => {
    data.activeRoleMenuForm = {
      userrole_id: 0,
      role_name: '',
      detailList: []
    }
    data.navListOptions.indexValue = ''
    data.selectedNodeIds = new Set()
    data.originalSelectedNodeIds = new Set()
    data.isDirty = false
  },
  selectRole: async (item: RoleMenuVO) => {
    if (!item.userrole_id || data.isSaving) {
      return
    }
    await method.loadRoleMenus(item.userrole_id)
  },
  navListClick: (item: RoleMenuVO) => {
    if (data.isSaving || !item.userrole_id || item.userrole_id === data.activeRoleMenuForm.userrole_id) {
      return
    }
    if (!data.isDirty) {
      method.selectRole(item)
      return
    }
    hookComponent.$dialog({
      content: i18n.global.t('base.roleMenu.switchRoleConfirm'),
      handleConfirm: () => {
        method.selectRole(item)
      }
    })
  },
  updateOpenedNodes: (opened: unknown[]) => {
    data.openedNodeIds = opened.map(String)
  },
  getNodeState: (node: PermissionTreeNode) => getPermissionNodeCheckState(node, data.selectedNodeIds),
  toggleNode: (node: PermissionTreeNode, checked: boolean) => {
    if (data.isSaving) {
      return
    }
    data.selectedNodeIds = setPermissionNodeCascade({
      node,
      checked,
      selected: data.selectedNodeIds,
      menus: data.menuOptions
    })
    method.markDirty()
  },
  toggleSelectAll: () => {
    if (data.isSaving) {
      return
    }
    if (isAllSelected.value) {
      data.selectedNodeIds = new Set()
    } else {
      data.selectedNodeIds = new Set(getSelectableNodeIds(data.menuOptions))
    }
    method.markDirty()
  },
  toggleExpandAll: () => {
    data.openedNodeIds = isAllExpanded.value ? [] : flattenPermissionTree(permissionTree.value).filter((item) => item.children.length).map((item) => item.id)
  },
  markDirty: () => {
    data.selectedNodeIds = normalizePermissionSelection(data.selectedNodeIds, data.menuOptions)
    data.isDirty = method.serializeSelected(data.selectedNodeIds) !== method.serializeSelected(data.originalSelectedNodeIds)
  },
  serializeSelected: (selected: Set<string>) => Array.from(selected).sort().join('|'),
  savePermissions: async () => {
    if (data.isSaving) {
      return
    }
    if (!data.activeRoleMenuForm.userrole_id) {
      hookComponent.$message({
        type: 'error',
        content: i18n.global.t('base.roleMenu.beforeUpdateOrDel')
      })
      return
    }
    data.isSaving = true
    const payload = serializePermissionPayload({
      userroleId: data.activeRoleMenuForm.userrole_id,
      menus: data.menuOptions,
      selected: data.selectedNodeIds
    })
    try {
      const response = await updateRoleMenuBatch(payload)
      const res = response?.data
      if (!res?.isSuccess) {
        method.showError(res?.errorMessage)
        return
      }
      hookComponent.$message({
        type: 'success',
        content: `${ i18n.global.t('system.page.update') }${ i18n.global.t('system.tips.success') }`
      })
      const reloaded = await method.loadRoleMenus(data.activeRoleMenuForm.userrole_id)
      if (!reloaded) {
        data.originalSelectedNodeIds = new Set(data.selectedNodeIds)
        data.isDirty = false
      }
    } catch {
      method.showError()
    } finally {
      data.isSaving = false
    }
  },
  getNodeTitle: (node: PermissionTreeNode) => {
    if (node.type === 'module') {
      return i18n.global.t(`router.sideBar.${ node.title }`)
    }
    if (node.type === 'menu') {
      return i18n.global.t(`router.sideBar.${ node.menuName }`)
    }
    if (node.type === 'access') {
      return i18n.global.t('base.roleMenu.menuAccess')
    }
    return getActionName(node.actionCode ?? node.title, node.menuName)
  }
})

onMounted(async () => {
  await method.refresh()
})

const permissionTree = computed(() => buildPermissionTree(data.menuOptions, data.activeRoleMenuForm.detailList))
const panelHeight = computed(() => computedCardHeight({ hasTab: false, hasOperateBtn: false }))
const selectedMenuCount = computed(() => getSelectedMenuCount(data.selectedNodeIds))
const selectedRoleName = computed(() => data.activeRoleMenuForm.role_name)
const hasActiveRole = computed(() => Boolean(data.activeRoleMenuForm.userrole_id))
const isLoading = computed(() => data.isLoadingMenus || data.isLoadingRole)
const selectableNodeIds = computed(() => getSelectableNodeIds(data.menuOptions))
const isAllSelected = computed(() => selectableNodeIds.value.length > 0 && selectableNodeIds.value.every((id) => data.selectedNodeIds.has(id)))
const isAllExpanded = computed(() => {
  const expandableIds = flattenPermissionTree(permissionTree.value).filter((item) => item.children.length).map((item) => item.id)
  return expandableIds.length > 0 && expandableIds.every((id) => data.openedNodeIds.includes(id))
})
</script>

<style scoped lang="less">
.permission-card {
  overflow: hidden;
}

.permission-layout {
  min-width: 760px;
}

.dataListCol {
  box-sizing: border-box;
  padding-right: 10px !important;
}

.role-panel,
.permission-panel {
  border: 1px solid #eef0f4;
  box-shadow: none;
}

.permission-panel {
  display: flex;
  flex-direction: column;
}

.permission-toolbar {
  min-height: 76px;
  padding: 12px 16px;
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 16px;
  background: #fbfcff;
}

.permission-summary {
  min-width: 160px;
}

.permission-title {
  font-size: 16px;
  font-weight: 600;
  color: #1f2d3d;
}

.permission-subtitle {
  margin-top: 4px;
  color: #7b8794;
  font-size: 13px;
}

.dirty-dot {
  display: inline-block;
  margin-left: 8px;
  color: #f59e0b;
}

.permission-actions {
  display: flex;
  flex-wrap: wrap;
  justify-content: flex-end;
  align-items: center;
  gap: 8px;
}

.permission-tree-wrap {
  flex: 1;
  min-height: 0;
  overflow: auto;
  padding: 12px 16px;
}

.permission-tree {
  color: #334155;
}

.loading-state {
  height: 100%;
  display: flex;
  align-items: flex-start;
}

.permission-checkbox {
  margin-inline-end: 4px;
}

.permission-node {
  min-height: 32px;
  display: flex;
  align-items: center;
  gap: 8px;
}

.empty-state {
  height: 100%;
  display: flex;
  align-items: center;
  justify-content: center;
  color: #9aa3af;
}

@media (max-width: 960px) {
  .permission-layout {
    min-width: 0;
  }

  .dataListCol {
    padding-right: 0 !important;
    padding-bottom: 10px;
  }

  .permission-toolbar {
    align-items: flex-start;
    flex-direction: column;
  }

  .permission-actions {
    justify-content: flex-start;
  }
}
</style>
