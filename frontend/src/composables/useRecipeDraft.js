import { ref, computed } from 'vue'
import {
  createRecipe,
  getRecipe,
  updateRecipe,
  uploadRecipePhoto,
  deleteRecipePhoto
} from '../api/recipes'
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
export const PHOTO_ERROR_MESSAGE =
  'Рецепт сохранён, но фото не удалось сохранить. Повторите действие с фото.'
export const PHOTO_UNKNOWN_MESSAGE =
  'Рецепт сохранён, но результат действия с фото неизвестен из-за обрыва связи. Повторите — действие защищено от дубля.'

let ingredientSequence = 0

/**
 * Стабильная локальная identity строки ингредиента. Не участвует в сериализации
 * черновика и в запросе на сохранение — нужна, чтобы таймеры и ответы
 * автодополнения были привязаны к строке, а не к её индексу.
 */
export function nextIngredientId() {
  ingredientSequence += 1
  return `ingredient-${ingredientSequence}`
}

export function newIngredientDraft() {
  return { uid: nextIngredientId(), name: '', amount: '', unit: 'g', note: '' }
}

/** Пустое фото-состояние черновика. */
export function emptyPhoto() {
  return { existing: null, removed: false, selected: null }
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
    photo: emptyPhoto()
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
            uid: nextIngredientId(),
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

/** Сигнатура текстовой части черновика без учёта фото. */
export function textSignature(draft) {
  return serializeDraft({ ...draft, photo: emptyPhoto() })
}

/**
 * Одно действие с фото, зафиксированное в начале submit: оставить, загрузить/
 * заменить либо удалить. Поздний выбор файла не подменяет уже отправленное
 * действие, потому что intent хранит ссылку на файл, актуальную на момент submit.
 */
export function photoIntentFromDraft(draft) {
  const photo = draft.photo || {}
  if (photo.selected) return { kind: 'upload', file: photo.selected }
  if (photo.removed && photo.existing) return { kind: 'delete' }
  return { kind: 'keep' }
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
  const uploadPhotoRequest = options.uploadPhoto || uploadRecipePhoto
  const deletePhotoRequest = options.deletePhoto || deleteRecipePhoto
  const confirmLeave = options.confirm || ((message) => window.confirm(message))

  const editingId = ref(options.initialId ?? null)
  const draft = ref(emptyDraft())
  const confirmedSnapshot = ref(serializeDraft(draft.value))
  // Ревизия загруженного рецепта — ожидаемая версия для следующего сохранения.
  const revision = ref(null)
  // Конфликт ревизии: сообщение и актуальная серверная версия для сравнения.
  const conflictMessage = ref('')
  const conflictRevision = ref(null)
  // Действие с фото, зафиксированное в начале submit: id, ожидаемая ревизия и
  // файл/намерение. Живёт вне ref — это не состояние формы, а замороженная
  // команда. Поздний выбор файла её не подменяет.
  let pendingPhoto = null
  // Серверный текст, сохранённый последним успешным PUT/POST (без применённого
  // фото). База для подтверждённого снимка, когда ответ фото без тела (DELETE).
  let lastServerTextDraft = null

  const photoSaving = ref(false)
  // Текст сохранён, а действие с фото — ещё нет: честный частичный результат.
  const photoPartial = ref(false)
  // Результат последней попытки фото неизвестен (обрыв/неразобранный ответ):
  // повтор сначала проверяет актуальное фото, а не отправляет слепой дубль.
  const photoUnknown = ref(false)
  const photoError = ref('')

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
    clearPhotoState()
  }

  function clearPhotoState() {
    pendingPhoto = null
    lastServerTextDraft = null
    photoSaving.value = false
    photoPartial.value = false
    photoUnknown.value = false
    photoError.value = ''
  }

  function clearMessages() {
    loadError.value = ''
    saveError.value = ''
    savedMessage.value = ''
    conflictMessage.value = ''
    conflictRevision.value = null
    clearPhotoState()
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
    clearPhotoState()

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
    // Несохранённое действие с фото — тоже потеря, даже если текст уже сохранён.
    return (!dirty.value && !pendingPhoto) || confirmLeave(LEAVE_MESSAGE)
  }

  function validate() {
    if (!(draft.value.name || '').trim()) return EMPTY_NAME_MESSAGE
    return ''
  }

  /**
   * Заменить текстовую часть черновика серверными значениями после успешного
   * сохранения, сохранив выбранное, но ещё не применённое действие с фото.
   * Правки, сделанные во время запроса, остаются новым dirty-черновиком.
   */
  function applySavedText(editedDuringSave) {
    const serverDraft = lastServerTextDraft
    if (!editedDuringSave) {
      draft.value = { ...serverDraft, photo: { ...draft.value.photo } }
    }
    // Подтверждённый снимок — реальное состояние сервера: текст сохранён, фото
    // пока прежнее (действие с фото ещё не применено).
    confirmedSnapshot.value = serializeDraft(serverDraft)
  }

  /** Фото-запрос всё ещё принадлежит текущему ресурсу (не устарел после ухода). */
  function isCurrentPhoto(target) {
    return pendingPhoto === target && target.id === editingId.value
  }

  /** Совпадает ли текущее фото-состояние черновика с замороженным intent. */
  function photoMatchesIntent(photo, intent) {
    if (!intent) return true
    if (intent.kind === 'upload') {
      return Boolean(photo.selected) && photoIdentity(photo.selected) === photoIdentity(intent.file)
    }
    if (intent.kind === 'delete') {
      return Boolean(photo.removed) && !photo.selected
    }
    return true
  }

  /**
   * Применить серверное фото к черновику и подтверждённому снимку. Поздний выбор
   * другого файла не затирается — он остаётся новым dirty-черновиком.
   */
  function applySavedPhoto(data) {
    const intent = pendingPhoto
    const latePhotoChange = !photoMatchesIntent(draft.value.photo, intent)
    const serverDraft = data ? draftFromRecipe(data) : null
    const serverPhoto = serverDraft ? serverDraft.photo : emptyPhoto()
    if (!latePhotoChange) {
      draft.value = { ...draft.value, photo: { ...serverPhoto } }
    } else {
      // Поздний выбор файла не затираем, но подтягиваем актуальный URL с сервера,
      // чтобы снятие позднего выбора возвращало черновик к подтверждённому виду.
      draft.value = {
        ...draft.value,
        photo: { ...draft.value.photo, existing: serverPhoto.existing }
      }
    }
    if (serverDraft) {
      confirmedSnapshot.value = serializeDraft(serverDraft)
    } else if (lastServerTextDraft) {
      confirmedSnapshot.value = serializeDraft({ ...lastServerTextDraft, photo: emptyPhoto() })
    }
  }

  /**
   * Сохранение. ID ресурса и одно действие с фото фиксируются до первого await,
   * поэтому смена маршрута или поздний выбор файла не подменяют отправленное.
   * При успехе текста и сбое фото возвращается честный частичный результат.
   */
  async function save() {
    // Текст уже сохранён, а изменилось только фото — не перезаписываем поля,
    // а повторяем исключительно действие с фото (в т.ч. защищённо после обрыва).
    if (
      pendingPhoto &&
      lastServerTextDraft &&
      textSignature(draft.value) === textSignature(lastServerTextDraft)
    ) {
      saving.value = true
      saveError.value = ''
      savedMessage.value = ''
      try {
        photoPartial.value = true
        const photoResult = await retryPhoto()
        if (photoResult.ok) return { ok: true, id: editingId.value, data: photoResult.data }
        return {
          ok: false,
          textSaved: true,
          id: editingId.value,
          photoFailed: true,
          conflict: Boolean(photoResult.conflict)
        }
      } finally {
        saving.value = false
      }
    }

    const targetId = editingId.value
    const sentSnapshot = serializeDraft(draft.value)
    const sentPayload = draftToPayload(draft.value, revision.value)
    const intent = photoIntentFromDraft(draft.value)
    const requestId = ++saveRequestId
    saving.value = true
    saveError.value = ''
    savedMessage.value = ''
    conflictMessage.value = ''
    conflictRevision.value = null
    clearPhotoState()
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
        const editedDuringSave = serializeDraft(draft.value) !== sentSnapshot
        lastServerTextDraft = draftFromRecipe(data)
        applySavedText(editedDuringSave)
        revision.value = Number.isInteger(data.revision) ? data.revision : revision.value
        // Новый рецепт теперь существует: привязываем черновик к его id, чтобы
        // повтор фото обновлял его, а не создавал второй.
        if (targetId == null && savedId) editingId.value = savedId
        savedMessage.value = 'Рецепт сохранён.'

        if (intent.kind === 'keep') return { ok: true, id: savedId, data }

        // Текст уже сохранён: фото — отдельное действие, его сбой не создаёт
        // второй рецепт и не теряет сохранённые поля.
        pendingPhoto = {
          kind: intent.kind,
          file: intent.file,
          id: savedId,
          revision: revision.value
        }
        photoPartial.value = true
        const photoResult = await performPhoto()
        if (photoResult.ok) return { ok: true, id: savedId, data: photoResult.data }
        return {
          ok: false,
          textSaved: true,
          id: savedId,
          photoFailed: true,
          conflict: Boolean(photoResult.conflict)
        }
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

  /**
   * Применить замороженное действие с фото к уже сохранённому рецепту.
   * Известный отказ сервера (4xx/5xx с разобранным телом) — повторяемо.
   * Обрыв связи или неразобранный ответ — результат неизвестен: повтор пойдёт
   * через проверку актуального фото, а не слепым дублем.
   */
  async function performPhoto() {
    const target = pendingPhoto
    if (!target) return { ok: true }
    photoSaving.value = true
    photoError.value = ''
    photoUnknown.value = false
    try {
      const result =
        target.kind === 'delete'
          ? await deletePhotoRequest(target.id, target.revision)
          : await uploadPhotoRequest(target.id, target.file, target.revision)
      if (!isCurrentPhoto(target)) return { ok: false, stale: true }
      const status = result.response.status
      if (status === 204 || status === 200) {
        if (target.kind === 'upload' && result.data == null) {
          photoUnknown.value = true
          photoError.value = PHOTO_UNKNOWN_MESSAGE
          photoPartial.value = true
          return { ok: false, unknown: true }
        }
        applySavedPhoto(result.data)
        // Успешное фото подняло ревизию: без тела ответа (DELETE) она ровно на шаг.
        revision.value = Number.isInteger(result.data?.revision)
          ? result.data.revision
          : target.revision + 1
        pendingPhoto = null
        photoPartial.value = false
        return { ok: true, data: result.data }
      }
      if (status === 409) {
        conflictRevision.value = Number.isInteger(result.data?.revision)
          ? result.data.revision
          : null
        conflictMessage.value = result.data?.error || CONFLICT_MESSAGE
        photoPartial.value = true
        return { ok: false, conflict: true }
      }
      const serverError = result.data?.error
      photoError.value = serverError ? `${PHOTO_ERROR_MESSAGE} ${serverError}` : PHOTO_ERROR_MESSAGE
      photoPartial.value = true
      return { ok: false }
    } catch {
      if (!isCurrentPhoto(target)) return { ok: false, stale: true }
      photoUnknown.value = true
      photoError.value = PHOTO_UNKNOWN_MESSAGE
      photoPartial.value = true
      return { ok: false, unknown: true }
    } finally {
      if (isCurrentPhoto(target)) photoSaving.value = false
    }
  }

  /**
   * Защищённое повторение после неизвестного результата: сначала читаем актуальное
   * фото. Удаление видно однозначно (фото уже нет) — повтор не нужен; для загрузки
   * обновляем ожидаемую ревизию и повторяем идемпотентный PUT.
   */
  async function verifyPendingPhoto() {
    const target = pendingPhoto
    if (!target) return { ok: true }
    try {
      const { response, data } = await loadRecipe(target.id)
      if (!isCurrentPhoto(target)) return { ok: false, stale: true }
      if (response.status !== 200 || data == null) return { ok: false }
      if (target.kind === 'delete' && !data.photoUrl) {
        lastServerTextDraft = draftFromRecipe(data)
        applySavedPhoto(data)
        // DELETE без тела: сервер уже поднял ревизию — фиксируем её для следующего сохранения.
        revision.value = Number.isInteger(data.revision) ? data.revision : revision.value
        pendingPhoto = null
        photoPartial.value = false
        photoUnknown.value = false
        return { ok: true, data }
      }
      target.revision = Number.isInteger(data.revision) ? data.revision : target.revision
      return { ok: false }
    } catch {
      return { ok: false }
    }
  }

  /** Повтор только действия с фото для уже сохранённого рецепта. */
  async function retryPhoto() {
    if (!pendingPhoto) return { ok: true }
    if (photoUnknown.value) {
      const applied = await verifyPendingPhoto()
      if (applied.ok) return applied
    }
    return performPhoto()
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
    photoSaving,
    photoPartial,
    photoUnknown,
    photoError,
    loadError,
    saveError,
    savedMessage,
    setIdentity,
    load,
    save,
    retryPhoto,
    validate,
    confirmNavigation,
    resetDraft
  }
}
