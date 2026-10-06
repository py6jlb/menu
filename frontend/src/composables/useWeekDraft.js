import { ref, computed } from 'vue'
import { addDays, toIso } from '../constants/plan'
import { getWeekPlan, saveWeekPlan } from '../api/plans'
import { LEAVE_MESSAGE } from './draftMessages'

export const LOAD_ERROR_MESSAGE = 'Не удалось загрузить план. Проверьте соединение и попробуйте снова.'
export const SAVE_ERROR_MESSAGE = 'Не удалось сохранить план. Проверьте соединение и повторите.'
export const FAMILY_ERROR_MESSAGE = 'Вы пока не состоите в семье.'
export const CONFLICT_MESSAGE =
  'План на этой неделе изменил другой участник. Ваш черновик сохранён. Загрузите актуальную версию, чтобы сравнить и повторить.'
export const RELOAD_CONFIRM_MESSAGE =
  'На сервере более новая версия плана. Загрузить её и заменить текущий черновик?'

export function slotKey(day, mealType) {
  return `${day}:${mealType}`
}

/** Канонический список позиций: только значимые для сохранения поля. */
export function entriesFromDraft(draft) {
  return Object.keys(draft)
    .sort()
    .map((key) => {
      const [day, mealType] = key.split(':')
      const entry = draft[key]
      return { day: Number(day), mealType, recipeId: entry.recipeId, portions: entry.portions }
    })
}

/** Снимок черновика для сравнения с подтверждённым состоянием. */
export function serializeDraft(draft) {
  return JSON.stringify(entriesFromDraft(draft))
}

export function draftFromEntries(entries) {
  const map = {}
  for (const entry of entries || []) {
    map[slotKey(entry.day, entry.mealType)] = {
      recipeId: entry.recipeId,
      recipeName: entry.recipeName,
      portions: entry.portions,
      state: entry.state
    }
  }
  return map
}

/**
 * Недельный черновик, устойчивый к поздним ответам и сетевым ошибкам.
 *
 * Состояние сосредоточено вокруг трёх вещей: identity недели (`weekStart`),
 * актуального черновика (`draft`) и подтверждённого снимка (`confirmedSnapshot`).
 * Интерфейсу не нужно знать порядок ответов: каждый запрос привязан к неделе
 * и номеру запроса, поэтому устаревший ответ не может подменить чужую неделю.
 */
export function useWeekDraft(options = {}) {
  const loadWeekPlan = options.loadWeekPlan || getWeekPlan
  const saveWeekPlanRequest = options.saveWeekPlan || saveWeekPlan
  const confirmLeave = options.confirm || ((message) => window.confirm(message))

  const weekStart = ref(options.initialWeek || new Date())
  const draft = ref({})
  const confirmedSnapshot = ref(serializeDraft({}))
  const revision = ref(0)
  const conflict = ref(null)

  const loading = ref(false)
  const saving = ref(false)
  const loadError = ref('')
  const saveError = ref('')
  const savedMessage = ref('')

  let loadRequestId = 0
  let saveRequestId = 0

  const dirty = computed(() => confirmedSnapshot.value !== serializeDraft(draft.value))

  /**
   * Построчные различия локального черновика и серверной версии при конфликте:
   * какие ячейки на сервере отличаются от черновика. Пусто, если различий нет.
   */
  const conflictDiff = computed(() => {
    if (!conflict.value) return []
    const server = new Map(
      (conflict.value.entries || []).map((entry) => [slotKey(entry.day, entry.mealType), entry])
    )
    const local = draft.value
    const keys = new Set([...server.keys(), ...Object.keys(local)])
    const diff = []
    for (const key of keys) {
      const serverEntry = server.get(key)
      const localEntry = local[key]
      if (
        serverEntry &&
        localEntry &&
        serverEntry.recipeId === localEntry.recipeId &&
        serverEntry.portions === localEntry.portions
      ) {
        continue
      }
      const [day, mealType] = key.split(':')
      diff.push({
        day: Number(day),
        mealType,
        serverName: serverEntry?.recipeName ?? null,
        serverPortions: serverEntry?.portions ?? null,
        localName: localEntry?.recipeName ?? null,
        localPortions: localEntry?.portions ?? null
      })
    }
    return diff.sort((a, b) => a.day - b.day || a.mealType.localeCompare(b.mealType))
  })

  function slotEntry(day, mealType) {
    return draft.value[slotKey(day, mealType)]
  }

  function setSlot(day, mealType, entry) {
    draft.value = { ...draft.value, [slotKey(day, mealType)]: { ...entry } }
  }

  function removeSlot(day, mealType) {
    const key = slotKey(day, mealType)
    if (!(key in draft.value)) return
    const next = { ...draft.value }
    delete next[key]
    draft.value = next
  }

  function isCurrentLoad(requestId, requestedWeek) {
    return requestId === loadRequestId && requestedWeek === toIso(weekStart.value)
  }

  async function loadWeek() {
    const requestedWeek = toIso(weekStart.value)
    const requestId = ++loadRequestId
    loading.value = true
    loadError.value = ''
    savedMessage.value = ''
    saveError.value = ''
    try {
      const { response, data } = await loadWeekPlan(requestedWeek)
      if (!isCurrentLoad(requestId, requestedWeek)) return
      if (response.status === 200) {
        if (data == null) {
          // Тело ответа не разобралось — черновик не трогаем.
          loadError.value = LOAD_ERROR_MESSAGE
        } else {
          draft.value = draftFromEntries(data.entries)
          confirmedSnapshot.value = serializeDraft(draft.value)
          revision.value = data.revision ?? 0
          conflict.value = null
        }
      } else if (response.status === 404) {
        draft.value = {}
        confirmedSnapshot.value = serializeDraft({})
        revision.value = 0
        conflict.value = null
        loadError.value = data?.error || FAMILY_ERROR_MESSAGE
      } else {
        loadError.value = data?.error || LOAD_ERROR_MESSAGE
      }
    } catch {
      if (isCurrentLoad(requestId, requestedWeek)) loadError.value = LOAD_ERROR_MESSAGE
    } finally {
      if (isCurrentLoad(requestId, requestedWeek)) loading.value = false
    }
  }

  function confirmNavigation() {
    return !dirty.value || confirmLeave(LEAVE_MESSAGE)
  }

  async function goToWeek(offset) {
    if (!confirmNavigation()) return false
    weekStart.value = addDays(weekStart.value, offset * 7)
    await loadWeek()
    return true
  }

  async function save() {
    const savedWeek = toIso(weekStart.value)
    const sentSnapshot = serializeDraft(draft.value)
    const sentRevision = revision.value
    const requestId = ++saveRequestId
    saving.value = true
    savedMessage.value = ''
    saveError.value = ''
    conflict.value = null
    try {
      const { response, data } = await saveWeekPlanRequest(
        savedWeek,
        entriesFromDraft(draft.value),
        sentRevision
      )
      if (requestId !== saveRequestId) return
      if (response.status === 409) {
        // Устаревшая ревизия: черновик не трогаем, показываем серверную версию.
        if (savedWeek === toIso(weekStart.value)) {
          conflict.value = {
            revision: data?.revision ?? null,
            entries: data?.entries ?? []
          }
          saveError.value = data?.error || CONFLICT_MESSAGE
        }
        return
      }
      if (response.status !== 200) {
        saveError.value = data?.error || SAVE_ERROR_MESSAGE
        return
      }
      if (data == null) {
        // Ответ не разобрался: результат мутации неизвестен, черновик остаётся dirty.
        saveError.value = SAVE_ERROR_MESSAGE
        return
      }
      // Ответ относится к другой неделе — не отмечаем текущую сохранённой.
      if (savedWeek !== toIso(weekStart.value)) return
      revision.value = data.revision ?? sentRevision + 1
      if (serializeDraft(draft.value) === sentSnapshot) {
        draft.value = draftFromEntries(data.entries)
        confirmedSnapshot.value = serializeDraft(draft.value)
      } else {
        // Правки, сделанные после отправки, остаются dirty.
        confirmedSnapshot.value = sentSnapshot
      }
      savedMessage.value = 'План сохранён.'
    } catch {
      // Результат мутации неизвестен — ревизию не двигаем и черновик не чистим:
      // повторная отправка снова проверит ту же ожидаемую ревизию.
      if (requestId === saveRequestId) saveError.value = SAVE_ERROR_MESSAGE
    } finally {
      if (requestId === saveRequestId) saving.value = false
    }
  }

  async function reloadServerVersion() {
    if (!confirmLeave(RELOAD_CONFIRM_MESSAGE)) return false
    conflict.value = null
    await loadWeek()
    return true
  }

  return {
    weekStart,
    draft,
    revision,
    conflict,
    conflictDiff,
    dirty,
    loading,
    saving,
    loadError,
    saveError,
    savedMessage,
    slotEntry,
    setSlot,
    removeSlot,
    loadWeek,
    save,
    reloadServerVersion,
    goToWeek,
    confirmNavigation
  }
}
