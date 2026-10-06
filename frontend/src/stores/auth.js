import { reactive, ref, computed } from 'vue'
import { localStore } from './storage'

const TOKEN_KEY = 'menu_planner_token'
const USER_KEY = 'menu_planner_user'

/** Явные состояния инициализации сессии. */
export const SESSION_STATUS = {
  IDLE: 'idle',
  RESTORING: 'restoring',
  READY: 'ready'
}

function parseUser(raw) {
  if (!raw) return null
  try {
    const parsed = JSON.parse(raw)
    return parsed && typeof parsed === 'object' ? parsed : null
  } catch {
    return null
  }
}

function readStoredUser() {
  return parseUser(localStore.get(USER_KEY))
}

const state = reactive({
  token: localStore.get(TOKEN_KEY) || '',
  user: readStoredUser()
})

const status = ref(SESSION_STATUS.IDLE)

function setSession(token, user) {
  state.token = token || ''
  state.user = user || null
  if (state.token) localStore.set(TOKEN_KEY, state.token)
  else localStore.remove(TOKEN_KEY)
  if (user) localStore.set(USER_KEY, JSON.stringify(user))
  else localStore.remove(USER_KEY)
}

function updateUser(user) {
  if (!user) return
  state.user = user
  localStore.set(USER_KEY, JSON.stringify(user))
}

function clearSession() {
  state.token = ''
  state.user = null
  localStore.remove(TOKEN_KEY)
  localStore.remove(USER_KEY)
}

/**
 * 401 завершает только ту сессию, чьим токеном пользовался запрос. Поздний
 * ответ с прежним токеном не должен гасить новую сессию, вошедшую позже.
 */
function invalidateSession(token) {
  if (token && state.token !== token) return false
  clearSession()
  return true
}

/**
 * Синхронизация между вкладками: только чтение чужого изменения, никаких
 * обратных записей — иначе событие `storage` зациклится.
 */
function onStorage(event) {
  if (!event) return
  if (event.key === TOKEN_KEY) {
    if (!event.newValue) {
      state.token = ''
      state.user = null
      return
    }
    state.token = event.newValue
    state.user = readStoredUser()
    return
  }
  if (event.key === USER_KEY) {
    state.user = parseUser(event.newValue)
  }
}

if (typeof window !== 'undefined' && typeof window.addEventListener === 'function') {
  window.addEventListener('storage', onStorage)
}

export function useAuth() {
  const isAuthenticated = computed(() => Boolean(state.token))
  const isEmailVerified = computed(() => state.user?.isEmailVerified === true)
  const isAdmin = computed(() => state.user?.role === 'Admin')
  const roleLabel = computed(() => (isAdmin.value ? 'Администратор' : 'Пользователь'))

  return {
    state,
    status,
    isAuthenticated,
    isEmailVerified,
    isAdmin,
    roleLabel,
    setSession,
    updateUser,
    clearSession,
    invalidateSession
  }
}
