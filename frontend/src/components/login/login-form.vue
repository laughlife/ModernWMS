<template>
  <div class="loginForm">
    <div class="titleText">
      <h2>欢迎登录</h2>
      <p>南阳有座山物流管理系统</p>
    </div>
    <div class="formContainer">
      <v-form ref="VFormRef" v-model="data.valid" lazy-validation @keydown.enter.prevent="method.login()">
        <v-text-field
          v-model="data.userName"
          required
          :rules="data.userNameVaildRules"
          :label="$t('login.userName')"
          prepend-inner-icon="mdi-account-outline"
          variant="outlined"
          density="comfortable"
        ></v-text-field>
        <v-text-field
          v-model="data.password"
          required
          :rules="data.passwordVaildRules"
          autocomplete="current-password"
          :append-inner-icon="data.showPassword ? 'mdi-eye' : 'mdi-eye-off'"
          :type="data.showPassword ? 'text' : 'password'"
          :label="$t('login.password')"
          prepend-inner-icon="mdi-lock-outline"
          variant="outlined"
          density="comfortable"
          @click:append-inner="method.handleShowPassword()"
        ></v-text-field>
        <v-checkbox v-model="data.remember" :label="$t('login.rememberTips')" color="primary"></v-checkbox>
        <v-btn
          data-testid="login-submit"
          color="#1769e8"
          class="loginBtn"
          elevation="0"
          @click="method.login()"
        >
          {{ $t('login.mainButtonLabel') }}
        </v-btn>
      </v-form>
    </div>
  </div>
</template>

<script lang="ts" setup>
import { reactive, ref, onMounted } from 'vue'
import { Md5 } from 'ts-md5'
import i18n from '@/languages/i18n'
import { login, getUserAuthority } from '@/api/sys/login'
import { useSystemStore } from '@/store/module/system'
import { useUserStore } from '@/store/module/user'
import { hookComponent } from '@/components/system'
import { loadRouter, router } from '@/router/index'
// import userRegisterForm from './user-register-form.vue'

// 加解密算法
function simpleEncrypt(text: string, key: string) {
  let encrypted = ''
  for (let i = 0; i < text.length; i++) {
    encrypted += String.fromCharCode(text.charCodeAt(i) ^ key.charCodeAt(i % key.length))
  }
  return encrypted
}

function simpleDecrypt(encryptedText: string, key: string) {
  return simpleEncrypt(encryptedText, key) // 因为异或运算具有对称性，加密和解密过程相同
}

// Get v-form ref
const VFormRef = ref()
const systemStore = useSystemStore()
const userStore = useUserStore()

const data = reactive({
  showDialog: false,
  valid: true,
  showPassword: false,
  userName: 'admin', // 240507 刘福: 默认账号 admin 1
  password: '1',
  remember: false,
  dialogForm: {
    id: 0,
    user_num: '',
    user_name: '',
    auth_string: '',
    email: '',
    // sex: '',
    is_valid: true
  },
  userNameVaildRules: [(v: string) => !!v || `${ i18n.global.t('system.checkText.mustInput') }${ i18n.global.t('login.userName') }!`],
  passwordVaildRules: [(v: string) => !!v || `${ i18n.global.t('system.checkText.mustInput') }${ i18n.global.t('login.password') }!`],
  encryption: 'ModernWMS2024'
})

const method = reactive({
  handleShowPassword: () => {
    data.showPassword = !data.showPassword
  },
  login: async () => {
    const { valid } = await VFormRef.value.validate()
    if (!valid) {
      return
    }
    const { data: loginRes } = await login({
      user_name: data.userName,
      password: Md5.hashStr(data.password)
    })

    if (loginRes.isSuccess) {
      const expiredTime = new Date().getTime() + loginRes.data.expire * 60 * 1000

      userStore.setToken(loginRes.data.access_token)
      userStore.setRefreshToken(loginRes.data.refresh_token)
      userStore.setExpirationTime(expiredTime)
      userStore.setEffectiveMinutes(loginRes.data.expire)
      userStore.setUserInfo(loginRes.data)

      const { data: authorityRes } = await getUserAuthority(loginRes.data.userrole_id)
      if (!authorityRes.isSuccess) {
        hookComponent.$message({
          type: 'error',
          content: authorityRes.errorMessage
        })
        return
      }
      if (authorityRes.data.length <= 0) {
        hookComponent.$message({
          type: 'error',
          content: i18n.global.t('login.notAuthority')
        })
        return
      }

      const authorityList = authorityRes.data

      // test
      // authorityList.push({
      //   id: 112999,
      //   menu_name: 'test',
      //   module: 'baseModule',
      //   vue_path: 'test',
      //   vue_path_detail: '',
      //   vue_directory: 'test/test',
      //   sort: 2
      // })

      userStore.setUserMenuList(authorityList)
      loadRouter()

      hookComponent.$message({
        type: 'success',
        content: i18n.global.t('login.loginSuccess')
      })

      // Remember user login info
      if (data.remember) {
        const rememberJSON = JSON.stringify({
          user_num: simpleEncrypt(data.userName, data.encryption),
          password: simpleEncrypt(data.password, data.encryption)
        })
        localStorage.setItem('userLoginInfo', rememberJSON)
      } else {
        localStorage.setItem('userLoginInfo', '')
      }

      // Jump home
      systemStore.setCurrentRouterPath('homepage')
      router.push('/home')
    } else {
      hookComponent.$message({
        type: 'error',
        content: loginRes.errorMessage
      })
    }
  },
  openRegisterDialog: () => {
    data.dialogForm = {
      id: 0,
      user_num: '',
      user_name: '',
      auth_string: '',
      email: '',
      // sex: '',
      is_valid: true
    }
    data.showDialog = true
  },
  // Shut add or update dialog
  closeDialog: () => {
    data.showDialog = false
  },
  // after Add or update success.
  saveSuccess: () => {
    method.closeDialog()
  }
})

onMounted(() => {
  // Get remember username and password
  const rememberJSON = localStorage.getItem('userLoginInfo')
  if (rememberJSON) {
    const obj = JSON.parse(rememberJSON)
    data.remember = true

    try {
      data.userName = simpleDecrypt(obj.user_num, data.encryption)
      data.password = simpleDecrypt(obj.password, data.encryption)
    } catch {
      // Compatible with old encrypted data
      try {
        data.userName = decodeURIComponent(window.atob(obj.userName))
        data.password = decodeURIComponent(window.atob(obj.password))
      } catch {
        // Compatible with old encrypted data
        try {
          data.userName = window.atob(obj.userName)
          data.password = window.atob(obj.password)
        } catch {
          data.userName = ''
          data.password = ''
          data.remember = false
        }
      }
    }
  }
  // 旧的加密数据
})
</script>

<style scoped lang="less">
.loginForm {
  width: 100%;
  box-sizing: border-box;
  padding: 50px 44px 42px;

  .titleText {
    margin-bottom: 38px;
    text-align: center;

    h2 {
      margin: 0;
      color: #17243d;
      font-size: 32px;
      font-weight: 700;
      line-height: 1.3;
      letter-spacing: 1px;
    }

    p {
      margin: 12px 0 0;
      color: #7b8597;
      font-size: 14px;
    }
  }

  .formContainer {
    padding: 0;

    .v-btn {
      width: 100%;
    }

    .v-text-field {
      margin-top: 12px;
    }

    .v-checkbox {
      height: 58px;
      margin-top: -4px;
      color: #6f7a8e;
    }
  }

  :deep(.v-messages) {
    color: #b00020 !important;
  }

  :deep(.v-field) {
    min-height: 60px;
    color: #7d8799;
    background: #fff;
    border-radius: 8px;
  }

  :deep(.v-field__outline) {
    color: #cbd4e2;
  }

  :deep(.v-field--focused .v-field__outline) {
    color: #1769e8;
  }

  :deep(.v-field__input) {
    color: #22304a;
    font-size: 15px;
  }

  :deep(.v-label) {
    color: #7d8799;
  }

  :deep(.v-selection-control__input > .v-icon) {
    font-size: 22px;
  }
}

.loginBtn {
  height: 54px;
  margin-top: 8px;
  border-radius: 7px;
  font-size: 17px;
  font-weight: 600;
  letter-spacing: 8px;
  box-shadow: 0 10px 22px rgba(23, 105, 232, 0.2) !important;
}

@media (max-width: 480px) {
  .loginForm {
    padding: 38px 24px 32px;

    .titleText {
      margin-bottom: 28px;

      h2 {
        font-size: 27px;
      }
    }

    :deep(.v-field) {
      min-height: 56px;
    }
  }
}
</style>
