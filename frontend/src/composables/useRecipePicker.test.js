import { describe, it, expect, vi } from 'vitest'
import {
  useRecipePicker,
  emptyFilters,
  hasAnyFilter,
  toPickerItem,
  MATCH_ERROR_MESSAGE,
  BROKEN_SELECTION_MESSAGE
} from './useRecipePicker'

function deferred() {
  let resolve
  let reject
  const promise = new Promise((res, rej) => {
    resolve = res
    reject = rej
  })
  return { promise, resolve, reject }
}

function item(recipeId, name, extra = {}) {
  return {
    recipeId,
    name,
    difficulty: 1,
    calories: 100,
    cookTimeMinutes: 10,
    servings: 2,
    tags: [],
    seasonality: [],
    diet: [],
    matchScore: 0,
    ...extra
  }
}

function ok(items) {
  return { response: { status: 200 }, data: { items } }
}

function make(options = {}) {
  return useRecipePicker({ schedule: (run) => run(), ...options })
}

describe('useRecipePicker — выбор не теряется', () => {
  it('выбранный рецепт исчез из новой выдачи — имя сохраняется', async () => {
    const matchRecipes = vi
      .fn()
      .mockResolvedValueOnce(ok([item('r1', 'Борщ')]))
      .mockResolvedValueOnce(ok([item('r2', 'Блины')]))
    const picker = make({ matchRecipes })

    await picker.open(null)
    picker.select(picker.recipes.value[0])
    await picker.apply()

    expect(picker.recipes.value.map((r) => r.id)).toEqual(['r2'])

    const entry = picker.confirm()
    expect(entry).toMatchObject({ recipeId: 'r1', recipeName: 'Борщ' })
  })

  it('смена только порций сохраняет актуальное имя и состояние', async () => {
    const matchRecipes = vi
      .fn()
      .mockResolvedValue(ok([item('r1', 'Борщ', { state: 'warning', isExternal: true })]))
    const picker = make({ matchRecipes })

    await picker.open(null)
    picker.select(picker.recipes.value[0])
    picker.portions.value = 4

    expect(picker.confirm()).toMatchObject({
      recipeId: 'r1',
      recipeName: 'Борщ',
      portions: 4,
      state: 'warning',
      isExternal: true
    })
  })

  it('блокировка без источника объясняет недоступный выбор до подтверждения', async () => {
    const picker = make({ matchRecipes: vi.fn().mockResolvedValue(ok([])) })

    await picker.open({ recipeId: 'r9', recipeName: 'Старое блюдо', portions: 3, state: 'broken' })

    expect(picker.portions.value).toBe(3)
    expect(picker.selectionWarning.value).toBe(BROKEN_SELECTION_MESSAGE)
    expect(picker.confirm()).toMatchObject({ recipeId: 'r9', recipeName: 'Старое блюдо', state: 'broken' })
  })

  it('у обычного выбора предупреждения нет', async () => {
    const picker = make({ matchRecipes: vi.fn().mockResolvedValue(ok([item('r1', 'Борщ')])) })

    await picker.open(null)
    picker.select(picker.recipes.value[0])

    expect(picker.selectionWarning.value).toBe('')
  })
})

describe('useRecipePicker — фильтры, сброс и поиск', () => {
  it('черновые фильтры не влияют на выдачу до «Применить»', async () => {
    const picker = make({ matchRecipes: vi.fn().mockResolvedValue(ok([])) })
    await picker.open(null)

    picker.draftFilters.value.maxDifficulty = 3
    expect(picker.hasFilters.value).toBe(false)

    await picker.apply()
    expect(picker.hasFilters.value).toBe(true)
  })

  it('«Сбросить» действительно запрашивает выдачу без фильтров', async () => {
    const matchRecipes = vi.fn().mockResolvedValue(ok([]))
    const picker = make({ matchRecipes })
    await picker.open(null)

    picker.draftFilters.value.maxDifficulty = 3
    picker.draftFilters.value.ingredient = 'лук'
    await picker.apply()
    picker.search.value = 'борщ'
    await picker.reset()

    const body = matchRecipes.mock.calls.at(-1)[0]
    expect(body.search).toBeNull()
    expect(body.filters.maxDifficulty).toBeNull()
    expect(body.filters.includeIngredients).toBeNull()
    expect(picker.hasFilters.value).toBe(false)
  })

  it('поиск по имени уходит на сервер, а не фильтруется локально', async () => {
    // Сервер — источник истины: даже несовпадающее имя показывается как есть.
    const matchRecipes = vi.fn().mockResolvedValue(ok([item('r1', 'Совсем другое')]))
    const picker = make({ matchRecipes })
    await picker.open(null)

    await picker.setSearch('  борщ ')

    expect(matchRecipes.mock.calls.at(-1)[0].search).toBe('борщ')
    expect(picker.recipes.value.map((r) => r.id)).toEqual(['r1'])
  })

  it('hasAnyFilter распознаёт пустой и заполненный набор', () => {
    expect(hasAnyFilter(emptyFilters())).toBe(false)
    expect(hasAnyFilter({ ...emptyFilters(), maxCalories: '500' })).toBe(true)
    expect(hasAnyFilter({ ...emptyFilters(), seasons: ['winter'] })).toBe(true)
  })
})

describe('useRecipePicker — порядок ответов и ошибки', () => {
  it('поздний ответ не заменяет более новый', async () => {
    const slow = deferred()
    const fast = deferred()
    const matchRecipes = vi
      .fn()
      .mockImplementationOnce(() => slow.promise)
      .mockImplementationOnce(() => fast.promise)
    const picker = make({ matchRecipes })

    const first = picker.reload()
    const second = picker.reload()

    fast.resolve(ok([item('r2', 'Новое')]))
    await second
    slow.resolve(ok([item('r1', 'Старое')]))
    await first

    expect(picker.recipes.value.map((r) => r.id)).toEqual(['r2'])
    expect(picker.loading.value).toBe(false)
  })

  it('ошибка подбора завершает pending и не чистит выдачу и выбор', async () => {
    const matchRecipes = vi
      .fn()
      .mockResolvedValueOnce(ok([item('r1', 'Борщ')]))
      .mockRejectedValueOnce(new Error('network'))
    const picker = make({ matchRecipes })

    await picker.open(null)
    picker.select(picker.recipes.value[0])
    await picker.reload()

    expect(picker.loading.value).toBe(false)
    expect(picker.error.value).toBe(MATCH_ERROR_MESSAGE)
    expect(picker.recipes.value).toHaveLength(1)
    expect(picker.selection.value.recipeId).toBe('r1')
  })

  it('HTTP-ошибка показывает серверное объяснение и завершает pending', async () => {
    const matchRecipes = vi
      .fn()
      .mockResolvedValue({ response: { status: 500 }, data: { error: 'Сервер недоступен.' } })
    const picker = make({ matchRecipes })

    await picker.open(null)

    expect(picker.error.value).toBe('Сервер недоступен.')
    expect(picker.loading.value).toBe(false)
  })

  it('пустой неразобранный ответ даёт понятную ошибку, не удаляя предыдущую выдачу', async () => {
    const matchRecipes = vi
      .fn()
      .mockResolvedValueOnce(ok([item('r1', 'Борщ')]))
      .mockResolvedValueOnce({ response: { status: 200 }, data: null })
    const picker = make({ matchRecipes })

    await picker.open(null)
    await picker.reload()

    expect(picker.error.value).toBe(MATCH_ERROR_MESSAGE)
    expect(picker.recipes.value).toHaveLength(1)
  })
})

describe('toPickerItem — метаданные внешнего рецепта', () => {
  it('переносит состояние и происхождение, не маскируя внешний под обычный', () => {
    const mapped = toPickerItem(
      item('r1', 'Борщ', { isExternal: true, state: 'warning', sourceFamilyName: 'Семья X' })
    )

    expect(mapped).toMatchObject({
      id: 'r1',
      name: 'Борщ',
      isExternal: true,
      state: 'warning',
      sourceFamilyName: 'Семья X'
    })
  })

  it('у собственного рецепта состояние пустое', () => {
    expect(toPickerItem(item('r1', 'Борщ'))).toMatchObject({
      isExternal: false,
      state: null,
      sourceFamilyName: null
    })
  })
})
