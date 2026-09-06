<script setup>
import { ref } from 'vue'
import { useRouter } from 'vue-router'
import { useAuth } from '../stores/auth'
import { apiJson } from '../api/client'

const router = useRouter()
const { setSession } = useAuth()

const email = ref('')
const password = ref('')
const error = ref('')
const pending = ref(false)

async function submit() {
  error.value = ''
  pending.value = true
  try {
    const { response, data } = await apiJson('/api/auth/register', {
      method: 'POST',
      body: JSON.stringify({ email: email.value, password: password.value })
    })
    if (response.status === 201) {
      setSession(data.token, data.user)
      router.push({ name: 'home' })
    } else if (response.status === 409) {
      error.value = data?.error || 'Этот email уже зарегистрирован.'
    } else {
      error.value = data?.error || 'Не удалось зарегистрироваться.'
    }
  } catch (err) {
    error.value = err.message || 'Сервер недоступен.'
  } finally {
    pending.value = false
  }
}
</script>

<template>
  <section class="card">
    <h2>Регистрация</h2>
    <form @submit.prevent="submit">
      <label>
        Email
        <input v-model="email" type="email" autocomplete="email" required />
      </label>
      <label>
        Пароль
        <input
          v-model="password"
          type="password"
          autocomplete="new-password"
          minlength="6"
          required
        />
      </label>
      <p v-if="error" class="error">{{ error }}</p>
      <button type="submit" :disabled="pending">
        {{ pending ? 'Регистрация…' : 'Зарегистрироваться' }}
      </button>
    </form>
    <p>Уже есть аккаунт? <router-link to="/login">Войдите</router-link></p>
  </section>
</template>

<style scoped>
label {
  display: flex;
  flex-direction: column;
  gap: 0.25rem;
  font-size: 0.9rem;
}
</style>
