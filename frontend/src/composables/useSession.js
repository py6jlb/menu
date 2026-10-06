import { useAuth, SESSION_STATUS } from '../stores/auth'
import { getMe } from '../api/auth'

let restorePromise = null

/**
 * Однократная инициализация сессии в рамках загрузки страницы. Пока идёт
 * запрос, статус `restoring`; защищённая навигация дожидается результата и
 * решает доступ по актуальному `me`, а не по устаревшему кэшу. Сетевой сбой
 * не запирает пользователя: кэш остаётся, а недействительность токена позже
 * поймает первый же 401.
 */
export function ensureSession() {
  const auth = useAuth()
  if (auth.status.value === SESSION_STATUS.READY) return Promise.resolve()
  if (restorePromise) return restorePromise

  auth.status.value = SESSION_STATUS.RESTORING
  restorePromise = restore().finally(() => {
    auth.status.value = SESSION_STATUS.READY
    restorePromise = null
  })
  return restorePromise
}

async function restore() {
  const auth = useAuth()
  if (!auth.state.token) return
  try {
    const { response, data } = await getMe()
    if (response.status === 200 && data) {
      auth.updateUser(data)
    } else if (response.status === 401) {
      auth.clearSession()
    }
  } catch {
    // Сервер недоступен: сохраняем кэш сессии до следующего запроса.
  }
}
