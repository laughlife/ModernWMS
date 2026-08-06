import type { App } from 'vue'
import printDirective from 'vue3-print-nb'
import { hiprint } from 'yk-vue-plugin-hiprint'

export function installPrinting(app: App) {
  app.use(printDirective)
}

export function createPrintElementTypeGroup(title: string, elements: object[]) {
  return new hiprint.PrintElementTypeGroup(title, elements)
}

export function initializePrintElementTypes(provider: object, container: string, moduleName: string) {
  hiprint.init({ providers: [provider] })
  hiprint.PrintElementTypeManager.build(container, moduleName)
}

export function createPrintTemplate(options: object) {
  return new hiprint.PrintTemplate(options)
}
