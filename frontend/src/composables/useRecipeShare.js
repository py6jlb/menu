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

export function useRecipeShare() {
  const share = ref(null)
  const loading = ref(false)
  const error = ref('')

  function apply(data) {
    share.value = data || null
    return share.value
  }

  function fail(fallback, data) {
    error.value = data?.error || fallback
    return null
  }

  async function run(fallback, action) {
    error.value = ''
    loading.value = true
    try {
      return await action()
    } catch {
      return fail(fallback)
    } finally {
      loading.value = false
    }
  }

  async function load(recipeId) {
    error.value = ''
    try {
      const { response, data } = await getRecipeShare(recipeId)
      if (response.status === 200) return apply(data)
      if (response.status === 404) return apply(null)
      return fail(LOAD_ERROR, data)
    } catch {
      return fail(LOAD_ERROR)
    }
  }

  function create(recipeId) {
    return run(CREATE_ERROR, async () => {
      const { response, data } = await createRecipeShare(recipeId)
      if (response.status === 200 || response.status === 201) return apply(data)
      return fail(CREATE_ERROR, data)
    })
  }

  function revoke(recipeId) {
    return run(REVOKE_ERROR, async () => {
      const { response, data } = await revokeRecipeShare(recipeId)
      if (response.status === 200) return apply(data)
      return fail(REVOKE_ERROR, data)
    })
  }

  function regenerate(recipeId) {
    return run(REGENERATE_ERROR, async () => {
      const { response, data } = await regenerateRecipeShare(recipeId)
      if (response.status === 200) return apply(data)
      return fail(REGENERATE_ERROR, data)
    })
  }

  return { share, loading, error, load, create, revoke, regenerate }
}
