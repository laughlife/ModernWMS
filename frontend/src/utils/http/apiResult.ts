import type { ApiResult } from '@/types/System/ApiResult'

const isApiResult = <T>(value: unknown): value is ApiResult<T> => {
  if (!value || typeof value !== 'object') return false
  const candidate = value as {
    isSuccess?: unknown
    code?: unknown
    errorMessage?: unknown
    data?: unknown
  }
  return typeof candidate.isSuccess === 'boolean'
    && typeof candidate.code === 'number'
    && typeof candidate.errorMessage === 'string'
    && 'data' in candidate
}

/**
 * 兼容当前拦截器可能返回 AxiosResponse 或直接返回业务响应的两种情况。
 * API 层统一调用后，Vue 组件不再感知 Axios 包装结构。
 */
export const unwrapApiResult = <T>(
  response: unknown
): ApiResult<T> => {
  if (isApiResult<T>(response)) return response
  const wrappedResponse = response as { data?: unknown } | null
  if (isApiResult<T>(wrappedResponse?.data)) return wrappedResponse.data
  throw new Error('接口响应格式无效')
}
