export function buildServerUrl(basePath: string, port: string): string {
  return `${basePath.replace(/\/$/, '')}:${port}`
}
