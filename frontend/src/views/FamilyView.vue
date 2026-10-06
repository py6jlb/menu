<script setup>
import { ref, onMounted } from 'vue'
import { useFamily } from '../composables/useFamily'
import { useAuth } from '../stores/auth'

const { state, isEmailVerified } = useAuth()

const {
  family,
  loading,
  loadError,
  createError,
  joinError,
  copyError,
  regenerateError,
  removeError,
  creating,
  joining,
  regenerating,
  removing,
  copied,
  load,
  create,
  join,
  copyInviteCode,
  regenerate,
  removeMember
} = useFamily()

const newFamilyName = ref('')
const joinCode = ref('')

function isOwner() {
  return family.value && family.value.ownerId === state.user?.id
}

async function onRemoveMember(member) {
  if (!window.confirm(`Удалить участника ${member.email} из семьи?`)) return
  await removeMember(member)
}

onMounted(load)
</script>

<template>
  <section>
    <div class="page-heading">
      <h2>Семья</h2>
    </div>

    <p v-if="!isEmailVerified" class="notice">
      Подтвердите почту, чтобы создавать семью, присоединяться по коду и управлять участниками.
      <router-link to="/verify">Ввести код</router-link>
    </p>

    <p v-if="loading" class="loading">Загрузка…</p>

    <div v-else-if="loadError" class="load-recovery">
      <p class="error">{{ loadError }}</p>
      <button type="button" class="btn btn--ghost" @click="load">Повторить</button>
    </div>

    <div v-else-if="!family" class="no-family-grid">
      <div class="card option-card">
        <div class="option-icon">👨‍👩‍👧</div>
        <h3>Создать семью</h3>
        <p class="option-desc">Создайте новую семью и делитесь меню с близкими.</p>
        <form @submit.prevent="create(newFamilyName)" class="option-form">
          <label class="field">
            <span>Название семьи</span>
            <input v-model="newFamilyName" type="text" required />
          </label>
          <p v-if="createError" class="error">{{ createError }}</p>
          <button type="submit" class="btn btn--primary btn--block" :disabled="creating || !isEmailVerified">Создать семью</button>
        </form>
      </div>

      <div class="card option-card">
        <div class="option-icon">🔑</div>
        <h3>Присоединиться по коду</h3>
        <p class="option-desc">Есть код от семьи? Введите его, чтобы присоединиться.</p>
        <form @submit.prevent="join(joinCode)" class="option-form">
          <label class="field">
            <span>Инвайт-код</span>
            <input v-model="joinCode" type="text" autocomplete="off" required placeholder="Например, ABC123" />
          </label>
          <p v-if="joinError" class="error">{{ joinError }}</p>
          <button type="submit" class="btn btn--primary btn--block" :disabled="joining || !isEmailVerified">Присоединиться</button>
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
        <button type="button" class="btn btn--primary" @click="copyInviteCode">
          {{ copied ? 'Скопировано!' : 'Копировать' }}
        </button>
        <button v-if="isOwner() && isEmailVerified" type="button" class="btn btn--ghost" :disabled="regenerating" @click="regenerate">Обновить код</button>
        <p v-if="copyError" class="error">{{ copyError }}</p>
        <p v-if="regenerateError" class="error">{{ regenerateError }}</p>
      </div>

      <h4 class="members-title">Участники</h4>
      <p v-if="removeError" class="error">{{ removeError }}</p>
      <ul class="members">
        <li v-for="member in family.members" :key="member.id" class="member">
          <span class="member-email">{{ member.email }}</span>
          <span class="badge" :class="member.role === 'Owner' ? 'badge--owner' : 'badge--member'">
            {{ member.role === 'Owner' ? 'Владелец' : 'Участник' }}
          </span>
          <button
            v-if="isOwner() && isEmailVerified && member.id !== family.ownerId"
            type="button"
            class="btn btn--ghost btn--small"
            :disabled="removing"
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

.load-recovery {
  display: flex;
  flex-direction: column;
  align-items: flex-start;
  gap: 0.75rem;
  max-width: 640px;
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
