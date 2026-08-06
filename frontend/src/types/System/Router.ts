import { RouteMeta } from 'vue-router'

export interface RouterProps {
  name: string
  module?: string
  path: string
  directory: string
  redirect: string
  component: any
  children?: RouterProps[]
  meta?: RouteMeta
}
