<template>
  <div class="languageIcon">
    <v-menu>
      <template #activator="{ props }">
        <!-- <v-icon icon="mdi-translate" color="#666666" v-bind="props"></v-icon> -->
        <v-icon icon="mdi-web" color="#666666" v-bind="props"></v-icon>
      </template>
      <v-list>
        <v-list-item v-for="(item, index) in data.languageList" :key="index" :value="index" @click="method.changeLanguage(item.value)">
          <v-list-item-title>{{ item.title }}</v-list-item-title>
        </v-list-item>
      </v-list>
    </v-menu>
  </div>
</template>

<script lang="ts" setup>
import { reactive } from 'vue'
import { useI18n } from 'vue-i18n'
import { useLocale } from 'vuetify'
import { getSelectedLang } from '@/languages/method/index'
import { getSelcectedLangForVuetify } from '@/plugins/vuetify/method/index'
import { useSystemStore } from '@/store/module/system'
// import { router } from '@/router'

const { locale } = useI18n()
const { current } = useLocale()
const systemStore = useSystemStore()

const data = reactive({
  showLanguage: false,
  languageList: [
    { title: '简体中文', value: 'zh' },
    { title: '繁體中文', value: 'tw' },
    { title: 'English', value: 'en' }
  ]
})

const method = reactive({
  changeLanguage: (lang: string) => {
    if (systemStore.language === lang) {
      return
    }
    systemStore.setLanguage(lang)
    locale.value = getSelectedLang(lang) // global
    current.value = getSelcectedLangForVuetify(lang) // vuetify

    // if (!['/', '/login'].includes(router.currentRoute.value.path)) {
    systemStore.setRefreshFlag(true)
    // }
  }
})
</script>

<style lang="less" scoped>
.languageIcon {
  height: 40px;
  width: 40px;
  font-size: 16px;
  display: flex;
  justify-content: center;
  align-items: center;
  transition: all 0.5s;
  border-radius: 40px;
  &:hover {
    background-color: #e4e5ea;
  }
  &:active {
    background-color: #cdced3;
  }
}
</style>
