<template>
  <v-col :cols="cols">
    <v-row no-gutters align="center" @keyup.enter="emitSearch">
      <v-col cols="4">
        <v-text-field
          :model-value="keyword"
          clearable
          hide-details
          density="comfortable"
          class="searchInput mt-1"
          label="装箱任务号、商品、SKU、FNSKU"
          variant="solo"
          @update:model-value="onKeywordChange"
        />
      </v-col>
      <v-col cols="3">
        <v-select
          :model-value="groupId"
          :items="groupOptions"
          item-title="name"
          item-value="id"
          clearable
          hide-details
          density="comfortable"
          class="searchInput ml-2 mt-1"
          label="小组"
          variant="solo"
          @update:model-value="onGroupChange"
        />
      </v-col>
      <v-col cols="3">
        <v-autocomplete
          :model-value="memberId"
          :items="filteredMemberOptions"
          :item-title="memberOptionTitle"
          item-value="user_id"
          clearable
          hide-details
          density="comfortable"
          class="searchInput ml-2 mt-1"
          label="组员"
          variant="solo"
          :loading="memberOptionsLoading"
          @update:search="searchMembers"
          @update:model-value="onMemberChange"
          @click:clear="clearMember"
        />
      </v-col>
      <v-col cols="2" class="searchBtnCol">
        <v-btn color="primary" prepend-icon="mdi-magnify" @click="emitSearch">搜索</v-btn>
      </v-col>
    </v-row>
  </v-col>
</template>

<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref } from 'vue'
import { getOperatorGroupOptions, getOperatorMemberOptions } from '@/api/base/warehouseSetting'
import { DEBOUNCE_TIME } from '@/constant/system'
import type { OperatorGroupMemberOptionVO, OperatorGroupOptionVO } from '@/types/Base/Warehouse'

const props = withDefaults(defineProps<{
  keyword: string
  groupId: number | null
  memberId: number | null
  cols?: string | number
}>(), { cols: 8 })

const emit = defineEmits<{
  'update:keyword': [value: string]
  'update:groupId': [value: number | null]
  'update:memberId': [value: number | null]
  search: []
}>()

const groupOptions = ref<OperatorGroupOptionVO[]>([])
const memberOptions = ref<OperatorGroupMemberOptionVO[]>([])
const memberOptionsLoading = ref(false)
let memberSearchTimer: ReturnType<typeof setTimeout> | null = null
let keywordSearchTimer: ReturnType<typeof setTimeout> | null = null
let memberSearchRequestId = 0

const filteredMemberOptions = computed(() => {
  if (!props.groupId) return memberOptions.value
  return memberOptions.value.filter((member) => member.group_id === props.groupId)
})

const memberOptionTitle = (item: OperatorGroupMemberOptionVO): string =>
  item ? `${item.group_name}/${item.member_name}` : ''

const emitSearch = (): void => {
  if (keywordSearchTimer) clearTimeout(keywordSearchTimer)
  keywordSearchTimer = null
  emit('search')
}

const onKeywordChange = (value: unknown): void => {
  emit('update:keyword', String(value ?? ''))
  if (keywordSearchTimer) clearTimeout(keywordSearchTimer)
  keywordSearchTimer = setTimeout(() => {
    keywordSearchTimer = null
    emit('search')
  }, DEBOUNCE_TIME)
}

const onGroupChange = (groupId: number | null): void => {
  emit('update:groupId', groupId || null)
  emit('update:memberId', null)
  emitSearch()
}

const onMemberChange = (memberId: number | null): void => {
  if (!memberId) return
  const member = memberOptions.value.find((item) => item.user_id === memberId)
  if (member) emit('update:groupId', member.group_id)
  emit('update:memberId', memberId)
  emitSearch()
}

const clearMember = (): void => {
  emit('update:memberId', null)
  emitSearch()
}

const searchMembers = (keyword: string | null): void => {
  if (memberSearchTimer) clearTimeout(memberSearchTimer)
  const requestId = ++memberSearchRequestId
  memberOptionsLoading.value = true
  memberSearchTimer = setTimeout(async () => {
    memberSearchTimer = null
    try {
      const { data: result } = await getOperatorMemberOptions(keyword?.trim() ?? '')
      if (requestId === memberSearchRequestId && result.isSuccess) memberOptions.value = result.data
    } catch {
      if (requestId === memberSearchRequestId) memberOptions.value = []
    } finally {
      if (requestId === memberSearchRequestId) memberOptionsLoading.value = false
    }
  }, DEBOUNCE_TIME)
}

onMounted(async () => {
  try {
    const { data: result } = await getOperatorGroupOptions()
    if (result.isSuccess) groupOptions.value = result.data
  } catch {
    groupOptions.value = []
  }
})

onBeforeUnmount(() => {
  if (memberSearchTimer) clearTimeout(memberSearchTimer)
  if (keywordSearchTimer) clearTimeout(keywordSearchTimer)
  memberSearchRequestId += 1
})
</script>

<style scoped lang="less">
.searchBtnCol { display: flex; justify-content: flex-end; align-items: center; padding-top: 4px; }
</style>
