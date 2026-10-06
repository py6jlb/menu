const INTERNAL_BASE = 'http://internal.local'

/**
 * Возвращает только безопасный внутренний маршрут. Абсолютные URL, схема
 * `//host` и обратные слэши отбрасываются, чтобы `returnTo` из query не стал
 * открытым редиректом на чужой сайт.
 */
export function sanitizeReturnTo(value) {
  if (typeof value !== 'string' || value.length === 0) return null
  if (!value.startsWith('/') || value.startsWith('//')) return null
  if (value.includes('\\')) return null

  try {
    const url = new URL(value, INTERNAL_BASE)
    if (url.origin !== INTERNAL_BASE) return null
    return `${url.pathname}${url.search}${url.hash}`
  } catch {
    return null
  }
}
