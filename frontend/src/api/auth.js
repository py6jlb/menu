import { apiJson, postJson } from './client'

/** Вход по email и паролю; ответ содержит токен сессии и пользователя. */
export async function login(email, password) {
  return postJson('/api/auth/login', { email, password })
}

/** Регистрация по email и паролю; после неё почта требует подтверждения. */
export async function register(email, password) {
  return postJson('/api/auth/register', { email, password })
}

/** Текущий пользователь по токену; ответ подтверждает актуальность сессии. */
export async function getMe() {
  return apiJson('/api/auth/me')
}

/** Подтверждение почты кодом из письма. */
export async function verifyEmail(code) {
  return postJson('/api/auth/verify', { code })
}

/** Повторная отправка кода подтверждения почты. */
export async function resendVerification() {
  return apiJson('/api/auth/verify/resend', {
    method: 'POST'
  })
}

/** Запрос кода сброса пароля на email. */
export async function forgotPassword(email) {
  return postJson('/api/auth/forgot', { email })
}

/** Смена пароля по коду сброса; `payload` — email, код и новый пароль с подтверждением. */
export async function resetPassword(payload) {
  return postJson('/api/auth/reset', payload)
}
