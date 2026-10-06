import { ref, computed } from 'vue'
import { mondayOf, addDays, toIso, parseIso } from '../constants/plan'
import { getShoppingList } from '../api/shopping'

export const SHOPPING_LOAD_ERROR_MESSAGE =
  'Не удалось загрузить список покупок. Проверьте соединение и попробуйте снова.'
export const SHOPPING_FAMILY_ERROR_MESSAGE = 'Вы пока не состоите в семье.'

/**
 * Список покупок выбранной недели, устойчивый к поздним ответам и сетевым ошибкам.
 *
 * Каждый запрос привязан к неделе и номеру: ответ на старую неделю не подменяет
 * текущую. Сетевая ошибка снимает pending, но оставляет предыдущий результат —
 * `resultWeek` говорит, за какую неделю он показан, а `resultIsCurrent` — совпадает
 * ли он с выбранной неделей.
 */
export function useShoppingList(options = {}) {
  const fetchList = options.fetchList || getShoppingList
  const weekStart = ref(options.initialWeek || mondayOf(new Date()))

  const items = ref([])
  const excluded = ref([])
  const hasPlan = ref(false)
  const loading = ref(false)
  const refreshing = ref(false)
  const error = ref('')
  const resultWeek = ref(null)

  let requestId = 0

  const selectedWeek = computed(() => toIso(weekStart.value))
  const resultIsCurrent = computed(() => resultWeek.value === selectedWeek.value)
  const resultWeekDate = computed(() => (resultWeek.value ? parseIso(resultWeek.value) : null))

  function isCurrent(id, requestedWeek) {
    return id === requestId && requestedWeek === selectedWeek.value
  }

  async function load({ refresh = false } = {}) {
    const requestedWeek = selectedWeek.value
    const id = ++requestId
    if (refresh) refreshing.value = true
    else loading.value = true
    error.value = ''

    try {
      const { response, data } = await fetchList(requestedWeek)
      if (!isCurrent(id, requestedWeek)) return

      if (response.status === 200 && data) {
        items.value = data.items || []
        excluded.value = data.excluded || []
        hasPlan.value = data.hasPlan === true
        resultWeek.value = requestedWeek
      } else if (response.status === 404) {
        items.value = []
        excluded.value = []
        hasPlan.value = false
        resultWeek.value = null
        error.value = data?.error || SHOPPING_FAMILY_ERROR_MESSAGE
      } else {
        // Определённая ошибка сервера: предыдущий результат сохраняем.
        error.value = data?.error || SHOPPING_LOAD_ERROR_MESSAGE
      }
    } catch {
      // Сеть недоступна: pending снимаем, показываем прежний результат за его неделю.
      if (isCurrent(id, requestedWeek)) error.value = SHOPPING_LOAD_ERROR_MESSAGE
    } finally {
      if (isCurrent(id, requestedWeek)) {
        loading.value = false
        refreshing.value = false
      }
    }
  }

  function goToWeek(offset) {
    weekStart.value = addDays(weekStart.value, offset * 7)
    return load()
  }

  function refresh() {
    return load({ refresh: true })
  }

  return {
    weekStart,
    items,
    excluded,
    hasPlan,
    loading,
    refreshing,
    error,
    resultWeek,
    resultIsCurrent,
    resultWeekDate,
    load,
    goToWeek,
    refresh
  }
}
