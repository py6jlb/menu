<script setup>
import { ref, computed, onBeforeUnmount } from 'vue'
import { forgotPassword, resetPassword } from '../api/auth'

const RESEND_COOLDOWN_SECONDS = 300

const email = ref('')
const sent = ref(false)
const neutralMessage = ref('')
const forgotPending = ref(false)
const forgotError = ref('')

const code = ref('')
const newPassword = ref('')
const newPasswordConfirm = ref('')
const resetPending = ref(false)
const resetError = ref('')
const resetDone = ref(false)

const cooldown = ref(0)
let timer = null

const cooldownLabel = computed(() => {
  const minutes = Math.floor(cooldown.value / 60)
  const seconds = cooldown.value % 60
  return `${minutes}:${String(seconds).padStart(2, '0')}`
})

const passwordsMismatch = computed(
  () => newPasswordConfirm.value.length > 0 && newPassword.value !== newPasswordConfirm.value
)

function stopTimer() {
  if (timer) {
    clearInterval(timer)
    timer = null
  }
}

function startCooldown(seconds) {
  cooldown.value = Math.max(0, Math.floor(seconds))
  stopTimer()
  if (cooldown.value === 0) return
  timer = setInterval(() => {
    cooldown.value -= 1
    if (cooldown.value <= 0) {
      cooldown.value = 0
      stopTimer()
    }
  }, 1000)
}

function parseCooldownSeconds(message) {
  const match = /(\d+)\s*сек/.exec(message || '')
  return match ? Number(match[1]) : 0
}

async function requestCode() {
  forgotError.value = ''
  forgotPending.value = true
  try {
    const { response, data } = await forgotPassword(email.value.trim())
    if (response.status === 200) {
      sent.value = true
      neutralMessage.value =
        data?.message || 'Если аккаунт существует и почта подтверждена, отправлен код.'
      startCooldown(RESEND_COOLDOWN_SECONDS)
    } else if (response.status === 429) {
      const seconds = parseCooldownSeconds(data?.error)
      if (seconds) startCooldown(seconds)
      forgotError.value = data?.error || 'Слишком много запросов. Попробуйте позже.'
    } else {
      forgotError.value = data?.error || 'Не удалось отправить код.'
    }
  } catch (err) {
    forgotError.value = err.message || 'Сервер недоступен.'
  } finally {
    forgotPending.value = false
  }
}

async function submitReset() {
  resetError.value = ''
  if (newPassword.value !== newPasswordConfirm.value) {
    resetError.value = 'Пароли не совпадают.'
    return
  }
  resetPending.value = true
  try {
    const { response, data } = await resetPassword({
      email: email.value.trim(),
      code: code.value.trim(),
      newPassword: newPassword.value,
      newPasswordConfirm: newPasswordConfirm.value
    })
    if (response.status === 200) {
      resetDone.value = true
    } else if (response.status === 423) {
      resetError.value = data?.error || 'Слишком много неверных попыток. Попробуйте позже.'
    } else {
      resetError.value = data?.error || 'Неверный или истёкший код.'
    }
  } catch (err) {
    resetError.value = err.message || 'Сервер недоступен.'
  } finally {
    resetPending.value = false
  }
}

onBeforeUnmount(stopTimer)
</script>

<template>
  <section class="auth-wrap">
    <div class="card auth-card">
      <div class="auth-logo">🔑</div>
      <h2 class="auth-title">Сброс пароля</h2>

      <template v-if="resetDone">
        <p class="success">Пароль изменён. Войдите с новым паролем.</p>
        <router-link to="/login" class="btn btn--primary btn--block">Перейти ко входу</router-link>
      </template>

      <template v-else>
        <p class="auth-sub">
          Введите email — если аккаунт существует и почта подтверждена, мы отправим код
          для сброса пароля.
        </p>

        <form @submit.prevent="requestCode" class="auth-form">
          <label class="field">
            <label>Email</label>
            <input
              v-model="email"
              type="email"
              autocomplete="email"
              :disabled="sent"
              required
            />
          </label>
          <p v-if="forgotError" class="error">{{ forgotError }}</p>
          <button
            type="submit"
            class="btn btn--primary btn--block"
            :disabled="forgotPending || cooldown > 0 || !email.trim()"
          >
            <template v-if="cooldown > 0">Отправить снова через {{ cooldownLabel }}</template>
            <template v-else>{{ forgotPending ? 'Отправляем…' : 'Отправить код' }}</template>
          </button>
        </form>

        <template v-if="sent">
          <p class="success">{{ neutralMessage }}</p>
          <p class="hint">
            Для восстановления нужна подтверждённая почта. Код действует 1 час.
          </p>

          <form @submit.prevent="submitReset" class="auth-form reset-form">
            <label class="field">
              <label>Код из письма</label>
              <input
                v-model="code"
                type="text"
                inputmode="numeric"
                autocomplete="one-time-code"
                maxlength="6"
                pattern="[0-9]{6}"
                placeholder="000000"
                required
                class="code-input"
              />
            </label>
            <label class="field">
              <label>Новый пароль</label>
              <input
                v-model="newPassword"
                type="password"
                autocomplete="new-password"
                minlength="6"
                required
              />
            </label>
            <label class="field">
              <label>Повторите новый пароль</label>
              <input
                v-model="newPasswordConfirm"
                type="password"
                autocomplete="new-password"
                minlength="6"
                required
              />
            </label>
            <p v-if="passwordsMismatch" class="error">Пароли не совпадают.</p>
            <p v-if="resetError" class="error">{{ resetError }}</p>
            <button
              type="submit"
              class="btn btn--primary btn--block"
              :disabled="resetPending || passwordsMismatch || code.trim().length < 6"
            >
              {{ resetPending ? 'Меняем пароль…' : 'Сменить пароль' }}
            </button>
          </form>
        </template>

        <p class="auth-alt">
          Вспомнили пароль? <router-link to="/login">Войдите</router-link>
        </p>
      </template>
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
  max-width: 420px;
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

.reset-form {
  margin-top: 1rem;
}

.code-input {
  font-size: 1.5rem;
  letter-spacing: 0.35em;
  text-align: center;
  font-weight: 800;
}

.auth-alt {
  margin: 1.25rem 0 0;
  font-size: 0.9rem;
  color: var(--text-soft);
}
</style>
