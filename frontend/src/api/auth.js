import { apiJson } from './client'

export async function getMe() {
  return apiJson('/api/auth/me')
}

export async function verifyEmail(code) {
  return apiJson('/api/auth/verify', {
    method: 'POST',
    body: JSON.stringify({ code })
  })
}

export async function resendVerification() {
  return apiJson('/api/auth/verify/resend', {
    method: 'POST'
  })
}

export async function forgotPassword(email) {
  return apiJson('/api/auth/forgot', {
    method: 'POST',
    body: JSON.stringify({ email })
  })
}

export async function resetPassword(payload) {
  return apiJson('/api/auth/reset', {
    method: 'POST',
    body: JSON.stringify(payload)
  })
}
