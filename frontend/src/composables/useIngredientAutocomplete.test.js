import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { useIngredientAutocomplete } from './useIngredientAutocomplete'

function deferred() {
  let resolve
  let reject
  const promise = new Promise((res, rej) => {
    resolve = res
    reject = rej
  })
  return { promise, resolve, reject }
}

function ok(items) {
  return { response: { status: 200 }, data: { items } }
}

function make(options = {}) {
  return useIngredientAutocomplete({ delay: 150, ...options })
}

async function flush() {
  await vi.advanceTimersByTimeAsync(0)
}

describe('useIngredientAutocomplete — дебаунс и актуальность', () => {
  beforeEach(() => vi.useFakeTimers())
  afterEach(() => vi.useRealTimers())

  it('быстрый набор шлёт один запрос с последним query', async () => {
    const request = vi.fn().mockResolvedValue(ok(['лук']))
    const autocomplete = make({ request })

    autocomplete.input('r1', 'л')
    autocomplete.input('r1', 'лу')
    autocomplete.input('r1', 'лук')
    expect(request).not.toHaveBeenCalled()

    await vi.advanceTimersByTimeAsync(150)

    expect(request).toHaveBeenCalledTimes(1)
    expect(request).toHaveBeenCalledWith('лук')
    const state = autocomplete.stateFor('r1')
    expect(state.showSuggestions).toBe(true)
    expect(state.suggestions).toEqual(['лук'])
    expect(state.loading).toBe(false)
  })

  it('пустой query очищает подсказки и запрос не отправляется', async () => {
    const request = vi.fn()
    const autocomplete = make({ request })

    autocomplete.input('r1', 'лук')
    await vi.advanceTimersByTimeAsync(150)
    autocomplete.input('r1', '   ')
    await vi.advanceTimersByTimeAsync(150)

    const state = autocomplete.stateFor('r1')
    expect(request).toHaveBeenCalledTimes(1)
    expect(state.showSuggestions).toBe(false)
    expect(state.suggestions).toEqual([])
  })

  it('ответ старого текста не заменяет более новый (обратный порядок)', async () => {
    const stale = deferred()
    const fresh = deferred()
    const request = vi
      .fn()
      .mockImplementationOnce(() => stale.promise)
      .mockImplementationOnce(() => fresh.promise)
    const autocomplete = make({ request })

    autocomplete.input('r1', 'л')
    await vi.advanceTimersByTimeAsync(150)
    autocomplete.input('r1', 'лук')
    await vi.advanceTimersByTimeAsync(150)

    fresh.resolve(ok(['лук']))
    await flush()
    stale.resolve(ok(['ложка']))
    await flush()

    const state = autocomplete.stateFor('r1')
    expect(state.suggestions).toEqual(['лук'])
    expect(state.showSuggestions).toBe(true)
  })

  it('ответ удалённой строки не открывает список', async () => {
    vi.useFakeTimers()
    const pending = deferred()
    const request = vi.fn().mockImplementation(() => pending.promise)
    const autocomplete = make({ request })

    autocomplete.input('r1', 'лук')
    await vi.advanceTimersByTimeAsync(150)
    const removed = autocomplete.stateFor('r1')

    autocomplete.release('r1')
    pending.resolve(ok(['лук']))
    await flush()

    expect(removed.showSuggestions).toBe(false)
    expect(removed.suggestions).toEqual([])
  })

  it('удаление соседней строки не сбрасывает подсказки текущей', async () => {
    const request = vi.fn().mockResolvedValue(ok(['лук']))
    const autocomplete = make({ request })

    autocomplete.input('r1', 'лук')
    autocomplete.input('r2', 'соль')
    autocomplete.release('r2')
    await vi.advanceTimersByTimeAsync(150)

    expect(request).toHaveBeenCalledTimes(1)
    expect(request).toHaveBeenCalledWith('лук')
    expect(autocomplete.stateFor('r1').suggestions).toEqual(['лук'])
  })

  it('перестановка строк сохраняет подсказки за identity, а не за индексом', async () => {
    const request = vi
      .fn()
      .mockResolvedValueOnce(ok(['лук']))
      .mockResolvedValueOnce(ok(['соль']))
    const autocomplete = make({ request })

    autocomplete.input('r1', 'лук')
    autocomplete.input('r2', 'соль')
    await vi.advanceTimersByTimeAsync(150)

    // Перестановка — операция вью над массивом; identity строк не меняется.
    expect(autocomplete.stateFor('r1').suggestions).toEqual(['лук'])
    expect(autocomplete.stateFor('r2').suggestions).toEqual(['соль'])
  })
})

describe('useIngredientAutocomplete — закрытие и выбор', () => {
  beforeEach(() => vi.useFakeTimers())
  afterEach(() => vi.useRealTimers())

  it('blur во время запроса не даёт позднему ответу открыть список', async () => {
    const pending = deferred()
    const request = vi.fn().mockImplementation(() => pending.promise)
    const autocomplete = make({ request })

    autocomplete.input('r1', 'лук')
    await vi.advanceTimersByTimeAsync(150)
    autocomplete.close('r1')

    pending.resolve(ok(['лук']))
    await flush()

    expect(autocomplete.stateFor('r1').showSuggestions).toBe(false)
    expect(autocomplete.stateFor('r1').loading).toBe(false)
  })

  it('Escape/выбор во время запроса оставляет список закрытым', async () => {
    const pending = deferred()
    const request = vi.fn().mockImplementation(() => pending.promise)
    const autocomplete = make({ request })

    autocomplete.input('r1', 'лук')
    await vi.advanceTimersByTimeAsync(150)
    autocomplete.input('r1', 'лук ') // выбор подсказки обновляет поле и закрывает
    autocomplete.close('r1')

    pending.resolve(ok(['лук']))
    await flush()

    expect(autocomplete.stateFor('r1').showSuggestions).toBe(false)
  })

  it('закрытие отменяет ещё не отправленный debounce', async () => {
    const request = vi.fn()
    const autocomplete = make({ request })

    autocomplete.input('r1', 'лук')
    autocomplete.close('r1')
    await vi.advanceTimersByTimeAsync(150)

    expect(request).not.toHaveBeenCalled()
  })
})

describe('useIngredientAutocomplete — клавиатура и ошибки', () => {
  beforeEach(() => vi.useFakeTimers())
  afterEach(() => vi.useRealTimers())

  it('навигация по подсказкам ограничена границами списка', async () => {
    const request = vi.fn().mockResolvedValue(ok(['а', 'б', 'в']))
    const autocomplete = make({ request })

    autocomplete.input('r1', 'а')
    await vi.advanceTimersByTimeAsync(150)

    expect(autocomplete.activeSuggestion('r1')).toBeNull()
    autocomplete.moveActive('r1', -1)
    expect(autocomplete.activeSuggestion('r1')).toBe('а')
    autocomplete.moveActive('r1', 1)
    expect(autocomplete.activeSuggestion('r1')).toBe('б')
    autocomplete.moveActive('r1', 1)
    autocomplete.moveActive('r1', 1)
    expect(autocomplete.activeSuggestion('r1')).toBe('в')
    autocomplete.moveActive('r1', -1)
    autocomplete.moveActive('r1', -1)
    autocomplete.moveActive('r1', -1)
    expect(autocomplete.activeSuggestion('r1')).toBe('а')
  })

  it('сетевой отказ снимает загрузку и не блокирует ручной ввод', async () => {
    const request = vi.fn().mockRejectedValue(new Error('network'))
    const autocomplete = make({ request })

    autocomplete.input('r1', 'лук')
    await vi.advanceTimersByTimeAsync(150)

    const state = autocomplete.stateFor('r1')
    expect(state.loading).toBe(false)
    expect(state.showSuggestions).toBe(false)
    expect(state.suggestions).toEqual([])

    // Ручной ввод не зависит от состояния подсказок.
    autocomplete.input('r1', 'лук ручной')
    expect(autocomplete.stateFor('r1').loading).toBe(false)
  })

  it('ответ без элементов показывает «нет подсказок»', async () => {
    const request = vi.fn().mockResolvedValue(ok([]))
    const autocomplete = make({ request })

    autocomplete.input('r1', 'щщщ')
    await vi.advanceTimersByTimeAsync(150)

    const state = autocomplete.stateFor('r1')
    expect(state.showSuggestions).toBe(true)
    expect(state.noSuggestions).toBe(true)
  })
})

describe('useIngredientAutocomplete — очистка', () => {
  beforeEach(() => vi.useFakeTimers())
  afterEach(() => vi.useRealTimers())

  it('смена рецепта очищает таймеры и обезвреживает поздние ответы', async () => {
    const pending = deferred()
    const request = vi.fn().mockImplementation(() => pending.promise)
    const autocomplete = make({ request })

    autocomplete.input('r1', 'лук')
    await vi.advanceTimersByTimeAsync(150)
    autocomplete.input('r2', 'соль')

    autocomplete.reset()
    await vi.advanceTimersByTimeAsync(150)

    expect(request).toHaveBeenCalledTimes(1)

    pending.resolve(ok(['лук']))
    await flush()

    expect(autocomplete.stateFor('r1').showSuggestions).toBe(false)
  })
})
