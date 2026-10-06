import { reactive } from 'vue'
import { autocompleteIngredients } from '../api/ingredients'

export const AUTOCOMPLETE_DELAY = 150

/** Пустое состояние подсказок для одной строки ингредиента. */
export function createSuggestionState() {
  return {
    suggestions: [],
    showSuggestions: false,
    activeIndex: -1,
    loading: false,
    noSuggestions: false
  }
}

function resetSuggestionState(state) {
  state.suggestions = []
  state.showSuggestions = false
  state.activeIndex = -1
  state.loading = false
  state.noSuggestions = false
}

/**
 * Автодополнение ингредиентов, привязанное к стабильной identity строки.
 *
 * Состояние каждой строки живёт в одной записи по её `id` (uid), а не по
 * индексу: `{ state, timer, token, query }`. Каждый запрос получает собственный
 * токен и query, поэтому ответ устаревшего текста, удалённой или переставленной
 * строки не может открыть список или подменить более новый результат.
 * `reset()` очищает таймеры и обесценивает все записи при смене рецепта или
 * уходе со страницы.
 */
export function useIngredientAutocomplete(options = {}) {
  const request = options.request || autocompleteIngredients
  const delay = options.delay ?? AUTOCOMPLETE_DELAY

  const records = new Map()
  let tokenSequence = 0

  function recordFor(id) {
    let record = records.get(id)
    if (!record) {
      record = { state: reactive(createSuggestionState()), timer: undefined, token: 0, query: '' }
      records.set(id, record)
    }
    return record
  }

  function stateFor(id) {
    return recordFor(id).state
  }

  function isCurrent(id, token, query) {
    const record = records.get(id)
    return Boolean(record) && record.token === token && record.query === query
  }

  function cancelTimer(id) {
    const record = records.get(id)
    if (record && record.timer !== undefined) {
      clearTimeout(record.timer)
      record.timer = undefined
    }
  }

  async function run(id, query, token) {
    const record = records.get(id)
    if (record) record.timer = undefined
    if (!isCurrent(id, token, query)) return

    const state = records.get(id).state
    state.loading = true
    state.noSuggestions = false

    let result
    try {
      result = await request(query)
    } catch {
      if (!isCurrent(id, token, query)) return
      resetSuggestionState(records.get(id).state)
      return
    }

    if (!isCurrent(id, token, query)) return
    const current = records.get(id).state
    current.loading = false
    const response = result?.response
    const data = result?.data
    if (response?.status === 200 && data) {
      current.suggestions = data.items || []
      current.showSuggestions = true
      current.activeIndex = -1
      current.noSuggestions = current.suggestions.length === 0
    } else {
      resetSuggestionState(current)
    }
  }

  /** Реакция на ввод: дебаунс, свежий токен и отказ от подсказок прошлого текста. */
  function input(id, rawQuery) {
    cancelTimer(id)
    const record = recordFor(id)
    const query = (rawQuery || '').trim()
    record.token = ++tokenSequence
    record.query = query
    resetSuggestionState(record.state)
    if (!query) return
    record.timer = setTimeout(() => run(id, query, record.token), delay)
  }

  /** Закрыть список (blur, Escape, выбор) и обесценить отправленный ответ. */
  function close(id) {
    cancelTimer(id)
    const record = recordFor(id)
    record.token = ++tokenSequence
    record.query = ''
    resetSuggestionState(record.state)
  }

  /** Строка удалена: убрать таймер и состояние. */
  function release(id) {
    cancelTimer(id)
    records.delete(id)
  }

  /** Смена рецепта/уход со страницы: очистить таймеры и обесценить все ответы. */
  function reset() {
    records.forEach((record) => {
      if (record.timer !== undefined) clearTimeout(record.timer)
    })
    records.clear()
  }

  function moveActive(id, delta) {
    const state = records.get(id)?.state
    if (!state || state.suggestions.length === 0) return
    const last = state.suggestions.length - 1
    state.activeIndex = Math.max(0, Math.min(last, state.activeIndex + delta))
  }

  function activeSuggestion(id) {
    const state = records.get(id)?.state
    if (!state || state.activeIndex < 0) return null
    return state.suggestions[state.activeIndex] ?? null
  }

  return {
    stateFor,
    input,
    close,
    release,
    reset,
    moveActive,
    activeSuggestion
  }
}
