import { describe, it, expect, vi } from 'vitest'
import {
  useShoppingList,
  SHOPPING_LOAD_ERROR_MESSAGE,
  SHOPPING_FAMILY_ERROR_MESSAGE
} from './useShoppingList'
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

function ok({ items = [], excluded = [], hasPlan = true } = {}) {
  return { response: { status: 200 }, data: { weekStart: '2026-01-05', hasPlan, items, excluded } }
}

function make(options = {}) {
  return useShoppingList({
    initialWeek: WEEK_A,
    fetchList: vi.fn().mockResolvedValue(ok()),
    ...options
  })
}

describe('useShoppingList — порядок ответов', () => {
  it('поздний ответ прошлой недели не подменяет текущую', async () => {
    const slowA = deferred()
    const fastB = deferred()
    const fetchList = vi
      .fn()
      .mockImplementationOnce(() => slowA.promise)
      .mockImplementationOnce(() => fastB.promise)
    const state = make({ fetchList })

    const first = state.load()
    const second = state.goToWeek(1)

    fastB.resolve(ok({ items: [{ name: 'из B', amount: 1, unit: 'pcs', display: '1 шт' }] }))
    await second

    slowA.resolve(ok({ items: [{ name: 'из A', amount: 1, unit: 'pcs', display: '1 шт' }] }))
    await first

    expect(toIso(state.weekStart.value)).toBe(toIso(WEEK_B))
    expect(state.items.value.map((i) => i.name)).toEqual(['из B'])
    expect(state.resultWeek.value).toBe(toIso(WEEK_B))
    expect(state.resultIsCurrent.value).toBe(true)
  })

  it('успешная загрузка переносит диагностику и признак плана', async () => {
    const state = make({
      fetchList: vi.fn().mockResolvedValue(
        ok({
          items: [{ name: 'мука', amount: 300, unit: 'g', display: '300 г' }],
          excluded: [
            {
              day: 2,
              mealType: 'dinner',
              recipeId: 'r1',
              recipeName: 'Суп',
              reason: 'source_missing'
            }
          ],
          hasPlan: true
        })
      )
    })

    await state.load()

    expect(state.hasPlan.value).toBe(true)
    expect(state.items.value).toHaveLength(1)
    expect(state.excluded.value).toHaveLength(1)
    expect(state.loading.value).toBe(false)
    expect(state.resultIsCurrent.value).toBe(true)
  })

  it('пустой ответ без плана помечает план отсутствующим, а не теряет источник', async () => {
    const state = make({
      fetchList: vi.fn().mockResolvedValue(ok({ hasPlan: false }))
    })

    await state.load()

    expect(state.hasPlan.value).toBe(false)
    expect(state.items.value).toEqual([])
    expect(state.excluded.value).toEqual([])
  })
})

describe('useShoppingList — сетевые ошибки', () => {
  it('ошибка смены недели снимает pending и оставляет предыдущий результат с его неделей', async () => {
    const fetchList = vi
      .fn()
      .mockResolvedValueOnce(ok({ items: [{ name: 'мука', amount: 300, unit: 'g', display: '300 г' }] }))
      .mockRejectedValueOnce(new Error('offline'))
    const state = make({ fetchList })

    await state.load()
    expect(state.resultWeek.value).toBe(toIso(WEEK_A))

    await state.goToWeek(1)

    expect(state.loading.value).toBe(false)
    expect(state.error.value).toBe(SHOPPING_LOAD_ERROR_MESSAGE)
    expect(state.items.value.map((i) => i.name)).toEqual(['мука'])
    expect(state.resultWeek.value).toBe(toIso(WEEK_A))
    expect(state.resultIsCurrent.value).toBe(false)
    expect(state.resultWeekDate.value.getTime()).toBe(WEEK_A.getTime())
  })

  it('404 снимает pending и сообщает, что пользователь вне семьи', async () => {
    const state = make({
      fetchList: vi.fn().mockResolvedValue({ response: { status: 404 }, data: null })
    })

    await state.load()

    expect(state.loading.value).toBe(false)
    expect(state.error.value).toBe(SHOPPING_FAMILY_ERROR_MESSAGE)
    expect(state.resultWeek.value).toBe(null)
    expect(state.items.value).toEqual([])
  })

  it('обновление не подменяет результат при ошибке', async () => {
    const fetchList = vi
      .fn()
      .mockResolvedValueOnce(ok({ items: [{ name: 'соль', amount: 1, unit: 'tsp', display: '1 ч. ложка' }] }))
      .mockRejectedValueOnce(new Error('offline'))
    const state = make({ fetchList })

    await state.load()
    await state.refresh()

    expect(state.refreshing.value).toBe(false)
    expect(state.items.value.map((i) => i.name)).toEqual(['соль'])
    expect(state.error.value).toBe(SHOPPING_LOAD_ERROR_MESSAGE)
  })
})
