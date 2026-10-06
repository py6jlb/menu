/**
 * Безопасный доступ к Web Storage. В приватном режиме или при запрете
 * куки/хранилища обращение к `window.localStorage`/`setItem` бросает
 * исключение; обёртка не даёт белому экрану и деградирует в память.
 */
function safeStore(getStore) {
  return {
    get(key) {
      try {
        const store = getStore()
        return store ? store.getItem(key) : null
      } catch {
        return null
      }
    },
    set(key, value) {
      try {
        const store = getStore()
        if (store) store.setItem(key, value)
      } catch {
        // Хранилище недоступно — состояние сессии живёт в памяти вкладки.
      }
    },
    remove(key) {
      try {
        const store = getStore()
        if (store) store.removeItem(key)
      } catch {
        // см. set
      }
    }
  }
}

export const localStore = safeStore(() => window.localStorage)
export const sessionStore = safeStore(() => window.sessionStorage)
