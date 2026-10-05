<script setup>
import { ref, computed } from 'vue'
import { forgotPassword, resetPassword } from '../api/auth'
import {
  RESEND_COOLDOWN_SECONDS,
  parseCooldownSeconds,
  useCooldown
} from '../composables/useCooldown'

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

const { cooldown, cooldownLabel, startCooldown } = useCooldown()

const RESET_CODE_ERRORS = {
  invalid: 'Неверный код. Проверьте и попробуйте снова.',
  expired: 'Срок действия кода истёк. Запросите новый код.',
  used: 'Этот код уже использован. Запросите новый код.',
  closed: 'Слишком много неверных попыток. Запросите новый код.'
}

const passwordsMismatch = computed(
  () => newPasswordConfirm.value.length > 0 && newPassword.value !== newPasswordConfirm.value
)

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
    } else if (response.status === 400 && data?.code) {
      resetError.value = RESET_CODE_ERRORS[data.code] || data.error || 'Неверный или истёкший код.'
    } else if (response.status === 423) {
      resetError.value = data?.error || 'Слишком много неверных попыток. Запросите новый код.'
    } else {
      resetError.value = data?.error || 'Неверный или истёкший код.'
    }
  } catch (err) {
    resetError.value = err.message || 'Сервер недоступен.'
  } finally {
    resetPending.value = false
  }
}
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
