import { NavListOptions, UniformFileNaming, VxeTableRow, btnGroupItem } from '../System/Form'

export interface RoleMenuVO extends UniformFileNaming {
  userrole_id?: number
  role_name?: string
  detailList: RoleMenuDetailVo[]
}

export interface RoleMenuDetailVo extends VxeTableRow {
  id: number
  menu_id?: number
  menu_name?: string
  authority?: number
  menu_actions_authority: string[]
}

export interface MenuOption {
  id: number
  menu_name: string
  module: string
  vue_path?: string
  vue_path_detail?: string
  vue_directory?: string
  sort?: number
  menu_actions?: string[]
}

export interface RoleMenuBatchPayload {
  userrole_id: number
  detailList: RoleMenuBatchDetail[]
}

export interface RoleMenuBatchDetail {
  menu_id: number
  menu_actions_authority: string[]
}

export interface DataProps {
  activeRoleMenuForm: RoleMenuVO
  menuOptions: MenuOption[]
  roleList: RoleMenuVO[]
  showDialog: boolean
  dialogForm: RoleMenuVO
  navListOptions: NavListOptions
  btnList: btnGroupItem[]
  editMenuDialogCurrentRow: RoleMenuDetailVo
}
