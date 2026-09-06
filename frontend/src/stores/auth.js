import { reactive, computed } from 'vue'

const TOKEN_KEY = 'menu_planner_token'
const USER_KEY = 'menu_planner_user'

function loadStoredUser() {
  try {
    const raw = localStorage.getItem(USER_KEY)
    return raw ? JSON.parse(raw) : null
  } catch {
    return null
  }
}

const state = reactive({
  token: localStorage.getItem(TOKEN_KEY) || '',
  user: loadStoredUser()
})

export function useAuth() {
  const isAuthenticated = computed(() => Boolean(state.token))

  function setSession(token, user) {
    state.token = token
    state.user = user
    localStorage.setItem(TOKEN_KEY, token)
    localStorage.setItem(USER_KEY, JSON.stringify(user))
  }

  function clearSession() {
    state.token = ''
    state.user = null
    localStorage.removeItem(TOKEN_KEY)
    localStorage.removeItem(USER_KEY)
  }

  return { state, isAuthenticated, setSession, clearSession }
}
