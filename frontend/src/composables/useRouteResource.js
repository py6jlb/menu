import { ref } from 'vue'
import { LOAD_FAILED, NOT_FOUND } from '../constants/messages'
import { useRequestTracker } from './useRequestTracker'

/**
 * Загрузка ресурса, привязанного к ключу маршрута (id рецепта или токен
 * публичной ссылки). Каждый ответ сверяется с актуальным ключом и номером
 * запроса, поэтому поздний ответ старого ресурса не заменяет новый.
 */
export function useRouteResource(fetchResource, options = {}) {
  const notFoundMessage = options.notFound || NOT_FOUND
  const loadErrorMessage = options.loadError || LOAD_FAILED

  const key = ref(options.initialKey ?? null)
  const resource = ref(null)
  const loading = ref(false)
  const error = ref('')

  const tracker = useRequestTracker()

  async function load(nextKey = key.value) {
    key.value = nextKey
    const token = tracker.begin(nextKey)
    const current = () => tracker.isCurrent(token) && nextKey === key.value
    loading.value = true
    error.value = ''
    resource.value = null
    try {
      const { response, data } = await fetchResource(nextKey)
      if (!current()) return null
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
      if (current()) error.value = loadErrorMessage
      return null
    } finally {
      if (tracker.isCurrent(token)) loading.value = false
    }
  }

  function reload() {
    return load(key.value)
  }

  return { key, resource, loading, error, load, reload }
}
