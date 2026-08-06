import { btnGroupItem } from '../System/Form'

export interface OperatorGroupVO {
  sequence: number
  group_name: string
  leader_name: string
  phone: string
}

export interface DataProps {
  tableData: OperatorGroupVO[]
  btnList: btnGroupItem[]
  authorityList: string[]
}
