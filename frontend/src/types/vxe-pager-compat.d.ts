import 'vxe-table'

declare module 'vxe-table' {
  namespace VxePagerEvents {
    type PageChange = import('vxe-pc-ui').VxePagerEvents.PageChange
  }

  namespace VxePagerPropTypes {
    type Layouts = import('vxe-pc-ui').VxePagerPropTypes.Layouts
  }
}

export {}
