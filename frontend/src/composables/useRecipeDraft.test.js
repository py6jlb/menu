import { describe, it, expect, vi } from 'vitest'
import {
  useRecipeDraft,
  serializeDraft,
  LOAD_ERROR_MESSAGE,
  SAVE_ERROR_MESSAGE,
  EMPTY_NAME_MESSAGE
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
