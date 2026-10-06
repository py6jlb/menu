import { describe, it, expect, beforeEach, vi } from 'vitest'

const TOKEN_KEY = 'menu_planner_token'
const USER_KEY = 'menu_planner_user'

async function loadStore() {
  vi.resetModules()
  return import('./auth')
}

beforeEach(() => {
  localStorage.clear()
})

describe('auth — инициализация сессии', () => {
  it('читает токен и пользователя из хранилища', async () => {
    localStorage.setItem(TOKEN_KEY, 'tok')
    localStorage.setItem(USER_KEY, JSON.stringify({ id: 'u1', role: 'User', isEmailVerified: false }))

    const { useAuth, SESSION_STATUS } = await loadStore()
    const auth = useAuth()

    expect(auth.state.token).toBe('tok')
    expect(auth.state.user.id).toBe('u1')
    expect(auth.status.value).toBe(SESSION_STATUS.IDLE)
  })

  it('битый JSON пользователя не ломает инициализацию', async () => {
    localStorage.setItem(TOKEN_KEY, 'tok')
    localStorage.setItem(USER_KEY, '{not json')

    const { useAuth } = await loadStore()

    expect(useAuth().state.user).toBeNull()
    expect(useAuth().isAuthenticated.value).toBe(true)
  })

  it('недоступное хранилище не даёт исключения и сессия живёт в памяти', async () => {
    const original = Object.getOwnPropertyDescriptor(window, 'localStorage')
    Object.defineProperty(window, 'localStorage', {
      configurable: true,
      get() {
        throw new Error('storage denied')
      }
    })
    try {
      const { useAuth } = await loadStore()
      const auth = useAuth()

      expect(() => auth.setSession('t', { id: 'u' })).not.toThrow()
      expect(auth.state.token).toBe('t')
      expect(auth.isAuthenticated.value).toBe(true)
      expect(() => auth.clearSession()).not.toThrow()
      expect(auth.state.token).toBe('')
    } finally {
      Object.defineProperty(window, 'localStorage', original)
    }
  })
})

describe('auth — роли', () => {
  it('Admin — Администратор, User — Пользователь; владение семьёй отдельно', async () => {
    const { useAuth } = await loadStore()
    const auth = useAuth()

    auth.setSession('t', { id: 'a', role: 'Admin' })
    expect(auth.isAdmin.value).toBe(true)
    expect(auth.roleLabel.value).toBe('Администратор')

    auth.setSession('t', { id: 'u', role: 'User' })
    expect(auth.isAdmin.value).toBe(false)
    expect(auth.roleLabel.value).toBe('Пользователь')
  })
})

describe('auth — 401 и токены', () => {
  it('поздний 401 старого токена не гасит новую сессию', async () => {
    localStorage.setItem(TOKEN_KEY, 'old')
    const { useAuth } = await loadStore()
    const auth = useAuth()

    auth.setSession('new', { id: 'u2' })

    expect(auth.invalidateSession('old')).toBe(false)
    expect(auth.state.token).toBe('new')
    expect(auth.isAuthenticated.value).toBe(true)
  })

  it('401 текущего токена очищает сессию', async () => {
    const { useAuth } = await loadStore()
    const auth = useAuth()
    auth.setSession('tok', { id: 'u1' })

    expect(auth.invalidateSession('tok')).toBe(true)
    expect(auth.state.token).toBe('')
    expect(auth.state.user).toBeNull()
  })

  it('401 запроса без токена не гасит существующую сессию', async () => {
    const { useAuth } = await loadStore()
    const auth = useAuth()
    auth.setSession('tok', { id: 'u1' })

    expect(auth.invalidateSession('')).toBe(false)
    expect(auth.state.token).toBe('tok')
  })
})

describe('auth — синхронизация вкладок', () => {
  async function seed() {
    localStorage.setItem(TOKEN_KEY, 'tok')
    localStorage.setItem(USER_KEY, JSON.stringify({ id: 'u1', isEmailVerified: false }))
    const { useAuth } = await loadStore()
    return useAuth()
  }

  it('выход в другой вкладке очищает текущую сессию без обратной записи', async () => {
    const auth = await seed()

    window.dispatchEvent(new StorageEvent('storage', { key: TOKEN_KEY, newValue: null }))

    expect(auth.state.token).toBe('')
    expect(auth.state.user).toBeNull()
    // Обратной записи нет: localStorage не тронут этим обработчиком.
    expect(localStorage.getItem(TOKEN_KEY)).toBe('tok')
  })

  it('подтверждение почты в другой вкладке обновляет пользователя', async () => {
    const auth = await seed()
    const updated = JSON.stringify({ id: 'u1', isEmailVerified: true })
    localStorage.setItem(USER_KEY, updated)

    window.dispatchEvent(new StorageEvent('storage', { key: USER_KEY, newValue: updated }))

    expect(auth.isEmailVerified.value).toBe(true)
    expect(auth.isAuthenticated.value).toBe(true)
    expect(localStorage.getItem(USER_KEY)).toBe(updated)
  })

  it('новый вход в другой вкладке заменяет сессию только чтением', async () => {
    const auth = await seed()

    const nextUser = JSON.stringify({ id: 'u2', isEmailVerified: true })
    localStorage.setItem(TOKEN_KEY, 'tok2')
    localStorage.setItem(USER_KEY, nextUser)
    window.dispatchEvent(new StorageEvent('storage', { key: TOKEN_KEY, newValue: 'tok2' }))

    expect(auth.state.token).toBe('tok2')
    expect(auth.state.user.id).toBe('u2')
  })

  it('битый пользователь из другой вкладки не ломает сессию', async () => {
    const auth = await seed()

    window.dispatchEvent(new StorageEvent('storage', { key: USER_KEY, newValue: '{broken' }))

    expect(auth.state.user).toBeNull()
    expect(auth.state.token).toBe('tok')
  })
})
