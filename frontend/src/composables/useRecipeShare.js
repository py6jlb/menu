import { ref } from 'vue'
import {
  getRecipeShare,
  createRecipeShare,
  revokeRecipeShare,
  regenerateRecipeShare
} from '../api/recipes'

const LOAD_ERROR = 'Не удалось получить ссылку.'
const CREATE_ERROR = 'Не удалось создать ссылку.'
const REVOKE_ERROR = 'Не удалось отозвать ссылку.'
const REGENERATE_ERROR = 'Не удалось перегенерировать ссылку.'

/**
 * Sharing-состояние рецепта, привязанное к identity ресурса. Каждая операция
 * получает номер запроса и id рецепта, поэтому поздний ответ старого рецепта
 * не заполняет состояние нового: результат применяется, только пока запрос
 * остаётся актуальным, а `reset` обесценивает всё при смене маршрута.
 */
export function useRecipeShare() {
  const share = ref(null)
  const loading = ref(false)
  const error = ref('')

  let requestSeq = 0
  let activeRecipeId = null

  function isCurrent(seq, recipeId) {
    return seq === requestSeq && recipeId === activeRecipeId
  }

  function reset() {
    requestSeq += 1
    activeRecipeId = null
    share.value = null
    loading.value = false
    error.value = ''
  }

  function begin(recipeId) {
    activeRecipeId = recipeId
    return ++requestSeq
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
    const seq = begin(recipeId)
    error.value = ''
    loading.value = true
    try {
      return await action(seq)
    } catch {
      return isCurrent(seq, recipeId) ? fail(fallback) : share.value
    } finally {
      if (isCurrent(seq, recipeId)) loading.value = false
    }
  }

  async function load(recipeId) {
    const seq = begin(recipeId)
    error.value = ''
    try {
      const { response, data } = await getRecipeShare(recipeId)
      if (!isCurrent(seq, recipeId)) return share.value
      if (response.status === 200) return apply(data)
      if (response.status === 404) return apply(null)
      return fail(LOAD_ERROR, data)
    } catch {
      return isCurrent(seq, recipeId) ? fail(LOAD_ERROR) : share.value
    }
  }

  function create(recipeId) {
    return run(CREATE_ERROR, recipeId, async (seq) => {
      const { response, data } = await createRecipeShare(recipeId)
      if (!isCurrent(seq, recipeId)) return share.value
      if (response.status === 200 || response.status === 201) return apply(data)
      return fail(CREATE_ERROR, data)
    })
  }

  function revoke(recipeId) {
    return run(REVOKE_ERROR, recipeId, async (seq) => {
      const { response, data } = await revokeRecipeShare(recipeId)
      if (!isCurrent(seq, recipeId)) return share.value
      if (response.status === 200) return apply(data)
      return fail(REVOKE_ERROR, data)
    })
  }

  function regenerate(recipeId) {
    return run(REGENERATE_ERROR, recipeId, async (seq) => {
      const { response, data } = await regenerateRecipeShare(recipeId)
      if (!isCurrent(seq, recipeId)) return share.value
      if (response.status === 200) return apply(data)
      return fail(REGENERATE_ERROR, data)
    })
  }

  return { share, loading, error, load, create, revoke, regenerate, reset }
}
