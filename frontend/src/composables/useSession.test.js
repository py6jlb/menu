import { describe, it, expect, vi, beforeEach } from 'vitest'

vi.mock('../api/auth', () => ({
  getMe: vi.fn()
}))

const TOKEN_KEY = 'menu_planner_token'
const USER_KEY = 'menu_planner_user'

function deferred() {
  let resolve
  const promise = new Promise((res) => {
    resolve = res
  })
  return { promise, resolve }
}

async function setup({ token, user } = {}) {
  localStorage.clear()
  if (token) localStorage.setItem(TOKEN_KEY, token)
  if (user) localStorage.setItem(USER_KEY, JSON.stringify(user))
  vi.resetModules()
  const authModule = await import('../stores/auth')
  const apiModule = await import('../api/auth')
  const sessionModule = await import('./useSession')
  return {
    useAuth: authModule.useAuth,
    SESSION_STATUS: authModule.SESSION_STATUS,
    getMe: apiModule.getMe,
    ensureSession: sessionModule.ensureSession
  }
}

beforeEach(() => {
  vi.clearAllMocks()
})

describe('ensureSession — инициализация', () => {
  it('без токена сразу переходит в ready и не зовёт me', async () => {
    const { useAuth, ensureSession, getMe, SESSION_STATUS } = await setup()

    await ensureSession()

    expect(getMe).not.toHaveBeenCalled()
    expect(useAuth().status.value).toBe(SESSION_STATUS.READY)
  })

  it('с токеном подтягивает актуального пользователя', async () => {
    const { useAuth, ensureSession, getMe, SESSION_STATUS } = await setup({
      token: 'tok',
      user: { id: 'u1', isEmailVerified: false }
    })
    getMe.mockResolvedValue({
      response: { status: 200 },
      data: { id: 'u1', isEmailVerified: true, role: 'User' }
    })

    await ensureSession()

    expect(getMe).toHaveBeenCalledTimes(1)
    expect(useAuth().state.user.isEmailVerified).toBe(true)
    expect(useAuth().status.value).toBe(SESSION_STATUS.READY)
  })

  it('401 по устаревшему токену очищает сессию', async () => {
    const { useAuth, ensureSession, getMe } = await setup({
      token: 'stale',
      user: { id: 'u1' }
    })
    getMe.mockResolvedValue({ response: { status: 401 }, data: null })

    await ensureSession()

    expect(useAuth().state.token).toBe('')
    expect(useAuth().isAuthenticated.value).toBe(false)
  })

  it('сетевой сбой сохраняет кэш и снимает restoring', async () => {
    const { useAuth, ensureSession, getMe, SESSION_STATUS } = await setup({
      token: 'tok',
      user: { id: 'u1', isEmailVerified: false }
    })
    getMe.mockRejectedValue(new Error('offline'))

    await ensureSession()

    expect(useAuth().state.token).toBe('tok')
    expect(useAuth().state.user.id).toBe('u1')
    expect(useAuth().status.value).toBe(SESSION_STATUS.READY)
  })

  it('параллельные вызовы делают один запрос и дожидаются его', async () => {
    const { useAuth, ensureSession, getMe, SESSION_STATUS } = await setup({
      token: 'tok',
      user: { id: 'u1' }
    })
    const pending = deferred()
    getMe.mockReturnValue(pending.promise)

    const first = ensureSession()
    const second = ensureSession()

    expect(useAuth().status.value).toBe(SESSION_STATUS.RESTORING)
    expect(getMe).toHaveBeenCalledTimes(1)

    pending.resolve({ response: { status: 200 }, data: { id: 'u1', isEmailVerified: true } })
    await Promise.all([first, second])

    expect(useAuth().isEmailVerified.value).toBe(true)
    expect(useAuth().status.value).toBe(SESSION_STATUS.READY)
  })

  it('после ready повторный вызов не делает запрос', async () => {
    const { ensureSession, getMe } = await setup({ token: 'tok', user: { id: 'u1' } })
    getMe.mockResolvedValue({ response: { status: 200 }, data: { id: 'u1' } })

    await ensureSession()
    await ensureSession()

    expect(getMe).toHaveBeenCalledTimes(1)
  })
})
