import { describe, it, expect, vi } from 'vitest'
import {
  useWeekDraft,
  slotKey,
  LOAD_ERROR_MESSAGE,
  SAVE_ERROR_MESSAGE,
  CONFLICT_MESSAGE
} from './useWeekDraft'
import { toIso } from '../constants/plan'

const WEEK_A = new Date(2026, 0, 5)
const WEEK_B = new Date(2026, 0, 12)

function deferred() {
  let resolve
  let reject
  const promise = new Promise((res, rej) => {
    resolve = res
    reject = rej
  })
  return { promise, resolve, reject }
}

function ok(entries = [], revision = 0) {
  return { response: { status: 200 }, data: { revision, entries } }
}

function entry(day, mealType, recipeId, portions = 2) {
  return { day, mealType, recipeId, recipeName: `#${recipeId}`, portions, state: 'ok' }
}

function make(options = {}) {
  return useWeekDraft({
    initialWeek: WEEK_A,
    confirm: () => true,
    ...options
  })
}

describe('useWeekDraft — загрузка и порядок ответов', () => {
  it('поздний ответ прошлой недели не подменяет текущую неделю', async () => {
    const slowA = deferred()
    const fastB = deferred()
    const loadWeekPlan = vi
      .fn()
      .mockImplementationOnce(() => slowA.promise)
      .mockImplementationOnce(() => fastB.promise)
    const state = make({ loadWeekPlan })

    const first = state.loadWeek()
    const second = state.goToWeek(1)

    fastB.resolve(ok([entry(0, 'lunch', 22)]))
    await second

    slowA.resolve(ok([entry(0, 'lunch', 11)]))
    await first

    expect(toIso(state.weekStart.value)).toBe(toIso(WEEK_B))
    expect(state.draft.value[slotKey(0, 'lunch')].recipeId).toBe(22)
  })

  it('успешная загрузка не создаёт ложный dirty', async () => {
    const state = make({
      loadWeekPlan: vi.fn().mockResolvedValue(ok([entry(1, 'dinner', 5)]))
    })

    await state.loadWeek()

    expect(state.dirty.value).toBe(false)
  })

  it('пустой успешный ответ тоже не dirty', async () => {
    const state = make({ loadWeekPlan: vi.fn().mockResolvedValue(ok([])) })

    await state.loadWeek()

    expect(state.dirty.value).toBe(false)
  })

  it('404 показывает сообщение, но не оставляет план в подвешенном состоянии', async () => {
    const state = make({
      loadWeekPlan: vi.fn().mockResolvedValue({ response: { status: 404 }, data: null })
    })

    await state.loadWeek()

    expect(state.loading.value).toBe(false)
    expect(state.loadError.value).toMatch(/семь/i)
    expect(state.dirty.value).toBe(false)
  })
})

describe('useWeekDraft — сохранение', () => {
  it('смена недели во время PUT не отмечает другую неделю сохранённой', async () => {
    const pending = deferred()
    const state = make({
      saveWeekPlan: vi.fn().mockImplementation(() => pending.promise)
    })
    state.setSlot(0, 'lunch', { recipeId: 1, recipeName: 'A', portions: 2 })

    const savePromise = state.save()
    // Пользователь уходит на другую неделю, пока PUT ещё в полёте.
    state.weekStart.value = WEEK_B
    state.draft.value = {}

    pending.resolve(ok([entry(0, 'lunch', 1)]))
    await savePromise

    expect(state.savedMessage.value).toBe('')
    expect(state.weekStart.value).toEqual(WEEK_B)
    expect(state.draft.value).toEqual({})
  })

  it('правка во время PUT сохраняется как dirty и не теряется', async () => {
    const pending = deferred()
    const state = make({
      saveWeekPlan: vi.fn().mockImplementation(() => pending.promise)
    })
    state.setSlot(0, 'lunch', { recipeId: 1, recipeName: 'A', portions: 2 })

    const savePromise = state.save()
    state.setSlot(1, 'dinner', { recipeId: 9, recipeName: 'B', portions: 3 })

    pending.resolve(ok([entry(0, 'lunch', 1)]))
    await savePromise

    expect(state.savedMessage.value).toBe('План сохранён.')
    expect(state.dirty.value).toBe(true)
    expect(state.draft.value[slotKey(1, 'dinner')].recipeId).toBe(9)
  })

  it('сохранение без новых правок синхронизирует черновик с ответом', async () => {
    const state = make({
      saveWeekPlan: vi.fn().mockResolvedValue(ok([entry(0, 'lunch', 1)]))
    })
    state.setSlot(0, 'lunch', { recipeId: 1, recipeName: 'A', portions: 2 })

    await state.save()

    expect(state.savedMessage.value).toBe('План сохранён.')
    expect(state.dirty.value).toBe(false)
    expect(state.draft.value[slotKey(0, 'lunch')].state).toBe('ok')
  })
})

describe('useWeekDraft — сетевые отказы', () => {
  it('сетевой отказ загрузки завершает pending, даёт сообщение и хранит черновик', async () => {
    const state = make({
      loadWeekPlan: vi.fn().mockRejectedValue(new Error('network'))
    })
    state.setSlot(0, 'lunch', { recipeId: 7, recipeName: 'A', portions: 1 })

    await state.loadWeek()

    expect(state.loading.value).toBe(false)
    expect(state.loadError.value).toBe(LOAD_ERROR_MESSAGE)
    expect(state.draft.value[slotKey(0, 'lunch')].recipeId).toBe(7)
  })

  it('сетевой отказ сохранения завершает pending, не чистит черновик и не повторяет запрос', async () => {
    const saveWeekPlan = vi.fn().mockRejectedValue(new Error('timeout'))
    const state = make({ saveWeekPlan })
    state.setSlot(0, 'lunch', { recipeId: 7, recipeName: 'A', portions: 1 })

    await state.save()

    expect(state.saving.value).toBe(false)
    expect(state.saveError.value).toBe(SAVE_ERROR_MESSAGE)
    expect(state.draft.value[slotKey(0, 'lunch')].recipeId).toBe(7)
    expect(state.dirty.value).toBe(true)
    expect(saveWeekPlan).toHaveBeenCalledTimes(1)
  })

  it('ошибочный HTTP-ответ сохранения даёт понятное сообщение', async () => {
    const state = make({
      saveWeekPlan: vi.fn().mockResolvedValue({ response: { status: 500 }, data: null })
    })
    state.setSlot(0, 'lunch', { recipeId: 7, recipeName: 'A', portions: 1 })

    await state.save()

    expect(state.saving.value).toBe(false)
    expect(state.saveError.value).toBe(SAVE_ERROR_MESSAGE)
    expect(state.dirty.value).toBe(true)
  })

  it('неразобранный ответ загрузки не очищает черновик', async () => {
    const state = make({
      loadWeekPlan: vi.fn().mockResolvedValue({ response: { status: 200 }, data: null })
    })
    state.setSlot(0, 'lunch', { recipeId: 7, recipeName: 'A', portions: 1 })

    await state.loadWeek()

    expect(state.loading.value).toBe(false)
    expect(state.loadError.value).toBe(LOAD_ERROR_MESSAGE)
    expect(state.draft.value[slotKey(0, 'lunch')].recipeId).toBe(7)
  })

  it('неразобранный ответ сохранения не очищает черновик', async () => {
    const state = make({
      saveWeekPlan: vi.fn().mockResolvedValue({ response: { status: 200 }, data: null })
    })
    state.setSlot(0, 'lunch', { recipeId: 7, recipeName: 'A', portions: 1 })

    await state.save()

    expect(state.saving.value).toBe(false)
    expect(state.saveError.value).toBe(SAVE_ERROR_MESSAGE)
    expect(state.draft.value[slotKey(0, 'lunch')].recipeId).toBe(7)
    expect(state.dirty.value).toBe(true)
  })
})

describe('useWeekDraft — ревизия и конфликты', () => {
  it('загрузка сохраняет серверную ревизию недели', async () => {
    const state = make({
      loadWeekPlan: vi.fn().mockResolvedValue(ok([entry(0, 'lunch', 1)], 4))
    })

    await state.loadWeek()

    expect(state.revision.value).toBe(4)
  })

  it('сохранение отправляет ожидаемую ревизию и принимает новую', async () => {
    const saveWeekPlan = vi.fn().mockResolvedValue(ok([entry(0, 'lunch', 1)], 5))
    const state = make({
      loadWeekPlan: vi.fn().mockResolvedValue(ok([entry(0, 'lunch', 1)], 4)),
      saveWeekPlan
    })
    await state.loadWeek()
    state.setSlot(0, 'lunch', { recipeId: 1, recipeName: 'A', portions: 3 })

    await state.save()

    expect(saveWeekPlan).toHaveBeenCalledWith(toIso(WEEK_A), expect.any(Array), 4)
    expect(state.revision.value).toBe(5)
  })

  it('конфликт 409 сохраняет черновик, ревизию и показывает серверную версию', async () => {
    const state = make({
      loadWeekPlan: vi.fn().mockResolvedValue(ok([entry(0, 'lunch', 1)], 1)),
      saveWeekPlan: vi.fn().mockResolvedValue({
        response: { status: 409 },
        data: { error: 'План изменил другой участник.', revision: 7, entries: [entry(1, 'dinner', 9)] }
      })
    })
    await state.loadWeek()
    state.setSlot(2, 'breakfast', { recipeId: 5, recipeName: 'Моя', portions: 2 })

    await state.save()

    // Черновик не перезаписан и ревизия не сдвинута.
    expect(state.draft.value[slotKey(2, 'breakfast')].recipeId).toBe(5)
    expect(state.dirty.value).toBe(true)
    expect(state.revision.value).toBe(1)
    expect(state.conflict.value.revision).toBe(7)
    expect(state.conflict.value.entries).toHaveLength(1)
    expect(state.saveError.value).toBe('План изменил другой участник.')
  })

  it('конфликт показывает построчные различия между черновиком и сервером', async () => {
    const state = make({
      loadWeekPlan: vi.fn().mockResolvedValue(
        ok([entry(0, 'lunch', 1, 2), entry(1, 'dinner', 2, 2)], 1)
      ),
      saveWeekPlan: vi.fn().mockResolvedValue({
        response: { status: 409 },
        data: {
          revision: 2,
          entries: [entry(0, 'lunch', 9, 3), entry(1, 'dinner', 2, 2), entry(2, 'breakfast', 4, 1)]
        }
      })
    })
    await state.loadWeek()
    // Локально добавляем завтрак; обед сервер изменил, ужин совпадает.
    state.setSlot(2, 'breakfast', { recipeId: 5, recipeName: '#5', portions: 2 })

    await state.save()

    expect(state.conflictDiff.value).toHaveLength(2)
    expect(state.conflictDiff.value[0]).toMatchObject({
      day: 0,
      mealType: 'lunch',
      serverName: '#9',
      localName: '#1'
    })
    expect(state.conflictDiff.value[1]).toMatchObject({
      day: 2,
      mealType: 'breakfast',
      serverName: '#4',
      localName: '#5'
    })
  })

  it('без конфликта построчных различий нет', async () => {
    const state = make({
      loadWeekPlan: vi.fn().mockResolvedValue(ok([entry(0, 'lunch', 1)], 1))
    })
    await state.loadWeek()

    expect(state.conflictDiff.value).toEqual([])
  })

  it('конфликт без тела ответа не снимает защиту и даёт понятное сообщение', async () => {
    const state = make({
      loadWeekPlan: vi.fn().mockResolvedValue(ok([], 2)),
      saveWeekPlan: vi.fn().mockResolvedValue({ response: { status: 409 }, data: null })
    })
    await state.loadWeek()
    state.setSlot(0, 'lunch', { recipeId: 1, recipeName: 'A', portions: 2 })

    await state.save()

    expect(state.revision.value).toBe(2)
    expect(state.dirty.value).toBe(true)
    expect(state.conflict.value).toEqual({ revision: null, entries: [] })
    expect(state.saveError.value).toBe(CONFLICT_MESSAGE)
  })

  it('загрузка актуальной версии заменяет черновик и ревизию', async () => {
    const loadWeekPlan = vi
      .fn()
      .mockResolvedValueOnce(ok([entry(0, 'lunch', 1)], 1))
      .mockResolvedValueOnce(ok([entry(1, 'dinner', 9)], 7))
    const state = make({
      loadWeekPlan,
      confirm: () => true,
      saveWeekPlan: vi.fn().mockResolvedValue({
        response: { status: 409 },
        data: { revision: 7, entries: [entry(1, 'dinner', 9)] }
      })
    })
    await state.loadWeek()
    state.setSlot(2, 'breakfast', { recipeId: 5, recipeName: 'Моя', portions: 2 })
    await state.save()
    expect(state.conflict.value).not.toBeNull()

    const loaded = await state.reloadServerVersion()

    expect(loaded).toBe(true)
    expect(state.revision.value).toBe(7)
    expect(state.dirty.value).toBe(false)
    expect(state.conflict.value).toBeNull()
    expect(state.draft.value[slotKey(1, 'dinner')].recipeId).toBe(9)
    expect(state.draft.value[slotKey(2, 'breakfast')]).toBeUndefined()
  })

  it('сетевой отказ не двигает ревизию — повтор проверяет ту же версию', async () => {
    const saveWeekPlan = vi
      .fn()
      .mockRejectedValueOnce(new Error('network'))
      .mockResolvedValueOnce(ok([entry(0, 'lunch', 1)], 2))
    const state = make({
      loadWeekPlan: vi.fn().mockResolvedValue(ok([entry(0, 'lunch', 1)], 1)),
      saveWeekPlan
    })
    await state.loadWeek()
    state.setSlot(0, 'lunch', { recipeId: 1, recipeName: 'A', portions: 3 })

    await state.save()

    expect(state.revision.value).toBe(1)
    expect(state.dirty.value).toBe(true)
    expect(state.saveError.value).toBe(SAVE_ERROR_MESSAGE)

    await state.save()

    expect(saveWeekPlan).toHaveBeenNthCalledWith(1, toIso(WEEK_A), expect.any(Array), 1)
    expect(saveWeekPlan).toHaveBeenNthCalledWith(2, toIso(WEEK_A), expect.any(Array), 1)
    expect(state.revision.value).toBe(2)
  })
})

describe('useWeekDraft — предупреждение об уходе', () => {
  it('отмена ухода оставляет текущую неделю и черновик', async () => {
    const state = make({ confirm: () => false })
    state.setSlot(0, 'lunch', { recipeId: 1, recipeName: 'A', portions: 2 })

    const changed = await state.goToWeek(1)

    expect(changed).toBe(false)
    expect(state.weekStart.value).toEqual(WEEK_A)
    expect(state.draft.value[slotKey(0, 'lunch')].recipeId).toBe(1)
  })

  it('без несохранённых изменений уход не требует подтверждения', async () => {
    const confirm = vi.fn(() => true)
    const state = make({
      confirm,
      loadWeekPlan: vi.fn().mockResolvedValue(ok([]))
    })

    await state.goToWeek(1)

    expect(confirm).not.toHaveBeenCalled()
    expect(state.weekStart.value).toEqual(WEEK_B)
  })

  it('есть изменения и подтверждение ухода — переключает неделю', async () => {
    const state = make({
      confirm: () => true,
      loadWeekPlan: vi.fn().mockResolvedValue(ok([]))
    })
    state.setSlot(0, 'lunch', { recipeId: 1, recipeName: 'A', portions: 2 })

    await state.goToWeek(1)

    expect(state.weekStart.value).toEqual(WEEK_B)
  })
})
