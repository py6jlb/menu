import { describe, it, expect, vi } from 'vitest'
import {
  useRecipeDraft,
  newIngredientDraft,
  serializeDraft,
  draftToPayload,
  photoIntentFromDraft,
  LOAD_ERROR_MESSAGE,
  SAVE_ERROR_MESSAGE,
  EMPTY_NAME_MESSAGE,
  PHOTO_ERROR_MESSAGE,
  PHOTO_UNKNOWN_MESSAGE
} from './useRecipeDraft'

function deferred() {
  let resolve
  let reject
  const promise = new Promise((res, rej) => {
    resolve = res
    reject = rej
  })
  return { promise, resolve, reject }
}

function ok(data) {
  return { response: { status: 200 }, data }
}

function created(data) {
  return { response: { status: 201 }, data }
}

function recipeData(id, overrides = {}) {
  return {
    id,
    name: `Рецепт ${id}`,
    description: 'описание',
    cookTimeMinutes: 30,
    servings: 4,
    difficulty: 3,
    calories: 100,
    tags: ['суп'],
    seasonality: ['winter'],
    diet: [],
    steps: ['первый', 'второй'],
    ingredients: [
      { name: 'лук', amount: 2, unit: 'pcs', note: null },
      { name: 'соль', amount: 1, unit: 'g', note: 'по вкусу' }
    ],
    photoUrl: `/photos/${id}.jpg`,
    revision: 1,
    ...overrides
  }
}

function make(options = {}) {
  return useRecipeDraft({
    initialId: null,
    confirm: () => true,
    loadRecipe: vi.fn(),
    createRecipe: vi.fn(),
    updateRecipe: vi.fn(),
    ...options
  })
}

describe('useRecipeDraft — lifecycle загрузки', () => {
  it('A/edit → B/edit: поздний ответ старого рецепта не подменяет новый', async () => {
    const slowA = deferred()
    const fastB = deferred()
    const loadRecipe = vi
      .fn()
      .mockImplementationOnce(() => slowA.promise)
      .mockImplementationOnce(() => fastB.promise)

    const state = make({ initialId: 'A', loadRecipe })

    const first = state.load()
    state.setIdentity('B')
    const second = state.load()

    fastB.resolve(ok(recipeData('B', { name: 'B-рецепт' })))
    await second

    slowA.resolve(ok(recipeData('A', { name: 'A-рецепт' })))
    await first

    expect(state.editingId.value).toBe('B')
    expect(state.draft.value.name).toBe('B-рецепт')
    expect(state.dirty.value).toBe(false)
    expect(state.loading.value).toBe(false)
  })

  it('edit → new: переход к новому очищает черновик и не переносит данные A', async () => {
    const slowA = deferred()
    const state = make({
      initialId: 'A',
      loadRecipe: vi.fn().mockImplementationOnce(() => slowA.promise)
    })

    const first = state.load()
    state.setIdentity(null)
    await state.load()

    slowA.resolve(ok(recipeData('A')))
    await first

    expect(state.isEdit.value).toBe(false)
    expect(state.draft.value.name).toBe('')
    expect(state.draft.value.photo.existing).toBeNull()
    expect(state.loadError.value).toBe('')
    expect(state.dirty.value).toBe(false)
  })

  it('фото и ошибки старого ресурса не переносятся в новый', async () => {
    const slowA = deferred()
    const fastB = deferred()
    const loadRecipe = vi
      .fn()
      .mockImplementationOnce(() => slowA.promise)
      .mockImplementationOnce(() => fastB.promise)

    const state = make({ initialId: 'A', loadRecipe })
    const first = state.load()
    state.setIdentity('B')
    const second = state.load()

    fastB.resolve(ok(recipeData('B', { photoUrl: null })))
    await second
    expect(state.draft.value.photo.existing).toBeNull()

    slowA.resolve(ok(recipeData('A', { photoUrl: '/a.jpg' })))
    await first

    expect(state.draft.value.photo.existing).toBeNull()
    expect(state.loadError.value).toBe('')
  })

  it('первичная загрузка не создаёт ложный dirty', async () => {
    const state = make({
      initialId: 'A',
      loadRecipe: vi.fn().mockResolvedValue(ok(recipeData('A')))
    })

    await state.load()

    expect(state.dirty.value).toBe(false)
    expect(state.loadError.value).toBe('')
  })

  it('сетевой отказ загрузки завершает pending, даёт сообщение и сохраняет введённое', async () => {
    const state = make({
      initialId: 'A',
      loadRecipe: vi.fn().mockRejectedValue(new Error('network'))
    })
    state.draft.value.name = 'мой черновик'

    await state.load()

    expect(state.loading.value).toBe(false)
    expect(state.loadError.value).toBe(LOAD_ERROR_MESSAGE)
    expect(state.draft.value.name).toBe('мой черновик')
  })

  it('неразобранный ответ загрузки не очищает черновик', async () => {
    const state = make({
      initialId: 'A',
      loadRecipe: vi.fn().mockResolvedValue({ response: { status: 200 }, data: null })
    })
    state.draft.value.name = 'мой черновик'

    await state.load()

    expect(state.loading.value).toBe(false)
    expect(state.loadError.value).toBe(LOAD_ERROR_MESSAGE)
    expect(state.draft.value.name).toBe('мой черновик')
  })

  it('404 даёт понятное сообщение и завершает pending', async () => {
    const state = make({
      initialId: 'A',
      loadRecipe: vi.fn().mockResolvedValue({ response: { status: 404 }, data: null })
    })

    await state.load()

    expect(state.loading.value).toBe(false)
    expect(state.loadError.value).toMatch(/не найден/i)
  })
})

describe('useRecipeDraft — dirty', () => {
  async function loaded(overrides) {
    const state = make({
      initialId: 'A',
      loadRecipe: vi.fn().mockResolvedValue(ok(recipeData('A', overrides)))
    })
    await state.load()
    return state
  }

  it('изменение поля делает черновик грязным, возврат значения — чистым', async () => {
    const state = await loaded()
    expect(state.dirty.value).toBe(false)

    state.draft.value.name = 'Другое имя'
    expect(state.dirty.value).toBe(true)

    state.draft.value.name = 'Рецепт A'
    expect(state.dirty.value).toBe(false)
  })

  it('учитывает порядок ингредиентов и шагов', async () => {
    const state = await loaded()
    const [a, b] = state.draft.value.ingredients

    state.draft.value.ingredients = [b, a]
    expect(state.dirty.value).toBe(true)

    state.draft.value.ingredients = [a, b]
    expect(state.dirty.value).toBe(false)

    const [s1, s2] = state.draft.value.steps
    state.draft.value.steps = [s2, s1]
    expect(state.dirty.value).toBe(true)

    state.draft.value.steps = [s1, s2]
    expect(state.dirty.value).toBe(false)
  })

  it('учитывает выбранное и удалённое фото', async () => {
    const state = await loaded()

    state.draft.value.photo.selected = { name: 'a.jpg', size: 10, lastModified: 1 }
    expect(state.dirty.value).toBe(true)

    state.draft.value.photo.selected = null
    expect(state.dirty.value).toBe(false)

    state.draft.value.photo.removed = true
    expect(state.dirty.value).toBe(true)

    state.draft.value.photo.removed = false
    expect(state.dirty.value).toBe(false)
  })

  it('пустые новые шаги и строки ингредиентов не создают ложный dirty', async () => {
    const state = await loaded()

    state.draft.value.steps.push('')
    state.draft.value.ingredients.push({ name: '', amount: '', unit: 'g', note: '' })
    expect(state.dirty.value).toBe(false)

    state.draft.value.steps[state.draft.value.steps.length - 1] = 'новый шаг'
    expect(state.dirty.value).toBe(true)
  })
})

describe('useRecipeDraft — сохранение', () => {
  it('редактирование фиксирует ID ресурса вместе с отправляемыми данными', async () => {
    const updateRecipe = vi.fn().mockResolvedValue(ok(recipeData('A', { name: 'Новое' })))
    const state = make({
      initialId: 'A',
      loadRecipe: vi.fn().mockResolvedValue(ok(recipeData('A'))),
      updateRecipe
    })
    await state.load()
    state.draft.value.name = 'Новое'

    const result = await state.save()

    expect(updateRecipe).toHaveBeenCalledWith('A', expect.objectContaining({ name: 'Новое' }))
    expect(result).toMatchObject({ ok: true, id: 'A' })
    expect(state.dirty.value).toBe(false)
    expect(state.savedMessage.value).toBe('Рецепт сохранён.')
  })

  it('создание нового рецепта отправляет POST и возвращает новый ID', async () => {
    const createRecipe = vi.fn().mockResolvedValue(created(recipeData('NEW')))
    const state = make({ createRecipe })
    state.draft.value.name = 'Новый'

    const result = await state.save()

    expect(createRecipe).toHaveBeenCalledTimes(1)
    expect(result).toMatchObject({ ok: true, id: 'NEW' })
  })

  it('правка во время сохранения остаётся dirty и не теряется', async () => {
    const pending = deferred()
    const updateRecipe = vi.fn().mockImplementation(() => pending.promise)
    const state = make({
      initialId: 'A',
      loadRecipe: vi.fn().mockResolvedValue(ok(recipeData('A'))),
      updateRecipe
    })
    await state.load()
    state.draft.value.name = 'первое'

    const savePromise = state.save()
    state.draft.value.description = 'второе'
    pending.resolve(ok(recipeData('A', { name: 'первое', description: 'описание' })))
    await savePromise

    expect(state.savedMessage.value).toBe('Рецепт сохранён.')
    expect(state.dirty.value).toBe(true)
    expect(state.draft.value.description).toBe('второе')
  })

  it('старая мутация не очищает более новый черновик другого ресурса', async () => {
    const pending = deferred()
    const updateRecipe = vi.fn().mockImplementation(() => pending.promise)
    const state = make({ initialId: 'A', updateRecipe })
    state.draft.value.name = 'правка A'

    const savePromise = state.save()
    state.setIdentity('B')
    state.draft.value.name = 'черновик B'

    pending.resolve(ok(recipeData('A', { name: 'A saved' })))
    const result = await savePromise

    expect(result).toMatchObject({ ok: false, stale: true })
    expect(state.editingId.value).toBe('B')
    expect(state.draft.value.name).toBe('черновик B')
    expect(state.dirty.value).toBe(true)
    expect(state.saving.value).toBe(false)
  })

  it('сетевой отказ сохранения завершает pending, не чистит черновик и не повторяет запрос', async () => {
    const updateRecipe = vi.fn().mockRejectedValue(new Error('timeout'))
    const state = make({
      initialId: 'A',
      loadRecipe: vi.fn().mockResolvedValue(ok(recipeData('A'))),
      updateRecipe
    })
    await state.load()
    state.draft.value.name = 'важное'

    await state.save()

    expect(state.saving.value).toBe(false)
    expect(state.saveError.value).toBe(SAVE_ERROR_MESSAGE)
    expect(state.draft.value.name).toBe('важное')
    expect(state.dirty.value).toBe(true)
    expect(updateRecipe).toHaveBeenCalledTimes(1)
  })

  it('неразобранный ответ сохранения не очищает черновик', async () => {
    const state = make({
      initialId: 'A',
      loadRecipe: vi.fn().mockResolvedValue(ok(recipeData('A'))),
      updateRecipe: vi.fn().mockResolvedValue({ response: { status: 200 }, data: null })
    })
    await state.load()
    state.draft.value.name = 'важное'

    await state.save()

    expect(state.saving.value).toBe(false)
    expect(state.saveError.value).toBe(SAVE_ERROR_MESSAGE)
    expect(state.dirty.value).toBe(true)
  })

  it('HTTP-ошибка сохранения даёт понятное сообщение и остаётся dirty', async () => {
    const state = make({
      initialId: 'A',
      loadRecipe: vi.fn().mockResolvedValue(ok(recipeData('A'))),
      updateRecipe: vi.fn().mockResolvedValue({ response: { status: 500 }, data: null })
    })
    await state.load()
    state.draft.value.name = 'важное'

    await state.save()

    expect(state.saveError.value).toBe(SAVE_ERROR_MESSAGE)
    expect(state.dirty.value).toBe(true)
  })

  it('валидация не отправляет пустое название', async () => {
    const updateRecipe = vi.fn()
    const state = make({ initialId: 'A', updateRecipe })
    const message = state.validate()

    expect(message).toBe(EMPTY_NAME_MESSAGE)
    expect(updateRecipe).not.toHaveBeenCalled()
  })
})

describe('useRecipeDraft — конкурентное редактирование', () => {
  it('сохранение передаёт ожидаемую ревизию и принимает новую из ответа', async () => {
    const updateRecipe = vi.fn().mockResolvedValue(ok(recipeData('A', { name: 'Новое', revision: 2 })))
    const state = make({
      initialId: 'A',
      loadRecipe: vi.fn().mockResolvedValue(ok(recipeData('A', { revision: 1 }))),
      updateRecipe
    })
    await state.load()
    state.draft.value.name = 'Новое'

    const result = await state.save()

    expect(updateRecipe).toHaveBeenCalledWith('A', expect.objectContaining({ name: 'Новое', revision: 1 }))
    expect(result).toMatchObject({ ok: true })
    expect(state.revision.value).toBe(2)
    expect(state.dirty.value).toBe(false)
  })

  it('конфликт ревизии сохраняет локальный черновик и не перезаписывает его', async () => {
    const state = make({
      initialId: 'A',
      loadRecipe: vi.fn().mockResolvedValue(ok(recipeData('A', { revision: 1 }))),
      updateRecipe: vi.fn().mockResolvedValue({
        response: { status: 409 },
        data: { error: 'Рецепт изменён другим участником.', revision: 4 }
      })
    })
    await state.load()
    state.draft.value.name = 'моя правка'

    const result = await state.save()

    expect(result).toMatchObject({ ok: false, conflict: true })
    expect(state.draft.value.name).toBe('моя правка')
    expect(state.dirty.value).toBe(true)
    expect(state.conflictRevision.value).toBe(4)
    expect(state.conflictMessage.value).toContain('другим участником')
    expect(state.saveError.value).toBe('')
    expect(state.saving.value).toBe(false)
  })

  it('смена ресурса сбрасывает ревизию и состояние конфликта', async () => {
    const state = make({
      initialId: 'A',
      loadRecipe: vi.fn().mockResolvedValue(ok(recipeData('A', { revision: 3 }))),
      updateRecipe: vi.fn().mockResolvedValue({
        response: { status: 409 },
        data: { revision: 5 }
      })
    })
    await state.load()
    state.draft.value.name = 'правка'
    await state.save()
    expect(state.conflictRevision.value).toBe(5)

    state.setIdentity('B')

    expect(state.revision.value).toBeNull()
    expect(state.conflictMessage.value).toBe('')
    expect(state.conflictRevision.value).toBeNull()
  })

  it('draftToPayload добавляет ревизию только когда она известна', () => {
    const draft = { name: 'a', steps: [], ingredients: [] }
    expect(draftToPayload(draft).revision).toBeUndefined()
    expect(draftToPayload(draft, 7).revision).toBe(7)
  })
})

describe('photoIntentFromDraft', () => {
  it('различает оставить, загрузить и удалить фото', () => {
    const file = { name: 'a.jpg', size: 1, lastModified: 1 }
    expect(
      photoIntentFromDraft({ photo: { existing: '/a.jpg', removed: false, selected: null } })
    ).toEqual({ kind: 'keep' })
    expect(photoIntentFromDraft({ photo: { existing: '/a.jpg', removed: false, selected: file } })).toEqual({
      kind: 'upload',
      file
    })
    expect(photoIntentFromDraft({ photo: { existing: '/a.jpg', removed: true, selected: null } })).toEqual({
      kind: 'delete'
    })
  })
})

describe('useRecipeDraft — действия с фото', () => {
  function photoOptions(overrides = {}) {
    return {
      initialId: 'A',
      loadRecipe: vi.fn().mockResolvedValue(ok(recipeData('A'))),
      updateRecipe: vi.fn().mockResolvedValue(ok(recipeData('A', { revision: 2 }))),
      uploadPhoto: vi.fn(),
      deletePhoto: vi.fn(),
      ...overrides
    }
  }

  const file = { name: 'a.jpg', size: 1, lastModified: 1, type: 'image/jpeg' }

  it('сохранение без изменения фото не отправляет фото-запрос', async () => {
    const uploadPhoto = vi.fn()
    const deletePhoto = vi.fn()
    const state = make(photoOptions({ uploadPhoto, deletePhoto }))
    await state.load()
    state.draft.value.name = 'Новое'

    const result = await state.save()

    expect(result).toMatchObject({ ok: true, id: 'A' })
    expect(uploadPhoto).not.toHaveBeenCalled()
    expect(deletePhoto).not.toHaveBeenCalled()
    expect(state.photoPartial.value).toBe(false)
    expect(state.photoError.value).toBe('')
  })

  it('загрузка фото после сохранения текста применяет серверное фото', async () => {
    const uploadPhoto = vi.fn().mockResolvedValue({
      response: { status: 200 },
      data: recipeData('A', { revision: 3, photoUrl: '/photos/new.jpg' })
    })
    const state = make(photoOptions({ uploadPhoto }))
    await state.load()
    state.draft.value.name = 'Новое'
    state.draft.value.photo.selected = file

    const result = await state.save()

    expect(uploadPhoto).toHaveBeenCalledWith('A', file, 2)
    expect(result).toMatchObject({ ok: true, id: 'A' })
    expect(state.draft.value.photo.existing).toBe('/photos/new.jpg')
    expect(state.draft.value.photo.selected).toBeNull()
    expect(state.dirty.value).toBe(false)
    expect(state.photoPartial.value).toBe(false)
    expect(state.photoError.value).toBe('')
  })

  it('успешное фото поднимает ревизию, чтобы следующее сохранение не конфликтовало', async () => {
    const uploadPhoto = vi.fn().mockResolvedValue({
      response: { status: 200 },
      data: recipeData('A', { revision: 3, photoUrl: '/photos/new.jpg' })
    })
    const state = make(photoOptions({ uploadPhoto }))
    await state.load()
    state.draft.value.name = 'Новое'
    state.draft.value.photo.selected = file

    await state.save()

    expect(state.revision.value).toBe(3)

    const deletePhoto = vi.fn().mockResolvedValue({ response: { status: 204 }, data: null })
    const second = make(photoOptions({ deletePhoto }))
    await second.load()
    second.draft.value.name = 'Новое'
    second.draft.value.photo.removed = true

    await second.save()

    // DELETE отвечает 204 без тела — ревизия растёт ровно на шаг.
    expect(second.revision.value).toBe(3)
  })

  it('сбой фото после успешного текста даёт частичный результат и повтор только фото', async () => {
    const uploadPhoto = vi
      .fn()
      .mockResolvedValueOnce({ response: { status: 500 }, data: { error: 'Сбой.' } })
      .mockResolvedValueOnce({
        response: { status: 200 },
        data: recipeData('A', { revision: 3, photoUrl: '/photos/new.jpg' })
      })
    const state = make(photoOptions({ uploadPhoto }))
    await state.load()
    state.draft.value.name = 'Новое'
    state.draft.value.photo.selected = file

    const result = await state.save()

    expect(result).toMatchObject({ ok: false, textSaved: true, id: 'A' })
    expect(state.photoPartial.value).toBe(true)
    expect(state.photoError.value).toContain('фото')
    expect(state.photoError.value).toContain('Сбой.')
    expect(state.draft.value.photo.selected).toEqual(file)
    expect(state.dirty.value).toBe(true)
    expect(state.saving.value).toBe(false)

    const retried = await state.retryPhoto()

    expect(retried.ok).toBe(true)
    expect(uploadPhoto).toHaveBeenCalledTimes(2)
    expect(state.photoPartial.value).toBe(false)
    expect(state.dirty.value).toBe(false)
    expect(state.draft.value.photo.existing).toBe('/photos/new.jpg')
  })

  it('повтор фото для нового рецепта не создаёт второй рецепт и не переписывает поля', async () => {
    const createRecipe = vi.fn().mockResolvedValue(created(recipeData('NEW', { revision: 1 })))
    const updateRecipe = vi.fn()
    const uploadPhoto = vi
      .fn()
      .mockResolvedValueOnce({ response: { status: 500 }, data: { error: 'Сбой.' } })
      .mockResolvedValueOnce({
        response: { status: 200 },
        data: recipeData('NEW', { revision: 2, photoUrl: '/photos/new.jpg' })
      })
    const state = make({ createRecipe, updateRecipe, uploadPhoto })
    state.draft.value.name = 'Новый'
    state.draft.value.photo.selected = file

    const result = await state.save()

    expect(createRecipe).toHaveBeenCalledTimes(1)
    expect(result).toMatchObject({ ok: false, textSaved: true, id: 'NEW' })
    expect(state.editingId.value).toBe('NEW')

    await state.retryPhoto()

    expect(createRecipe).toHaveBeenCalledTimes(1)
    expect(updateRecipe).not.toHaveBeenCalled()
    expect(uploadPhoto).toHaveBeenLastCalledWith('NEW', file, 1)
    expect(state.dirty.value).toBe(false)
  })

  it('удаление фото идёт через deletePhoto и очищает partial', async () => {
    const deletePhoto = vi.fn().mockResolvedValue({ response: { status: 204 }, data: null })
    const state = make(photoOptions({ deletePhoto }))
    await state.load()
    state.draft.value.name = 'Новое'
    state.draft.value.photo.removed = true

    const result = await state.save()

    expect(deletePhoto).toHaveBeenCalledWith('A', 2)
    expect(result).toMatchObject({ ok: true })
    expect(state.draft.value.photo.existing).toBeNull()
    expect(state.dirty.value).toBe(false)
    expect(state.photoPartial.value).toBe(false)
  })

  it('сбой удаления фото оставляет частичный результат и повторяет удаление', async () => {
    const deletePhoto = vi
      .fn()
      .mockResolvedValueOnce({ response: { status: 500 }, data: null })
      .mockResolvedValueOnce({ response: { status: 204 }, data: null })
    const state = make(photoOptions({ deletePhoto }))
    await state.load()
    state.draft.value.name = 'Новое'
    state.draft.value.photo.removed = true

    const result = await state.save()

    expect(result).toMatchObject({ ok: false, textSaved: true })
    expect(state.photoPartial.value).toBe(true)
    expect(state.draft.value.photo.removed).toBe(true)

    await state.retryPhoto()
    expect(deletePhoto).toHaveBeenCalledTimes(2)
    expect(state.photoPartial.value).toBe(false)
  })

  it('конфликт ревизии на фото показывает серверную версию', async () => {
    const uploadPhoto = vi.fn().mockResolvedValue({
      response: { status: 409 },
      data: { error: 'Рецепт изменён.', revision: 6 }
    })
    const state = make(photoOptions({ uploadPhoto }))
    await state.load()
    state.draft.value.name = 'Новое'
    state.draft.value.photo.selected = file

    const result = await state.save()

    expect(result).toMatchObject({ ok: false, textSaved: true, conflict: true })
    expect(state.conflictRevision.value).toBe(6)
    expect(state.conflictMessage.value).toBe('Рецепт изменён.')
    expect(state.photoPartial.value).toBe(true)
    expect(state.photoSaving.value).toBe(false)
  })

  it('неизвестный результат фото повторяется защищённо: сначала перечитывает ревизию', async () => {
    const uploadPhoto = vi
      .fn()
      .mockRejectedValueOnce(new Error('network'))
      .mockResolvedValueOnce({
        response: { status: 200 },
        data: recipeData('A', { revision: 3, photoUrl: '/photos/new.jpg' })
      })
    const loadRecipe = vi
      .fn()
      .mockResolvedValueOnce(ok(recipeData('A', { revision: 1 })))
      .mockResolvedValueOnce(ok(recipeData('A', { revision: 2, photoUrl: '/photos/A.jpg' })))
    const state = make(photoOptions({ uploadPhoto, loadRecipe }))
    await state.load()
    state.draft.value.name = 'Новое'
    state.draft.value.photo.selected = file

    const result = await state.save()

    expect(result).toMatchObject({ ok: false, textSaved: true })
    expect(state.photoUnknown.value).toBe(true)
    expect(state.photoError.value).toBe(PHOTO_UNKNOWN_MESSAGE)

    const retried = await state.retryPhoto()

    expect(retried.ok).toBe(true)
    expect(loadRecipe).toHaveBeenCalledTimes(2)
    expect(uploadPhoto).toHaveBeenLastCalledWith('A', file, 2)
    expect(state.photoUnknown.value).toBe(false)
    expect(state.photoPartial.value).toBe(false)
  })

  it('известный HTTP-отказ повторяется без перечитывания и слепого дубля', async () => {
    const uploadPhoto = vi
      .fn()
      .mockResolvedValueOnce({ response: { status: 500 }, data: { error: 'Сбой.' } })
      .mockResolvedValueOnce({
        response: { status: 200 },
        data: recipeData('A', { revision: 3, photoUrl: '/photos/new.jpg' })
      })
    const loadRecipe = vi.fn().mockResolvedValue(ok(recipeData('A', { revision: 1 })))
    const state = make(photoOptions({ uploadPhoto, loadRecipe }))
    await state.load()
    state.draft.value.name = 'Новое'
    state.draft.value.photo.selected = file

    await state.save()
    expect(state.photoUnknown.value).toBe(false)

    await state.retryPhoto()

    expect(loadRecipe).toHaveBeenCalledTimes(1)
    expect(uploadPhoto).toHaveBeenCalledTimes(2)
  })

  it('неизвестный результат удаления: проверка видит отсутствие фото и не повторяет запрос', async () => {
    const deletePhoto = vi.fn().mockRejectedValue(new Error('network'))
    const loadRecipe = vi
      .fn()
      .mockResolvedValueOnce(ok(recipeData('A', { revision: 1 })))
      .mockResolvedValueOnce(ok(recipeData('A', { revision: 2, photoUrl: null })))
    const state = make(photoOptions({ deletePhoto, loadRecipe }))
    await state.load()
    state.draft.value.name = 'Новое'
    state.draft.value.photo.removed = true

    const result = await state.save()

    expect(result).toMatchObject({ ok: false, textSaved: true })
    expect(state.photoUnknown.value).toBe(true)

    const retried = await state.retryPhoto()

    expect(retried.ok).toBe(true)
    expect(deletePhoto).toHaveBeenCalledTimes(1)
    expect(state.draft.value.photo.existing).toBeNull()
    expect(state.photoPartial.value).toBe(false)
    expect(state.revision.value).toBe(2)
  })

  it('выбор другого фото во время сохранения не подменяет отправленное действие', async () => {
    const pendingUpdate = deferred()
    const updateRecipe = vi.fn().mockImplementation(() => pendingUpdate.promise)
    const uploadPhoto = vi.fn().mockResolvedValue({
      response: { status: 200 },
      data: recipeData('A', { revision: 3, photoUrl: '/photos/first.jpg' })
    })
    const state = make(photoOptions({ updateRecipe, uploadPhoto }))
    await state.load()
    state.draft.value.name = 'Новое'
    state.draft.value.photo.selected = file

    const savePromise = state.save()
    const late = { name: 'b.jpg', size: 2, lastModified: 2, type: 'image/jpeg' }
    state.draft.value.photo.selected = late
    pendingUpdate.resolve(ok(recipeData('A', { revision: 2 })))
    const result = await savePromise

    expect(uploadPhoto).toHaveBeenCalledWith('A', file, 2)
    expect(result).toMatchObject({ ok: true })
    // позднее выбранный файл сохраняется как новый dirty-черновик
    expect(state.draft.value.photo.selected).toEqual(late)
    expect(state.dirty.value).toBe(true)
  })

  it('повторное «Сохранить» после частичного успеха не перезаписывает поля', async () => {
    const uploadPhoto = vi
      .fn()
      .mockResolvedValueOnce({ response: { status: 500 }, data: { error: 'Сбой.' } })
      .mockResolvedValueOnce({
        response: { status: 200 },
        data: recipeData('A', { revision: 3, photoUrl: '/photos/new.jpg' })
      })
    const updateRecipe = vi.fn().mockResolvedValue(ok(recipeData('A', { revision: 2 })))
    const state = make(photoOptions({ uploadPhoto, updateRecipe }))
    await state.load()
    state.draft.value.name = 'Новое'
    state.draft.value.photo.selected = file

    const first = await state.save()
    expect(first).toMatchObject({ ok: false, textSaved: true })
    expect(updateRecipe).toHaveBeenCalledTimes(1)

    const second = await state.save()

    expect(second).toMatchObject({ ok: true })
    expect(updateRecipe).toHaveBeenCalledTimes(1)
    expect(uploadPhoto).toHaveBeenCalledTimes(2)
    expect(state.dirty.value).toBe(false)
  })

  it('снятие позднего выбора после успешного фото возвращает черновик к чистому', async () => {
    const pendingUpdate = deferred()
    const updateRecipe = vi.fn().mockImplementation(() => pendingUpdate.promise)
    const uploadPhoto = vi.fn().mockResolvedValue({
      response: { status: 200 },
      data: recipeData('A', { revision: 3, name: 'Новое', photoUrl: '/photos/first.jpg' })
    })
    const state = make(photoOptions({ updateRecipe, uploadPhoto }))
    await state.load()
    state.draft.value.name = 'Новое'
    state.draft.value.photo.selected = file

    const savePromise = state.save()
    const late = { name: 'b.jpg', size: 2, lastModified: 2, type: 'image/jpeg' }
    state.draft.value.photo.selected = late
    pendingUpdate.resolve(ok(recipeData('A', { revision: 2, name: 'Новое' })))
    await savePromise

    expect(state.dirty.value).toBe(true)
    expect(state.draft.value.photo.existing).toBe('/photos/first.jpg')

    state.draft.value.photo.selected = null

    expect(state.dirty.value).toBe(false)
  })

  it('поздний ответ фото после смены ресурса не подменяет новый черновик', async () => {
    const pendingPhotoReq = deferred()
    const uploadPhoto = vi.fn().mockImplementation(() => pendingPhotoReq.promise)
    const updateRecipe = vi.fn().mockResolvedValue(ok(recipeData('A', { revision: 2 })))
    const state = make(photoOptions({ uploadPhoto, updateRecipe }))
    await state.load()
    state.draft.value.name = 'Новое'
    state.draft.value.photo.selected = file

    const savePromise = state.save()
    await new Promise((resolve) => setTimeout(resolve, 0))
    expect(uploadPhoto).toHaveBeenCalled()

    state.setIdentity('B')
    state.draft.value.name = 'черновик B'
    pendingPhotoReq.resolve({
      response: { status: 200 },
      data: recipeData('A', { revision: 3, photoUrl: '/photos/new.jpg' })
    })
    const result = await savePromise

    expect(result.ok).toBe(false)
    expect(state.editingId.value).toBe('B')
    expect(state.draft.value.name).toBe('черновик B')
    expect(state.draft.value.photo.existing).toBeNull()
    expect(state.photoSaving.value).toBe(false)
  })

  it('предупреждение об уходе учитывает несохранённое фото после частичного успеха', async () => {
    const confirm = vi.fn(() => false)
    const uploadPhoto = vi.fn().mockResolvedValue({ response: { status: 500 }, data: { error: 'Сбой.' } })
    const state = make(photoOptions({ uploadPhoto, confirm }))
    await state.load()
    state.draft.value.name = 'Новое'
    state.draft.value.photo.selected = file

    await state.save()

    expect(state.dirty.value).toBe(true)
    expect(state.confirmNavigation()).toBe(false)
    expect(confirm).toHaveBeenCalled()
  })
})

describe('useRecipeDraft — предупреждение об уходе', () => {
  it('отказ от ухода сохраняет пользователя и черновик на месте', async () => {
    const state = make({ confirm: () => false })
    state.draft.value.name = 'важное'

    expect(state.confirmNavigation()).toBe(false)
    expect(state.draft.value.name).toBe('важное')
  })

  it('без несохранённых изменений подтверждение не запрашивается', () => {
    const confirm = vi.fn(() => true)
    const state = make({ confirm })

    expect(state.confirmNavigation()).toBe(true)
    expect(confirm).not.toHaveBeenCalled()
  })

  it('с несохранёнными изменениями и подтверждением уход разрешён', () => {
    const state = make({ confirm: () => true })
    state.draft.value.name = 'важное'

    expect(state.confirmNavigation()).toBe(true)
  })
})

describe('useRecipeDraft — identity ингредиентов', () => {
  async function loaded() {
    const state = make({
      initialId: 'A',
      loadRecipe: vi.fn().mockResolvedValue(ok(recipeData('A')))
    })
    await state.load()
    return state
  }

  it('каждая строка получает уникальную непустую identity, переживающую перестановку', async () => {
    const state = await loaded()
    const uids = state.draft.value.ingredients.map((i) => i.uid)
    expect(uids.every(Boolean)).toBe(true)
    expect(new Set(uids).size).toBe(uids.length)

    state.draft.value.ingredients.push(newIngredientDraft())
    const addedUid = state.draft.value.ingredients.at(-1).uid
    expect(addedUid).toBeTruthy()
    expect(uids).not.toContain(addedUid)
  })

  it('identity не участвует в dirty и не попадает в запрос сохранения', async () => {
    const state = await loaded()

    state.draft.value.ingredients[0].uid = 'переписанный-uid'
    expect(state.dirty.value).toBe(false)

    const payload = draftToPayload(state.draft.value)
    expect(payload.ingredients[0]).not.toHaveProperty('uid')
  })
})

describe('serializeDraft', () => {
  it('не зависит от порядка сезонности, но зависит от порядка ингредиентов', () => {
    const base = {
      name: 'a',
      seasonality: ['winter', 'summer'],
      steps: ['один'],
      ingredients: [{ name: 'лук', amount: 1, unit: 'g', note: '' }],
      photo: { existing: null, removed: false, selected: null }
    }
    const reorderedSeason = { ...base, seasonality: ['summer', 'winter'] }
    expect(serializeDraft(reorderedSeason)).toBe(serializeDraft(base))

    const reorderedIngredients = {
      ...base,
      ingredients: [
        { name: 'соль', amount: 1, unit: 'g', note: '' },
        { name: 'лук', amount: 1, unit: 'g', note: '' }
      ]
    }
    expect(serializeDraft(reorderedIngredients)).not.toBe(serializeDraft(base))
  })
})
