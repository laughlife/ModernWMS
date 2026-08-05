export function buildServerUrl(basePath: string, port: string): string {
  const normalizedBasePath = basePath.replace(/\/$/, '')
  return port ? `${normalizedBasePath}:${port}` : normalizedBasePath
}
