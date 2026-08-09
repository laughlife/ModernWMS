import type { AxiosResponse } from 'axios'
import { describe, expect, it } from 'vitest'
import type { ApiResult } from '@/types/System/ApiResult'
import { unwrapApiResult } from './apiResult'

const successResult: ApiResult<{ path: string }> = {
  isSuccess: true,
  code: 200,
  errorMessage: '',
  data: { path: 'modernwms/test.png' }
}

describe('unwrapApiResult', () => {
  it('returns a business result that was already unwrapped', () => {
    expect(unwrapApiResult(successResult)).toBe(successResult)
  })

  it('extracts a business result from an Axios response', () => {
    const response = { data: successResult } as AxiosResponse<ApiResult<{ path: string }>>
    expect(unwrapApiResult(response)).toBe(successResult)
  })

  it('preserves a business failure returned with HTTP 200', () => {
    const failureResult: ApiResult<unknown> = {
      isSuccess: false,
      code: 400,
      errorMessage: '上传失败',
      data: null
    }
    const response = { data: failureResult } as AxiosResponse<ApiResult<unknown>>
    expect(unwrapApiResult(response)).toBe(failureResult)
  })

  it('rejects an invalid response shape instead of reporting a false business failure', () => {
    expect(() => unwrapApiResult({ data: {} } as AxiosResponse<ApiResult<unknown>>))
      .toThrow('接口响应格式无效')
  })
})
