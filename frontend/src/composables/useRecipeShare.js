import { ref } from 'vue'
import {
  getRecipeShare,
  createRecipeShare,
  revokeRecipeShare,
  regenerateRecipeShare
} from '../api/recipes'
import { useRequestTracker } from './useRequestTracker'

const LOAD_ERROR = 'Не удалось получить ссылку.'
const CREATE_ERROR = 'Не удалось создать ссылку.'
const REVOKE_ERROR = 'Не удалось отозвать ссылку.'
const REGENERATE_ERROR = 'Не удалось перегенерировать ссылку.'

/**
 * Sharing-состояние рецепта, привязанное к identity ресурса. Каждая операция
 * получает токен для id рецепта, поэтому поздний ответ старого рецепта не
 * заполняет состояние нового: результат применяется, только пока запрос
 * остаётся актуальным, а `reset` обесценивает всё при смене маршрута.
 */
export function useRecipeShare() {
  const share = ref(null)
  const loading = ref(false)
  const error = ref('')

  const tracker = useRequestTracker()
  let activeRecipeId = null

  function begin(recipeId) {
    activeRecipeId = recipeId
    return tracker.begin(recipeId)
  }

  function current(token, recipeId) {
    return tracker.isCurrent(token) && recipeId === activeRecipeId
  }

  function reset() {
    tracker.reset()
    activeRecipeId = null
    share.value = null
    loading.value = false
    error.value = ''
  }

  function apply(data) {
    share.value = data || null
    return share.value
  }

  function fail(fallback, data) {
    error.value = data?.error || fallback
    return null
  }

  async function run(fallback, recipeId, action) {
    const token = begin(recipeId)
    error.value = ''
    loading.value = true
    try {
      return await action(token)
    } catch {
      return current(token, recipeId) ? fail(fallback) : share.value
    } finally {
      if (current(token, recipeId)) loading.value = false
    }
  }

  async function load(recipeId) {
    const token = begin(recipeId)
    error.value = ''
    try {
      const { response, data } = await getRecipeShare(recipeId)
      if (!current(token, recipeId)) return share.value
      if (response.status === 200) return apply(data)
      if (response.status === 404) return apply(null)
      return fail(LOAD_ERROR, data)
    } catch {
      return current(token, recipeId) ? fail(LOAD_ERROR) : share.value
    }
  }

  function create(recipeId) {
    return run(CREATE_ERROR, recipeId, async (token) => {
      const { response, data } = await createRecipeShare(recipeId)
      if (!current(token, recipeId)) return share.value
      if (response.status === 200 || response.status === 201) return apply(data)
      return fail(CREATE_ERROR, data)
    })
  }

  function revoke(recipeId) {
    return run(REVOKE_ERROR, recipeId, async (token) => {
      const { response, data } = await revokeRecipeShare(recipeId)
      if (!current(token, recipeId)) return share.value
      if (response.status === 200) return apply(data)
      return fail(REVOKE_ERROR, data)
    })
  }

  function regenerate(recipeId) {
    return run(REGENERATE_ERROR, recipeId, async (token) => {
      const { response, data } = await regenerateRecipeShare(recipeId)
      if (!current(token, recipeId)) return share.value
      if (response.status === 200) return apply(data)
      return fail(REGENERATE_ERROR, data)
    })
  }

  return { share, loading, error, load, create, revoke, regenerate, reset }
}
