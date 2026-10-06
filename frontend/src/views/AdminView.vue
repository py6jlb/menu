<script setup>
import { ref } from 'vue'
import { unlockUser } from '../api/admin'
import { rateLimitMessage } from '../composables/useCooldown'

const email = ref('')
const pending = ref(false)
const message = ref('')
const error = ref('')

async function submit() {
  message.value = ''
  error.value = ''
  pending.value = true
  try {
    const { response, data } = await unlockUser(email.value.trim())
    if (response.status === 200) {
      message.value = data?.message || 'Пользователь разблокирован.'
      email.value = ''
    } else if (response.status === 404) {
      error.value = data?.error || 'Пользователь не найден.'
    } else if (response.status === 429) {
      error.value = rateLimitMessage(response, data)
    } else if (response.status === 403) {
      error.value = 'Недостаточно прав для этого действия.'
    } else {
      error.value = data?.error || 'Не удалось разблокировать пользователя.'
    }
  } catch (err) {
    error.value = err.message || 'Сервер недоступен.'
  } finally {
    pending.value = false
  }
}
</script>

<template>
  <section>
    <div class="page-heading">
      <h2>Разблокировка пользователя</h2>
    </div>

    <div class="card admin-card">
      <p class="hint">
        Если пользователь исчерпал попытки ввода кода подтверждения или сброса пароля,
        снимите блокировку по его email. Счётчик неверных попыток будет сброшен.
      </p>

      <form @submit.prevent="submit" class="admin-form">
        <label class="field">
          <span>Email пользователя</span>
          <input v-model="email" type="email" autocomplete="off" required />
        </label>
        <p v-if="message" class="success" role="status">{{ message }}</p>
        <p v-else-if="error" class="error" role="alert">{{ error }}</p>
        <button type="submit" class="btn btn--primary" :disabled="pending || !email.trim()">
          {{ pending ? 'Разблокировка…' : 'Разблокировать' }}
        </button>
      </form>

      <p class="hint">Не более 10 запросов в минуту.</p>
    </div>
  </section>
</template>

<style scoped>
.admin-card {
  max-width: 520px;
  display: flex;
  flex-direction: column;
  gap: 1rem;
}

.admin-form {
  display: flex;
  flex-direction: column;
  gap: 1rem;
}
</style>
