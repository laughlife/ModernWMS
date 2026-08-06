import { UniformFileNaming, TablePage } from '../System/Form'

export interface SupplierVO extends UniformFileNaming {
  id: number
  supplier_name: string
  name: string
  linkman: string
  telephone_num: string
  qq: string
  email: string
  province_name: string
  city_name: string
  address_line: string
  remark: string
}

export interface DataProps {
  tableData: SupplierVO[]
  tablePage: TablePage
}
