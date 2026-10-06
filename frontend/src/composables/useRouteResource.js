import { ref } from 'vue'

export const RESOURCE_LOAD_ERROR =
  'Не удалось загрузить данные. Проверьте соединение и попробуйте снова.'
export const RESOURCE_NOT_FOUND = 'Ресурс не найден.'

/**
 * Загрузка ресурса, привязанного к ключу маршрута (id рецепта или токен
 * публичной ссылки). Каждый ответ сверяется с актуальным ключом и номером
 * запроса, поэтому поздний ответ старого ресурса не заменяет новый.
 */
export function useRouteResource(fetchResource, options = {}) {
  const notFoundMessage = options.notFound || RESOURCE_NOT_FOUND
  const loadErrorMessage = options.loadError || RESOURCE_LOAD_ERROR

  const key = ref(options.initialKey ?? null)
  const resource = ref(null)
  const loading = ref(false)
  const error = ref('')

  let requestId = 0

  function isCurrent(requestIdForLoad, requestedKey) {
    return requestIdForLoad === requestId && requestedKey === key.value
  }

  async function load(nextKey = key.value) {
    key.value = nextKey
    const requestIdForLoad = ++requestId
    loading.value = true
    error.value = ''
    resource.value = null
    try {
      const { response, data } = await fetchResource(nextKey)
      if (!isCurrent(requestIdForLoad, nextKey)) return null
      if (response.status === 200) {
        if (data == null) {
          error.value = loadErrorMessage
          return null
        }
        resource.value = data
        return data
      }
      if (response.status === 404) {
        error.value = data?.error || notFoundMessage
      } else {
        error.value = data?.error || loadErrorMessage
      }
      return null
    } catch {
      if (isCurrent(requestIdForLoad, nextKey)) error.value = loadErrorMessage
      return null
    } finally {
      if (requestIdForLoad === requestId) loading.value = false
    }
  }

  function reload() {
    return load(key.value)
  }

  return { key, resource, loading, error, load, reload }
}
