import { ref, computed } from 'vue'
import { createRecipe, getRecipe, updateRecipe } from '../api/recipes'
import { parseList, joinList } from '../constants/recipe'

export const LEAVE_MESSAGE = 'Есть несохранённые изменения. Уйти без сохранения?'
export const LOAD_ERROR_MESSAGE =
  'Не удалось загрузить рецепт. Проверьте соединение и попробуйте снова.'
export const SAVE_ERROR_MESSAGE =
  'Не удалось сохранить рецепт. Проверьте соединение и повторите.'
export const NOT_FOUND_MESSAGE = 'Рецепт не найден.'
export const EMPTY_NAME_MESSAGE = 'Укажите название рецепта.'
export const CONFLICT_MESSAGE =
  'Рецепт изменён другим участником. Ваш черновик сохранён — загрузите актуальную версию, сравните изменения и сохраните снова.'

export function newIngredientDraft() {
  return { name: '', amount: '', unit: 'g', note: '' }
}

/** Пустой черновик нового рецепта: только поля, значимые для сохранения. */
export function emptyDraft() {
  return {
    name: '',
    description: '',
    cookTimeMinutes: 30,
    servings: 4,
    difficulty: 3,
    calories: '',
    tagsText: '',
    seasonality: [],
    dietText: '',
    steps: [''],
    ingredients: [newIngredientDraft()],
    photo: { existing: null, removed: false, selected: null }
  }
}

/** Снимок черновика по данным сервера. */
export function draftFromRecipe(data) {
  const calories = data.calories === null || data.calories === undefined ? '' : String(data.calories)
  return {
    name: data.name || '',
    description: data.description || '',
    cookTimeMinutes: data.cookTimeMinutes ?? 30,
    servings: data.servings ?? 4,
    difficulty: data.difficulty ?? 3,
    calories,
    tagsText: joinList(data.tags),
    seasonality: [...(data.seasonality || [])],
    dietText: joinList(data.diet),
    steps: data.steps && data.steps.length ? [...data.steps] : [''],
    ingredients:
      data.ingredients && data.ingredients.length
        ? data.ingredients.map((i) => ({
            name: i.name || '',
            amount: i.amount === null || i.amount === undefined ? '' : String(i.amount),
            unit: i.unit || 'g',
            note: i.note || ''
          }))
        : [newIngredientDraft()],
    photo: { existing: data.photoUrl || null, removed: false, selected: null }
  }
}

/**
 * Тело запроса на сохранение, построенное из черновика. Ожидаемая ревизия
 * передаётся отдельно: сервер отклонит сохранение, если основано на устаревшей.
 */
export function draftToPayload(draft, revision = null) {
  return {
    ...(revision === null || revision === undefined ? {} : { revision }),
    name: (draft.name || '').trim(),
    description: (draft.description || '').trim() || null,
    cookTimeMinutes: Number(draft.cookTimeMinutes),
    servings: Number(draft.servings),
    difficulty: Number(draft.difficulty),
    calories: isBlank(draft.calories) ? null : Number(draft.calories),
    tags: parseList(draft.tagsText),
    seasonality: [...(draft.seasonality || [])],
    diet: parseList(draft.dietText),
    steps: (draft.steps || [])
      .map((s) => (s || '').trim())
      .filter((s) => s.length > 0)
      .map((text) => ({ text })),
    ingredients: (draft.ingredients || [])
      .filter((i) => (i.name || '').trim() || i.amount || i.note)
      .map((i) => ({
        name: (i.name || '').trim(),
        amount: Number(i.amount),
        unit: i.unit,
        note: (i.note || '').trim() || null
      }))
  }
}

function isBlank(value) {
  return value === '' || value === null || value === undefined
}

function comparableNumber(value) {
  if (isBlank(value)) return null
  const n = Number(value)
  return Number.isFinite(n) ? n : null
}

/** Идентичность выбранного/существующего фото для сравнения черновиков. */
export function photoIdentity(value) {
  if (!value) return null
  if (typeof value === 'string') return value
  return `${value.name ?? ''}|${value.size ?? ''}|${value.lastModified ?? ''}`
}

/**
 * Каноническая сериализация черновика. Учитывает значения полей, порядок
 * ингредиентов и шагов, а также выбранное/удалённое фото. Пустые шаги и
 * ингредиенты игнорируются так же, как при сохранении, поэтому первичная
 * загрузка не создаёт ложный dirty.
 */
export function serializeDraft(draft) {
  return JSON.stringify({
    name: (draft.name || '').trim(),
    description: (draft.description || '').trim(),
    cookTimeMinutes: comparableNumber(draft.cookTimeMinutes),
    servings: comparableNumber(draft.servings),
    difficulty: comparableNumber(draft.difficulty),
    calories: isBlank(draft.calories) ? null : comparableNumber(draft.calories),
    tags: parseList(draft.tagsText),
    seasonality: [...(draft.seasonality || [])].sort(),
    diet: parseList(draft.dietText),
    steps: (draft.steps || []).map((s) => (s || '').trim()).filter((s) => s.length > 0),
    ingredients: (draft.ingredients || [])
      .filter((i) => (i.name || '').trim() || i.amount || i.note)
      .map((i) => ({
        name: (i.name || '').trim(),
        amount: comparableNumber(i.amount),
        unit: i.unit || '',
        note: (i.note || '').trim()
      })),
    photo: {
      existing: draft.photo?.existing || null,
      removed: Boolean(draft.photo?.removed),
      selected: photoIdentity(draft.photo?.selected)
    }
  })
}

/**
 * Черновик рецепта, привязанный к identity ресурса.
 *
 * Состояние сосредоточено вокруг трёх вещей: identity редактируемого ресурса
 * (`editingId`), актуального черновика (`draft`) и подтверждённого снимка
 * (`confirmedSnapshot`). Каждая загрузка и мутация привязаны к identity и
 * номеру запроса, поэтому поздний ответ старого рецепта не может подменить
 * содержимое нового, а старая мутация — очистить более новый черновик.
 */
export function useRecipeDraft(options = {}) {
  const loadRecipe = options.loadRecipe || getRecipe
  const createRecipeRequest = options.createRecipe || createRecipe
  const updateRecipeRequest = options.updateRecipe || updateRecipe
  const confirmLeave = options.confirm || ((message) => window.confirm(message))

  const editingId = ref(options.initialId ?? null)
  const draft = ref(emptyDraft())
  const confirmedSnapshot = ref(serializeDraft(draft.value))
  // Ревизия загруженного рецепта — ожидаемая версия для следующего сохранения.
  const revision = ref(null)
  // Конфликт ревизии: сообщение и актуальная серверная версия для сравнения.
  const conflictMessage = ref('')
  const conflictRevision = ref(null)

  const loading = ref(false)
  const saving = ref(false)
  const loadError = ref('')
  const saveError = ref('')
  const savedMessage = ref('')

  let loadRequestId = 0
  let saveRequestId = 0

  const isEdit = computed(() => Boolean(editingId.value))
  const dirty = computed(() => confirmedSnapshot.value !== serializeDraft(draft.value))

  function resetDraft() {
    draft.value = emptyDraft()
    confirmedSnapshot.value = serializeDraft(draft.value)
    revision.value = null
    conflictMessage.value = ''
    conflictRevision.value = null
  }

  function clearMessages() {
    loadError.value = ''
    saveError.value = ''
    savedMessage.value = ''
    conflictMessage.value = ''
    conflictRevision.value = null
  }

  /**
   * Смена identity ресурса. Сбрасывает черновик и сообщения и обесценивает
   * все запросы, привязанные к прежнему ресурсу.
   */
  function setIdentity(id) {
    const next = id ?? null
    if (next === editingId.value) return false
    editingId.value = next
    loadRequestId += 1
    saveRequestId += 1
    loading.value = false
    saving.value = false
    clearMessages()
    resetDraft()
    return true
  }

  function isCurrentLoad(requestId, requestedId) {
    return requestId === loadRequestId && requestedId === editingId.value
  }

  async function load() {
    const requestedId = editingId.value
    const requestId = ++loadRequestId
    loadError.value = ''
    savedMessage.value = ''
    saveError.value = ''
    conflictMessage.value = ''
    conflictRevision.value = null

    if (requestedId == null) {
      resetDraft()
      loading.value = false
      return
    }

    loading.value = true
    try {
      const { response, data } = await loadRecipe(requestedId)
      if (!isCurrentLoad(requestId, requestedId)) return
      if (response.status === 200) {
        if (data == null) {
          // Тело ответа не разобралось — черновик не трогаем.
          loadError.value = LOAD_ERROR_MESSAGE
          return
        }
        draft.value = draftFromRecipe(data)
        confirmedSnapshot.value = serializeDraft(draft.value)
        revision.value = Number.isInteger(data.revision) ? data.revision : null
      } else if (response.status === 404) {
        loadError.value = data?.error || NOT_FOUND_MESSAGE
      } else {
        loadError.value = data?.error || LOAD_ERROR_MESSAGE
      }
    } catch {
      if (isCurrentLoad(requestId, requestedId)) loadError.value = LOAD_ERROR_MESSAGE
    } finally {
      if (isCurrentLoad(requestId, requestedId)) loading.value = false
    }
  }

  function confirmNavigation() {
    return !dirty.value || confirmLeave(LEAVE_MESSAGE)
  }

  function validate() {
    if (!(draft.value.name || '').trim()) return EMPTY_NAME_MESSAGE
    return ''
  }

  /**
   * Сохранение. ID ресурса фиксируется вместе с отправляемыми данными, поэтому
   * смена маршрута во время запроса не приведёт к записи в другой рецепт.
   */
  async function save() {
    const targetId = editingId.value
    const sentSnapshot = serializeDraft(draft.value)
    const sentPayload = draftToPayload(draft.value, revision.value)
    const requestId = ++saveRequestId
    saving.value = true
    saveError.value = ''
    savedMessage.value = ''
    conflictMessage.value = ''
    conflictRevision.value = null
    try {
      const { response, data } = targetId
        ? await updateRecipeRequest(targetId, sentPayload)
        : await createRecipeRequest(sentPayload)

      if (requestId !== saveRequestId || targetId !== editingId.value) {
        return { ok: false, stale: true }
      }

      if (response.status === 200 || response.status === 201) {
        if (data == null) {
          // Ответ не разобрался: результат мутации неизвестен, черновик остаётся dirty.
          saveError.value = SAVE_ERROR_MESSAGE
          return { ok: false }
        }
        const savedId = data.id ?? targetId
        if (serializeDraft(draft.value) === sentSnapshot) {
          draft.value = draftFromRecipe(data)
          confirmedSnapshot.value = serializeDraft(draft.value)
        } else {
          // Правки, сделанные после отправки, остаются dirty.
          confirmedSnapshot.value = sentSnapshot
        }
        revision.value = Number.isInteger(data.revision) ? data.revision : revision.value
        savedMessage.value = 'Рецепт сохранён.'
        return { ok: true, id: savedId, data }
      }

      if (response.status === 409) {
        // Конфликт ревизий: локальный черновик не трогаем, даём сравнить с актуальным.
        conflictRevision.value = Number.isInteger(data?.revision) ? data.revision : null
        conflictMessage.value = data?.error || CONFLICT_MESSAGE
        return { ok: false, conflict: true }
      }

      saveError.value = data?.error || SAVE_ERROR_MESSAGE
      return { ok: false }
    } catch {
      if (requestId === saveRequestId && targetId === editingId.value) {
        saveError.value = SAVE_ERROR_MESSAGE
      }
      return { ok: false }
    } finally {
      if (requestId === saveRequestId) saving.value = false
    }
  }

  return {
    editingId,
    isEdit,
    draft,
    dirty,
    loading,
    saving,
    revision,
    conflictMessage,
    conflictRevision,
    loadError,
    saveError,
    savedMessage,
    setIdentity,
    load,
    save,
    validate,
    confirmNavigation,
    resetDraft
  }
}
