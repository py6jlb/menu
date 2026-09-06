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
    <div class="page-heading">
      <h2>Семья</h2>
    </div>

    <p v-if="loading" class="loading">Загрузка…</p>
    <p v-else-if="error" class="error">{{ error }}</p>

    <div v-else-if="!family" class="no-family-grid">
      <div class="card option-card">
        <div class="option-icon">👨‍👩‍👧</div>
        <h3>Создать семью</h3>
        <p class="option-desc">Создайте новую семью и делитесь меню с близкими.</p>
        <form @submit.prevent="onCreate" class="option-form">
          <label class="field">
            <span>Название семьи</span>
            <input v-model="newFamilyName" type="text" required />
          </label>
          <button type="submit" class="btn btn--primary btn--block" :disabled="pending">Создать семью</button>
        </form>
      </div>

      <div class="card option-card">
        <div class="option-icon">🔑</div>
        <h3>Присоединиться по коду</h3>
        <p class="option-desc">Есть код от семьи? Введите его, чтобы присоединиться.</p>
        <form @submit.prevent="onJoin" class="option-form">
          <label class="field">
            <span>Инвайт-код</span>
            <input v-model="joinCode" type="text" autocomplete="off" required placeholder="Например, ABC123" />
          </label>
          <button type="submit" class="btn btn--primary btn--block" :disabled="pending">Присоединиться</button>
        </form>
      </div>
    </div>

    <div v-else class="card family-card">
      <div class="family-header">
        <h3 class="family-name">{{ family.name }}</h3>
        <span v-if="isOwner()" class="badge badge--owner">Вы владелец</span>
      </div>

      <div class="invite-block">
        <span class="invite-label">Инвайт-код для приглашения</span>
        <div class="invite-code">«{{ family.inviteCode }}»</div>
        <button type="button" class="btn btn--primary" @click="onCopyCode">
          {{ copied ? 'Скопировано!' : 'Копировать' }}
        </button>
        <button v-if="isOwner()" type="button" class="btn btn--ghost" @click="onRegenerate">Обновить код</button>
      </div>

      <h4 class="members-title">Участники</h4>
      <ul class="members">
        <li v-for="member in family.members" :key="member.id" class="member">
          <span class="member-email">{{ member.email }}</span>
          <span class="badge" :class="member.role === 'Owner' ? 'badge--owner' : 'badge--member'">
            {{ member.role === 'Owner' ? 'Владелец' : 'Участник' }}
          </span>
          <button
            v-if="isOwner() && member.id !== family.ownerId"
            type="button"
            class="btn btn--ghost btn--small"
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
.no-family-grid {
  display: grid;
  grid-template-columns: 1fr;
  gap: 1rem;
  max-width: 760px;
}

@media (min-width: 700px) {
  .no-family-grid {
    grid-template-columns: 1fr 1fr;
  }
}

.option-card {
  display: flex;
  flex-direction: column;
}

.option-icon {
  font-size: 2rem;
  line-height: 1;
}

.option-card h3 {
  margin: 0.5rem 0 0.25rem;
}

.option-desc {
  color: var(--text-soft);
  font-size: 0.9rem;
  margin: 0 0 1rem;
}

.option-form {
  display: flex;
  flex-direction: column;
  gap: 0.75rem;
  margin-top: auto;
}

.family-card {
  max-width: 640px;
}

.family-header {
  display: flex;
  align-items: center;
  gap: 0.75rem;
  flex-wrap: wrap;
  margin-bottom: 1rem;
}

.family-name {
  margin: 0;
}

.badge--owner {
  background: var(--primary-soft);
  color: var(--primary);
}

.badge--member {
  background: var(--surface-2);
  color: var(--text-soft);
}

.invite-block {
  display: flex;
  flex-direction: column;
  gap: 0.75rem;
  align-items: flex-start;
  padding: 1.25rem;
  border: 1px dashed var(--primary);
  border-radius: var(--radius-sm);
  background: var(--primary-soft);
  margin-bottom: 1.5rem;
}

.invite-label {
  font-size: 0.85rem;
  color: var(--text-soft);
  font-weight: 600;
}

.invite-code {
  font-size: 1.6rem;
  font-weight: 800;
  letter-spacing: 0.08em;
  color: var(--primary);
  word-break: break-all;
}

.members-title {
  margin: 0 0 0.5rem;
}

.members {
  list-style: none;
  padding: 0;
  margin: 0;
}

.member {
  display: flex;
  align-items: center;
  gap: 0.75rem;
  flex-wrap: wrap;
  padding: 0.7rem 0;
  border-bottom: 1px solid var(--border);
}

.member:last-child {
  border-bottom: none;
}

.member-email {
  font-weight: 600;
  flex: 1;
  min-width: 0;
  word-break: break-word;
}
</style>
