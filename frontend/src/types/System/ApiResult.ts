interface ApiResultBase {
  code: number
  errorMessage: string
}

export type ApiResult<T> =
  | (ApiResultBase & {
      isSuccess: true
      data: T
    })
  | (ApiResultBase & {
      isSuccess: false
      data: T | null
    })
