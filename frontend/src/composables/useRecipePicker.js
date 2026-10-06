import { ref, computed } from 'vue'
import { matchRecipes } from '../api/recipes'

export const MATCH_ERROR_MESSAGE = 'Не удалось загрузить рецепты. Проверьте соединение и повторите.'
export const BROKEN_SELECTION_MESSAGE =
  'Блюдо недоступно: источник удалён, контент не читается. Уберите его из плана или замените на другое.'

export function emptyFilters() {
  return {
    maxDifficulty: null,
    maxCalories: '',
    seasons: [],
    diets: [],
    maxCookTime: '',
    ingredient: '',
    tag: '',
    preferSeasons: [],
    preferDiets: [],
    preferLowCalories: false,
    preferLowComplexity: false
  }
}

export function hasAnyFilter(filters) {
  return Boolean(
    filters.maxDifficulty ||
      filters.maxCalories !== '' ||
      filters.seasons.length ||
      filters.diets.length ||
      filters.maxCookTime !== '' ||
      filters.ingredient ||
      filters.tag ||
      filters.preferSeasons.length ||
      filters.preferDiets.length ||
      filters.preferLowCalories ||
      filters.preferLowComplexity
  )
}

export function toPickerItem(item) {
  return {
    id: item.recipeId,
    name: item.name,
    difficulty: item.difficulty,
    calories: item.calories,
    cookTimeMinutes: item.cookTimeMinutes,
    servings: item.servings,
    tags: item.tags,
    seasonality: item.seasonality,
    diet: item.diet,
    matchScore: item.matchScore,
    isExternal: item.isExternal || false,
    sourceFamilyName: item.sourceFamilyName || null,
    state: item.state || null
  }
}

function cloneFilters(filters) {
  return {
    ...filters,
    seasons: [...filters.seasons],
    diets: [...filters.diets],
    preferSeasons: [...filters.preferSeasons],
    preferDiets: [...filters.preferDiets]
  }
}

/** Внешний вид выбора: принимает и выдачу подбора (id/name), и запись плана (recipeId/recipeName). */
function selectionFrom(source) {
  return {
    recipeId: source.id ?? source.recipeId,
    name: source.name ?? source.recipeName,
    state: source.state || null,
    isExternal: source.isExternal || false,
    sourceFamilyName: source.sourceFamilyName || null
  }
}

function matchBody(filters, search) {
  const query = search.trim()
  return {
    search: query.length ? query : null,
    filters: {
      maxDifficulty: filters.maxDifficulty || null,
      maxCalories: filters.maxCalories === '' ? null : Number(filters.maxCalories),
      seasons: filters.seasons,
      diets: filters.diets,
      maxCookTimeMinutes: filters.maxCookTime === '' ? null : Number(filters.maxCookTime),
      includeIngredients: filters.ingredient ? [filters.ingredient] : null,
      tags: filters.tag ? [filters.tag] : null
    },
    preferences: {
      preferSeasons: filters.preferSeasons,
      preferDiets: filters.preferDiets,
      preferLowCalories: filters.preferLowCalories,
      preferLowComplexity: filters.preferLowComplexity
    }
  }
}

/**
 * Подбор доступного блюда. Выбор хранится отдельно от текущей выдачи: смена
 * фильтров/поиска не теряет имя и состояние выбранного рецепта. Поздний ответ
 * сервера не заменяет более новый (номер запроса), ошибка подбора не очищает
 * ни выдачу, ни выбор. Поиск по имени уходит на сервер до ограничения выдачи.
 */
export function useRecipePicker(options = {}) {
  const matchRecipe = options.matchRecipes || matchRecipes
  const schedule = options.schedule || ((run) => run())

  const recipes = ref([])
  const loading = ref(false)
  const error = ref('')
  const search = ref('')
  const draftFilters = ref(emptyFilters())
  const appliedFilters = ref(emptyFilters())
  const selection = ref(null)
  const portions = ref(1)

  let requestId = 0

  const hasFilters = computed(() => hasAnyFilter(appliedFilters.value))

  const selectionWarning = computed(() =>
    selection.value?.state === 'broken' ? BROKEN_SELECTION_MESSAGE : ''
  )

  /**
   * Если выбранный рецепт присутствует в свежей выдаче, берём актуальные имя/состояние
   * оттуда; если исчез — оставляем сохранённый выбор, чтобы подтверждение не потеряло имя.
   */
  function syncSelection() {
    if (!selection.value) return
    const fresh = recipes.value.find((r) => r.id === selection.value.recipeId)
    if (fresh) selection.value = selectionFrom(fresh)
  }

  async function reload() {
    const id = ++requestId
    loading.value = true
    error.value = ''
    try {
      const { response, data } = await matchRecipe(matchBody(appliedFilters.value, search.value))
      if (id !== requestId) return
      if (response.status === 200 && data && Array.isArray(data.items)) {
        recipes.value = data.items.map(toPickerItem)
        syncSelection()
      } else {
        error.value = data?.error || MATCH_ERROR_MESSAGE
      }
    } catch {
      if (id === requestId) error.value = MATCH_ERROR_MESSAGE
    } finally {
      if (id === requestId) loading.value = false
    }
  }

  /** Открыть подбор для ячейки плана; entry — текущая запись или null. */
  function open(entry) {
    search.value = ''
    draftFilters.value = emptyFilters()
    appliedFilters.value = emptyFilters()
    error.value = ''
    if (entry) {
      selection.value = selectionFrom(entry)
      portions.value = entry.portions || 1
    } else {
      selection.value = null
      portions.value = 1
    }
    return reload()
  }

  function select(item) {
    selection.value = selectionFrom(item)
  }

  function setSearch(value) {
    search.value = value
    return schedule(reload)
  }

  function apply() {
    appliedFilters.value = cloneFilters(draftFilters.value)
    return reload()
  }

  function reset() {
    draftFilters.value = emptyFilters()
    appliedFilters.value = emptyFilters()
    search.value = ''
    return reload()
  }

  function confirm() {
    if (!selection.value || portions.value < 1) return null
    // Сломанный источник нельзя назначить как доступное блюдо: контента нет.
    // Пользователь видит объяснение и убирает или заменяет запись.
    if (selection.value.state === 'broken') return null
    return {
      recipeId: selection.value.recipeId,
      recipeName: selection.value.name,
      portions: portions.value,
      state: selection.value.state,
      isExternal: selection.value.isExternal,
      sourceFamilyName: selection.value.sourceFamilyName
    }
  }

  return {
    recipes,
    loading,
    error,
    search,
    draftFilters,
    appliedFilters,
    hasFilters,
    selection,
    selectionWarning,
    portions,
    reload,
    open,
    select,
    setSearch,
    apply,
    reset,
    confirm
  }
}
