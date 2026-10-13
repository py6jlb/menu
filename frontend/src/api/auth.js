import { apiJson, postJson } from './client'

export async function login(email, password) {
  return postJson('/api/auth/login', { email, password })
}

export async function register(email, password) {
  return postJson('/api/auth/register', { email, password })
}

export async function getMe() {
  return apiJson('/api/auth/me')
}

export async function verifyEmail(code) {
  return postJson('/api/auth/verify', { code })
}

export async function resendVerification() {
  return apiJson('/api/auth/verify/resend', {
    method: 'POST'
  })
}

export async function forgotPassword(email) {
  return postJson('/api/auth/forgot', { email })
}

export async function resetPassword(payload) {
  return postJson('/api/auth/reset', payload)
}
