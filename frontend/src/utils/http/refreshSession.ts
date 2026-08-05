export function isRefreshResponseCurrent(
  currentToken: string,
  currentRefreshToken: string,
  requestedToken: string,
  requestedRefreshToken: string
): boolean {
  return currentToken === requestedToken && currentRefreshToken === requestedRefreshToken
}
