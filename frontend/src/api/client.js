import { useAuth } from '../stores/auth'

const PUBLIC_PATHS = [
  '/api/auth/login',
  '/api/auth/register',
  '/api/auth/forgot',
  '/api/auth/reset'
]

export async function apiFetch(path, options = {}) {
  const { state, clearSession } = useAuth()

  const isPublic = PUBLIC_PATHS.some((publicPath) => path.startsWith(publicPath))

  const headers = { ...(options.headers || {}) }
  if (options.body !== undefined && !(options.body instanceof FormData) && !headers['Content-Type']) {
    headers['Content-Type'] = 'application/json'
  }
  if (state.token && !isPublic) {
    headers['Authorization'] = `Bearer ${state.token}`
  }

  const response = await fetch(path, { ...options, headers })

  if (response.status === 401 && !isPublic) {
    clearSession()
    window.location.href = '/login'
  }

  return response
}

export async function apiJson(path, options = {}) {
  const response = await apiFetch(path, options)
  const text = await response.text()
  let data = null
  if (text) {
    try {
      data = JSON.parse(text)
    } catch {
      data = null
    }
  }
  return { response, data }
}
