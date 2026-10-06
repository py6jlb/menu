import { ref } from 'vue'
import { getMyFamily, createFamily, joinFamily, regenerateInviteCode, removeMember } from '../api/families'

export const FAMILY_LOAD_ERROR = 'Не удалось загрузить семью. Проверьте соединение и попробуйте снова.'
export const FAMILY_CREATE_ERROR = 'Не удалось создать семью.'
export const FAMILY_JOIN_ERROR = 'Не удалось присоединиться.'
export const FAMILY_COPY_ERROR = 'Не удалось скопировать код.'
export const FAMILY_REGENERATE_ERROR = 'Не удалось обновить код.'
export const FAMILY_REMOVE_ERROR = 'Не удалось удалить участника.'
export const FAMILY_NETWORK_ERROR = 'Сервер недоступен.'

const COPY_RESET_MS = 1500

/**
 * Состояние семьи, привязанного к странице: загрузка и каждая мутация
 * (создание, вступление, копирование кода, перегенерация, удаление участника)
 * ведут собственные сообщение об ошибке и признак ожидания. Ошибка одного
 * действия не скрывает уже загруженную семью и формы, а незавершённая операция
 * всегда снимает ожидание и допускает безопасный повтор с того же состояния.
 */
export function useFamily() {
  const family = ref(null)

  const loading = ref(false)
  const loadError = ref('')

  const createError = ref('')
  const joinError = ref('')
  const copyError = ref('')
  const regenerateError = ref('')
  const removeError = ref('')

  const creating = ref(false)
  const joining = ref(false)
  const regenerating = ref(false)
  const removing = ref(false)
  const copied = ref(false)

  let copyTimer = null

  async function load() {
    loading.value = true
    loadError.value = ''
    try {
      const { response, data } = await getMyFamily()
      if (response.status === 404) {
        family.value = null
      } else if (response.status === 200) {
        family.value = data
      } else {
        loadError.value = data?.error || FAMILY_LOAD_ERROR
      }
    } catch {
      loadError.value = FAMILY_LOAD_ERROR
    } finally {
      loading.value = false
    }
  }

  async function create(name) {
    if (creating.value) return false
    creating.value = true
    createError.value = ''
    try {
      const { response, data } = await createFamily(String(name ?? '').trim())
      if (response.status === 201) {
        family.value = data
        return true
      }
      createError.value = data?.error || FAMILY_CREATE_ERROR
      return false
    } catch {
      createError.value = FAMILY_NETWORK_ERROR
      return false
    } finally {
      creating.value = false
    }
  }

  async function join(inviteCode) {
    if (joining.value) return false
    joining.value = true
    joinError.value = ''
    try {
      const { response, data } = await joinFamily(String(inviteCode ?? '').trim())
      if (response.status === 200) {
        family.value = data
        return true
      }
      joinError.value = data?.error || FAMILY_JOIN_ERROR
      return false
    } catch {
      joinError.value = FAMILY_NETWORK_ERROR
      return false
    } finally {
      joining.value = false
    }
  }

  async function copyInviteCode() {
    copyError.value = ''
    try {
      if (!navigator.clipboard?.writeText) throw new Error('clipboard unavailable')
      await navigator.clipboard.writeText(family.value.inviteCode)
      copied.value = true
      if (copyTimer) clearTimeout(copyTimer)
      copyTimer = setTimeout(() => {
        copied.value = false
      }, COPY_RESET_MS)
      return true
    } catch {
      copyError.value = FAMILY_COPY_ERROR
      return false
    }
  }

  async function regenerate() {
    if (regenerating.value) return false
    regenerating.value = true
    regenerateError.value = ''
    try {
      const { response, data } = await regenerateInviteCode(family.value.id)
      if (response.status === 200) {
        family.value = { ...family.value, inviteCode: data.inviteCode }
        return true
      }
      regenerateError.value = data?.error || FAMILY_REGENERATE_ERROR
      return false
    } catch {
      regenerateError.value = FAMILY_NETWORK_ERROR
      return false
    } finally {
      regenerating.value = false
    }
  }

  async function removeMemberById(member) {
    if (removing.value) return false
    removing.value = true
    removeError.value = ''
    try {
      const { response, data } = await removeMember(family.value.id, member.id)
      if (response.status === 204) {
        family.value = {
          ...family.value,
          members: family.value.members.filter((m) => m.id !== member.id)
        }
        return true
      }
      removeError.value = data?.error || FAMILY_REMOVE_ERROR
      return false
    } catch {
      removeError.value = FAMILY_NETWORK_ERROR
      return false
    } finally {
      removing.value = false
    }
  }

  return {
    family,
    loading,
    loadError,
    createError,
    joinError,
    copyError,
    regenerateError,
    removeError,
    creating,
    joining,
    regenerating,
    removing,
    copied,
    load,
    create,
    join,
    copyInviteCode,
    regenerate,
    removeMember: removeMemberById
  }
}
