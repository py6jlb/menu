<script setup>
import { ref, onMounted } from 'vue'
import { useRouter } from 'vue-router'
import { useAuth } from '../stores/auth'
import { verifyEmail, resendVerification } from '../api/auth'
import {
  RESEND_COOLDOWN_SECONDS,
  rateLimitMessage,
  retryAfterSeconds,
  useCooldown
} from '../composables/useCooldown'

const router = useRouter()
const { state, isEmailVerified, updateUser } = useAuth()

const code = ref('')
const error = ref('')
const success = ref('')
const pending = ref(false)

const CODE_ERRORS = {
  invalid: 'Неверный код. Проверьте и попробуйте снова.',
  expired: 'Срок действия кода истёк. Запросите новый код.',
  used: 'Этот код уже использован. Запросите новый код.'
}

const resendPending = ref(false)
const resendError = ref('')
const resendMessage = ref('')
const { cooldown, cooldownLabel, startCooldown } = useCooldown()

async function submit() {
  error.value = ''
  success.value = ''
  pending.value = true
  try {
    const { response, data } = await verifyEmail(code.value.trim())
    if (response.status === 200) {
      updateUser(data)
      code.value = ''
      success.value = 'Почта подтверждена. Спасибо!'
      setTimeout(() => router.push({ name: 'home' }), 1500)
    } else if (response.status === 409) {
      success.value = 'Почта уже подтверждена.'
    } else if (response.status === 423) {
      error.value = data?.error || 'Слишком много неверных попыток. Попробуйте позже.'
    } else if (response.status === 400 && data?.code) {
      error.value = CODE_ERRORS[data.code] || data.error || 'Неверный код. Проверьте и попробуйте снова.'
    } else {
      error.value = data?.error || 'Неверный код. Проверьте и попробуйте снова.'
    }
  } catch (err) {
    error.value = 'Не удалось связаться с сервером. Проверьте соединение и попробуйте снова.'
  } finally {
    pending.value = false
  }
}

async function resend() {
  resendError.value = ''
  resendMessage.value = ''
  resendPending.value = true
  try {
    const { response, data } = await resendVerification()
    if (response.status === 200) {
      resendMessage.value = 'Код отправлен повторно.'
      startCooldown(RESEND_COOLDOWN_SECONDS)
    } else if (response.status === 429) {
      const seconds = retryAfterSeconds(response, data)
      if (seconds) startCooldown(seconds)
      resendError.value = rateLimitMessage(response, data)
    } else if (response.status === 423) {
      resendError.value = data?.error || 'Ввод кода временно заблокирован.'
    } else {
      resendError.value = data?.error || 'Не удалось отправить код.'
    }
  } catch (err) {
    resendError.value = err.message || 'Сервер недоступен.'
  } finally {
    resendPending.value = false
  }
}

onMounted(() => {
  if (isEmailVerified.value) {
    success.value = 'Почта уже подтверждена.'
  }
})
</script>

<template>
  <section class="auth-wrap">
    <div class="card auth-card">
      <div class="auth-logo">✉️</div>
      <h2 class="auth-title">Подтвердите почту</h2>
      <p class="auth-sub">
        Введите 6-значный код, который мы отправили на
        <strong>{{ state.user?.email }}</strong>.
      </p>

      <template v-if="isEmailVerified">
        <p class="success">Почта уже подтверждена.</p>
        <router-link to="/" class="btn btn--primary btn--block">На главную</router-link>
      </template>

      <template v-else>
        <form @submit.prevent="submit" class="auth-form">
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
          <p v-if="error" class="error">{{ error }}</p>
          <p v-else-if="success" class="success">{{ success }}</p>
          <button type="submit" class="btn btn--primary btn--block" :disabled="pending || code.trim().length < 6">
            {{ pending ? 'Проверяем…' : 'Подтвердить' }}
          </button>
        </form>

        <div class="resend-block">
          <p v-if="resendMessage" class="success">{{ resendMessage }}</p>
          <p v-else-if="resendError" class="error">{{ resendError }}</p>
          <button
            type="button"
            class="btn btn--ghost btn--block"
            :disabled="resendPending || cooldown > 0"
            @click="resend"
          >
            <template v-if="cooldown > 0">Отправить снова через {{ cooldownLabel }}</template>
            <template v-else>{{ resendPending ? 'Отправляем…' : 'Отправить код ещё раз' }}</template>
          </button>
        </div>

        <p class="auth-alt">
          <router-link to="/">Позже, вернуться на главную</router-link>
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

.auth-sub strong {
  color: var(--text);
  word-break: break-word;
}

.auth-form {
  display: flex;
  flex-direction: column;
  gap: 1rem;
  text-align: left;
}

.code-input {
  font-size: 1.5rem;
  letter-spacing: 0.35em;
  text-align: center;
  font-weight: 800;
}

.resend-block {
  display: flex;
  flex-direction: column;
  gap: 0.75rem;
  margin-top: 1rem;
}

.auth-alt {
  margin: 1.25rem 0 0;
  font-size: 0.9rem;
  color: var(--text-soft);
}
</style>
