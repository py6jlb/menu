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
    const { response, data } = await apiJson('/api/auth/login', {
      method: 'POST',
      body: JSON.stringify({ email: email.value, password: password.value })
    })
    if (response.status === 200) {
      setSession(data.token, data.user)
      router.push({ name: 'home' })
    } else {
      error.value = data?.error || 'Не удалось войти.'
    }
  } catch (err) {
    error.value = err.message || 'Сервер недоступен.'
  } finally {
    pending.value = false
  }
}
</script>

<template>
  <section class="auth-wrap">
    <div class="card auth-card">
      <div class="auth-logo">🍳</div>
      <h2 class="auth-title">Добро пожаловать!</h2>
      <p class="auth-sub">Войдите, чтобы продолжить планировать меню</p>

      <form @submit.prevent="submit" class="auth-form">
        <label class="field">
          <label>Email</label>
          <input v-model="email" type="email" autocomplete="email" required />
        </label>
        <label class="field">
          <label>Пароль</label>
          <input v-model="password" type="password" autocomplete="current-password" required />
        </label>
        <p v-if="error" class="error">{{ error }}</p>
        <button type="submit" class="btn btn--primary btn--block" :disabled="pending">
          {{ pending ? 'Вход…' : 'Войти' }}
        </button>
      </form>

      <p class="auth-alt">
        Нет аккаунта? <router-link to="/register">Зарегистрируйтесь</router-link>
      </p>
      <p class="auth-alt">
        <router-link to="/forgot">Забыли пароль?</router-link>
      </p>
    </div>
  </section>
</template>

<style scoped>
.auth-wrap {
  display: flex;
  justify-content: center;
  padding-top: 2rem;
}

.auth-card {
  width: 100%;
  max-width: 400px;
  text-align: center;
}

.auth-logo {
  font-size: 2.75rem;
  line-height: 1;
}

.auth-title {
  margin: 0.75rem 0 0.25rem;
  font-size: 1.5rem;
}

.auth-sub {
  color: var(--text-soft);
  margin: 0 0 1.5rem;
  font-size: 0.95rem;
}

.auth-form {
  display: flex;
  flex-direction: column;
  gap: 1rem;
  text-align: left;
}

.auth-alt {
  margin: 1.25rem 0 0;
  font-size: 0.9rem;
  color: var(--text-soft);
}
</style>
