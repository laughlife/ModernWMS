import axios from 'axios' // 引入axios
import { pinia } from '@/store'
import { useSystemStore } from '@/store/module/system'
import { useUserStore } from '@/store/module/user'
import { emitter } from '@/utils/bus'
import { router } from '@/router'
import { hookComponent } from '@/components/system'
import i18n from '@/languages/i18n'
import { buildServerUrl } from './serverUrl'
import { isRefreshResponseCurrent } from './refreshSession'

// Basis of axios
const SERVER_URL = buildServerUrl(import.meta.env.VITE_BASE_PATH, import.meta.env.VITE_SERVER_PORT)
axios.defaults.baseURL = SERVER_URL
const http = axios.create({
  baseURL: SERVER_URL,
  timeout: 10000
})

type RefreshSubscriber = {
  resolve: (token: string) => void
  reject: () => void
}

// Requests suspended while the access token is being refreshed.
let subscribesArr: RefreshSubscriber[] = []

// The count of current request
let acitveAxios = 0

function pushSubscribeInterface(subscriber: RefreshSubscriber) {
  subscribesArr.push(subscriber)
}

function reloadSubscribesWithNewToken(token: string) {
  const subscribers = subscribesArr.splice(0)
  subscribers.forEach((subscriber) => subscriber.resolve(token))
}

function rejectSubscribes() {
  const subscribers = subscribesArr.splice(0)
  subscribers.forEach((subscriber) => subscriber.reject())
}

/**
 * expired or will expired
 * @returns {boolean}
 */
function isTokenExpired() {
  const expiredTime = useUserStore(pinia).expirationTime
  if (expiredTime) {
    // Distance and x seconds is judged to be due
    const willExpiredSecond = 10 * 60
    const nowTime = new Date().getTime()
    const willExpired = (expiredTime - nowTime) / 1000 < willExpiredSecond

    return willExpired
  }
  return false
}

function rediretToLogin() {
  const systemStore = useSystemStore(pinia)
  useUserStore(pinia).clearSession()
  systemStore.clearOpenedMenu()
  systemStore.setCurrentRouterPath('')

  clearLoading() // Clear all loads

  router.push('/login')
}

const showLoading = (config: any) => {
  if (config.hideLoading || config.loadingTracked) return
  config.loadingTracked = true
  acitveAxios++
  if (acitveAxios === 1) {
    emitter.emit('showLoading')
  }
}

const closeLoading = (config?: any) => {
  if (!config?.loadingTracked) return
  config.loadingTracked = false
  acitveAxios = Math.max(0, acitveAxios - 1)
  if (acitveAxios === 0) {
    emitter.emit('closeLoading')
  }
}

const clearLoading = () => {
  acitveAxios = 0
  emitter.emit('closeLoading')
}

const handleRefreshToken = (token: string) => {
  const userStore = useUserStore(pinia)
  const refreshToken = userStore.refreshToken
  userStore.setIsRefreshingToken(true)
  axios
    .post('/refresh-token', {
      accessToken: token,
      refreshToken
    })
    .then(({ data: res }) => {
      if (!isRefreshResponseCurrent(userStore.token, userStore.refreshToken, token, refreshToken)) {
        rejectSubscribes()
        return
      }
      if (res.isSuccess) {
        const tokenVo = res.data
        const expiredTime = new Date().getTime() + userStore.effectiveMinutes * 60 * 1000

        userStore.setToken(tokenVo)
        userStore.setExpirationTime(expiredTime)

        // With the new token request those suspended interface
        reloadSubscribesWithNewToken(tokenVo)
      } else {
        return Promise.reject()
      }
    })
    .catch(() => {
      rejectSubscribes()
      rediretToLogin()
    })
    .finally(() => {
      userStore.setIsRefreshingToken(false)
    })
}

http.interceptors.request.use(
  (config: any) => {
    const userStore = useUserStore(pinia)
    const donNeedTokenApi = ['/login', '/user/register']
    const token = userStore.token

    config.params ? (config.params.culture = 'zh-cn') : (config.params = { culture: 'zh-cn' })

    showLoading(config)

    // It don't need token to request with some apis.
    if (donNeedTokenApi.includes(config.url)) {
      return config
    }

    // 1.Logout when token isn't exist
    if (!token) {
      rediretToLogin()
      return config
    }

    // 2.Request normally when token is exist and in valid date
    if (!isTokenExpired() || config.url === '/refresh-token') {
      const defaultHeaders = config.data instanceof FormData
        ? {}
        : { 'Content-Type': 'application/json' }
      config.headers = {
        ...defaultHeaders,
        ...config.headers
      }
      if (config.url && !donNeedTokenApi.includes(config.url)) {
        config.headers.Authorization = `Bearer ${ token }`
      }

      return config
    }

    // 3.Take a 'refresh token' request when it not in the refreshing.
    if (!userStore.isRefreshingToken) {
      handleRefreshToken(token)
    }

    // 4.Put the fail requests up and initiate them after refresh token
    const retry = new Promise((resolve, reject) => {
      pushSubscribeInterface({
        resolve: (newToken: string) => {
          config.headers.Authorization = `Bearer ${ newToken }`
          resolve(config)
        },
        reject: () => {
          const error = Object.assign(new Error('登录状态已失效'), {
            config,
            refreshCancelled: true
          })
          reject(error)
        }
      })
    })
    return retry
  },
  (error) => {
    closeLoading(error?.config)
    return Promise.reject(error)
  }
)

// 中文说明：历史接口的返回约定不统一，本拦截器可能返回 AxiosResponse 或业务响应体。
// 新增 API 不得在组件中猜测包装层；需要 ResultModel 时统一通过 apiResult.ts 解包并声明返回类型。
http.interceptors.response.use(
  (response) => {
    closeLoading(response.config)
    if (response.data.code === 0 || response.headers.success === 'true') {
      if (response.headers.msg) {
        response.data.msg = decodeURI(response.headers.msg)
      }
      return response.data
    }
    return response.data.msg ? response.data : response
  },
  (error) => {
    closeLoading(error?.config)
    if (error?.refreshCancelled) {
      return Promise.reject(error)
    }
    // 1.There isn't 'error.response' object when request timeout
    if (!error.response) {
      hookComponent.$message({
        type: 'error',
        content: i18n.global.t('system.tips.requestTimeout')
      })
      return
    }

    // 2.There is response status when request fail but not timeout
    switch (error.response.status) {
      case 500:
        console.error('error：', 500)
        break
      case 404:
        console.error('error：', 404)
        break
    }

    hookComponent.$message({
      type: 'error',
      content: i18n.global.t('system.tips.requestFail')
    })
    return error
  }
)

export default http
