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

// The interface array request failed
let subscribesArr: Array<any> = []

// The count of current request
let acitveAxios = 0

function pushSubscribeInterface(cb: any) {
  subscribesArr.push(cb)
}

function reloadSubscribesWithNewToken(token: string) {
  subscribesArr.map((cb) => cb(token))
}

function resetSubscribes() {
  subscribesArr = []
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

const showLoading = () => {
  acitveAxios++
  if (acitveAxios > 0) {
    emitter.emit('showLoading')
  }
}

const closeLoading = () => {
  acitveAxios--
  if (acitveAxios <= 0) {
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
        resetSubscribes()
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
      resetSubscribes()
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

    if (!config.hideLoading) {
      showLoading()
    }

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
      config.headers = {
        'Content-Type': 'application/json',
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
    const retry = new Promise((resolve) => {
      pushSubscribeInterface((newToken: string) => {
        config.headers.Authorization = `Bearer ${ newToken }`
        resolve(config)
      })
    })
    return retry
  },
  (error) => {
    closeLoading()
    return error
  }
)

http.interceptors.response.use(
  (response) => {
    closeLoading()
    if (response.data.code === 0 || response.headers.success === 'true') {
      if (response.headers.msg) {
        response.data.msg = decodeURI(response.headers.msg)
      }
      return response.data
    }
    return response.data.msg ? response.data : response
  },
  (error) => {
    closeLoading()
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
