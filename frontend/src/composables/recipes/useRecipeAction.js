import { ref } from 'vue'
import { CONFLICT, SERVER_UNAVAILABLE } from '../../constants/messages'
import { revisionOf } from '../../api/revision'
import { useRequestTracker } from '../common/useRequestTracker'

/**
 * Оркестрация одного действия над рецептом: `pending`/`error`/`conflict` плюс
 * страж «ответ старого ресурса не применяется».
 *
 * **Инстанс на действие**: каждый `useRecipeAction()` держит собственные refs,
 * поэтому ошибки и конфликты разных действий (удалить / убрать из семьи /
 * скопировать / импортировать) не перетирают друг друга и три отдельных места
 * показа ошибки копии продолжают работать.
 *
 * View владеет подтверждениями (`window.confirm`), навигацией и заменой ресурса
 * (через `onSuccess`) и размещением ошибок; composable владеет только ходом
 * запроса и его исходом. Router сюда не проникает.
 *
 * `run(request, { key, acceptedStatuses, onSuccess, fallbackMessage })`:
 * - `request()` — async-вызов, возвращающий `{ response, data }`;
 * - `key` — identity ресурса (id рецепта или токен ссылки): ответ, чей ключ
 *   устарел (уже начат другой ключ либо вызван `reset`), не применяется;
 * - `acceptedStatuses` — статусы успеха, на которых зовётся `onSuccess(data)`;
 * - `onSuccess(data)` — побочный эффект успеха (навигация/замена ресурса);
 * - `fallbackMessage` — текст для неожиданного статуса без серверного текста.
 */
export function useRecipeAction() {
  const pending = ref(false)
  const error = ref('')
  const conflict = ref(null) // null | { revision: number | null }

  const tracker = useRequestTracker()
  let activeKey = null

  function begin(key) {
    activeKey = key
    return tracker.begin(key)
  }

  function current(token, key) {
    return tracker.isCurrent(token) && key === activeKey
  }

  async function run(request, { key, acceptedStatuses = [], onSuccess, fallbackMessage } = {}) {
    const token = begin(key)
    const isCurrent = () => current(token, key)

    pending.value = true
    error.value = ''
    conflict.value = null

    try {
      const { response, data } = await request()
      if (!isCurrent()) return
      if (acceptedStatuses.includes(response.status)) {
        onSuccess?.(data)
        return
      }
      if (response.status === 409) {
        conflict.value = { revision: revisionOf(data) }
        error.value = data?.error || CONFLICT
        return
      }
      error.value = data?.error || fallbackMessage
    } catch {
      if (isCurrent()) error.value = SERVER_UNAVAILABLE
    } finally {
      if (isCurrent()) pending.value = false
    }
  }

  function reset() {
    tracker.reset()
    activeKey = null
    pending.value = false
    error.value = ''
    conflict.value = null
  }

  return { pending, error, conflict, run, reset }
}
