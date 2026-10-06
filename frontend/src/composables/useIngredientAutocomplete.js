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
 * Состояние каждой строки живёт в отдельной записи по её `id` (uid), а не по
 * индексу. Каждый запрос получает собственный токен: если строка изменилась,
 * была удалена или список закрыли, поздний ответ не может открыть список или
 * подменить более новый результат. `reset()` обесценивает все токены и
 * очищает таймеры при смене рецепта или уходе со страницы.
 */
export function useIngredientAutocomplete(options = {}) {
  const request = options.request || autocompleteIngredients
  const delay = options.delay ?? AUTOCOMPLETE_DELAY

  const states = new Map()
  const timers = new Map()
  const tokens = new Map()
  let tokenSequence = 0

  function stateFor(id) {
    let state = states.get(id)
    if (!state) {
      state = reactive(createSuggestionState())
      states.set(id, state)
    }
    return state
  }

  function nextToken(id) {
    tokenSequence += 1
    tokens.set(id, tokenSequence)
    return tokenSequence
  }

  function isCurrent(id, token) {
    return tokens.get(id) === token
  }

  function cancelTimer(id) {
    const timer = timers.get(id)
    if (timer !== undefined) {
      clearTimeout(timer)
      timers.delete(id)
    }
  }

  async function run(id, query, token) {
    timers.delete(id)
    if (!isCurrent(id, token)) return
    const state = stateFor(id)
    state.loading = true
    state.noSuggestions = false

    let result
    try {
      result = await request(query)
    } catch {
      if (!isCurrent(id, token)) return
      state.loading = false
      state.suggestions = []
      state.showSuggestions = false
      state.activeIndex = -1
      state.noSuggestions = false
      return
    }

    if (!isCurrent(id, token)) return
    state.loading = false
    const response = result?.response
    const data = result?.data
    if (response?.status === 200 && data) {
      state.suggestions = data.items || []
      state.showSuggestions = true
      state.activeIndex = -1
      state.noSuggestions = state.suggestions.length === 0
    } else {
      state.suggestions = []
      state.showSuggestions = false
      state.activeIndex = -1
      state.noSuggestions = false
    }
  }

  /** Реакция на ввод в строке: дебаунс и запрос за актуальным query. */
  function input(id, rawQuery) {
    cancelTimer(id)
    const query = (rawQuery || '').trim()
    const state = stateFor(id)
    const token = nextToken(id)
    if (!query) {
      resetSuggestionState(state)
      return
    }
    state.noSuggestions = false
    timers.set(
      id,
      setTimeout(() => run(id, query, token), delay)
    )
  }

  /** Закрыть список (blur, Escape, выбор) и обесценить отправленный ответ. */
  function close(id) {
    cancelTimer(id)
    nextToken(id)
    const state = stateFor(id)
    state.showSuggestions = false
    state.activeIndex = -1
    state.loading = false
  }

  /** Строка удалена: убрать таймер, ответы и состояние. */
  function release(id) {
    cancelTimer(id)
    nextToken(id)
    states.delete(id)
    timers.delete(id)
    tokens.delete(id)
  }

  /** Смена рецепта/уход со страницы: очистить таймеры и обесценить все ответы. */
  function reset() {
    timers.forEach((timer) => clearTimeout(timer))
    timers.clear()
    states.clear()
    tokens.clear()
  }

  function moveActive(id, delta) {
    const state = states.get(id)
    if (!state || state.suggestions.length === 0) return
    const last = state.suggestions.length - 1
    state.activeIndex = Math.max(0, Math.min(last, state.activeIndex + delta))
  }

  function activeSuggestion(id) {
    const state = states.get(id)
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
