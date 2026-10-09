/**
 * Офлайн-кэш читающих API-ответов.
 *
 * Кэшируется явный allowlist читающих GET: то, что смотришь офлайн, — список
 * покупок, план, рецепты, семья, настройки, автодополнение и публичный просмотр
 * по ссылке. Исключены аутентификация, админка и управление ссылками: их данные
 * на устройстве не нужны, а ссылка — изменяющая операция.
 */
export const API_CACHE_NAME = 'menu-api'

/**
 * Предикат Workbox `urlPattern`: самодостаточная функция без внешних ссылок —
 * Workbox сериализует её в service worker и замыкания туда не попадают.
 * Принимает `{ url, request }`, отсекает не-GET и всё, кроме allowlist.
 */
export function isCacheableApiRequest({ url, request }) {
  if (request && request.method && request.method !== 'GET') return false

  const path = url.pathname
  // Подбор рецептов делит префикс с деталью рецепта, но кэшировать его нельзя.
  if (path === '/api/recipes/match') return false

  return (
    /^\/api\/shopping-list$/.test(path) ||
    /^\/api\/plans\/week\/[^/]+$/.test(path) ||
    /^\/api\/recipes$/.test(path) ||
    /^\/api\/recipes\/[^/]+$/.test(path) ||
    /^\/api\/families$/.test(path) ||
    /^\/api\/settings$/.test(path) ||
    /^\/api\/ingredients\/autocomplete$/.test(path) ||
    /^\/api\/shared\/[^/]+$/.test(path)
  )
}

/**
 * Очистка runtime-кэша API при выходе и при 401. Статику и оболочку не трогаем.
 * В окружении без Cache Storage (тесты, старый браузер) — тихо ничего не делаем.
 */
export async function clearApiCache() {
  if (typeof caches === 'undefined') return
  try {
    await caches.delete(API_CACHE_NAME)
  } catch {
    // Кэш недоступен — не критично для выхода из сессии.
  }
}
