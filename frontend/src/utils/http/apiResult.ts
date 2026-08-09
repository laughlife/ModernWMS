import type { AxiosResponse } from 'axios'
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
  response: AxiosResponse<ApiResult<T>> | ApiResult<T>
): ApiResult<T> => {
  if (isApiResult<T>(response)) return response
  if (isApiResult<T>(response.data)) return response.data
  throw new Error('接口响应格式无效')
}
