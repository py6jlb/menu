<script setup>
import { ref, onMounted } from 'vue'
import { getMyFamily, createFamily, joinFamily, regenerateInviteCode, removeMember } from '../api/families'
import { useAuth } from '../stores/auth'

const { state } = useAuth()

const family = ref(null)
const loading = ref(true)
const error = ref('')

const newFamilyName = ref('')
const joinCode = ref('')
const pending = ref(false)
const copied = ref(false)

async function load() {
  loading.value = true
  error.value = ''
  const { response, data } = await getMyFamily()
  if (response.status === 404) {
    family.value = null
  } else if (response.status === 200) {
    family.value = data
  } else {
    error.value = data?.error || 'Не удалось загрузить семью.'
  }
  loading.value = false
}

function isOwner() {
  return family.value && family.value.ownerId === state.user?.id
}

async function onCreate() {
  error.value = ''
  pending.value = true
  try {
    const { response, data } = await createFamily(newFamilyName.value.trim())
    if (response.status === 201) {
      family.value = data
    } else if (response.status === 409) {
      error.value = data?.error || 'Вы уже состоите в семье.'
    } else {
      error.value = data?.error || 'Не удалось создать семью.'
    }
  } catch (err) {
    error.value = err.message || 'Сервер недоступен.'
  } finally {
    pending.value = false
  }
}

async function onJoin() {
  error.value = ''
  pending.value = true
  try {
    const { response, data } = await joinFamily(joinCode.value.trim())
    if (response.status === 200) {
      family.value = data
    } else if (response.status === 404) {
      error.value = data?.error || 'Семья по такому коду не найдена.'
    } else if (response.status === 409) {
      error.value = data?.error || 'Вы уже состоите в семье.'
    } else {
      error.value = data?.error || 'Не удалось присоединиться.'
    }
  } catch (err) {
    error.value = err.message || 'Сервер недоступен.'
  } finally {
    pending.value = false
  }
}

async function onCopyCode() {
  try {
    await navigator.clipboard.writeText(family.value.inviteCode)
    copied.value = true
    setTimeout(() => (copied.value = false), 1500)
  } catch {
    error.value = 'Не удалось скопировать код.'
  }
}

async function onRegenerate() {
  error.value = ''
  const { response, data } = await regenerateInviteCode(family.value.id)
  if (response.status === 200) {
    family.value = { ...family.value, inviteCode: data.inviteCode }
  } else {
    error.value = data?.error || 'Не удалось обновить код.'
  }
}

async function onRemoveMember(member) {
  error.value = ''
  if (!window.confirm(`Удалить участника ${member.email} из семьи?`)) return
  const { response } = await removeMember(family.value.id, member.id)
  if (response.status === 204) {
    family.value = {
      ...family.value,
      members: family.value.members.filter((m) => m.id !== member.id)
    }
  } else {
    error.value = 'Не удалось удалить участника.'
  }
}

onMounted(load)
</script>

<template>
  <section>
    <h2>Семья</h2>

    <p v-if="loading">Загрузка…</p>
    <p v-else-if="error" class="error">{{ error }}</p>

    <div v-else-if="!family" class="card">
      <h3>Создать семью</h3>
      <form @submit.prevent="onCreate">
        <label>
          Название семьи
          <input v-model="newFamilyName" type="text" required />
        </label>
        <button type="submit" :disabled="pending">Создать семью</button>
      </form>

      <hr />

      <h3>Присоединиться по коду</h3>
      <form @submit.prevent="onJoin">
        <label>
          Инвайт-код
          <input v-model="joinCode" type="text" autocomplete="off" required />
        </label>
        <button type="submit" :disabled="pending">Присоединиться</button>
      </form>
    </div>

    <div v-else class="card">
      <h3>{{ family.name }}</h3>

      <div class="invite">
        <strong>Инвайт-код:</strong>
        <code>{{ family.inviteCode }}</code>
        <button type="button" @click="onCopyCode">{{ copied ? 'Скопировано!' : 'Копировать' }}</button>
      </div>

      <button v-if="isOwner()" type="button" @click="onRegenerate">Обновить код</button>

      <h4>Участники</h4>
      <ul class="members">
        <li v-for="member in family.members" :key="member.id" class="member">
          <span>{{ member.email }}</span>
          <span class="role-badge">{{ member.role === 'Owner' ? 'Владелец' : 'Участник' }}</span>
          <button
            v-if="isOwner() && member.id !== family.ownerId"
            type="button"
            class="danger"
            @click="onRemoveMember(member)"
          >
            Удалить
          </button>
        </li>
      </ul>
    </div>
  </section>
</template>

<style scoped>
.invite {
  display: flex;
  align-items: center;
  gap: 0.75rem;
  margin: 1rem 0;
}

.members {
  list-style: none;
  padding: 0;
  margin: 0.5rem 0 0;
}

.member {
  display: flex;
  align-items: center;
  gap: 0.75rem;
  padding: 0.5rem 0;
  border-bottom: 1px solid #eee;
}

.role-badge {
  background: #eef2ff;
  border-radius: 999px;
  padding: 0.1rem 0.6rem;
  font-size: 0.8rem;
  color: #3730a3;
}

.danger {
  margin-left: auto;
  color: #b91c1c;
  background: none;
  border: 1px solid #b91c1c;
  border-radius: 6px;
  padding: 0.25rem 0.6rem;
}

hr {
  border: none;
  border-top: 1px solid #e5e5e5;
  margin: 1.25rem 0;
}
</style>
