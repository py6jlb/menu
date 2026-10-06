import { ref, computed } from 'vue'
import { getSettings, updateSettings } from '../api/settings'
import { DEFAULT_WINDOW_WEEKS } from '../constants/settings'

export const SETTINGS_LOAD_ERROR =
  'Не удалось загрузить настройки. Проверьте соединение и попробуйте снова.'
export const SETTINGS_SAVE_ERROR =
  'Не удалось сохранить настройки. Проверьте соединение и повторите.'

function hasWindow(data) {
  return data != null && Number.isInteger(data.repetitionWindowWeeks)
}

/**
 * Личные настройки: загрузка, правка и сохранение без побочных эффектов
 * на клиенте. Каждый ответ привязан к номеру запроса, поэтому устаревший ответ
 * не подменяет более новую правку. Ошибка сети или разбора завершает pending,
 * сохраняет введённое значение и допускает повтор.
 */
export function useSettings(options = {}) {
  const loadSettings = options.loadSettings || getSettings
  const saveSettings = options.saveSettings || updateSettings

  const windowWeeks = ref(DEFAULT_WINDOW_WEEKS)
  const confirmedWindowWeeks = ref(null)

  const loading = ref(false)
  const saving = ref(false)
  const loadError = ref('')
  const saveError = ref('')
  const savedMessage = ref('')

  let loadRequestId = 0
  let saveRequestId = 0

  const dirty = computed(
    () => confirmedWindowWeeks.value !== null && windowWeeks.value !== confirmedWindowWeeks.value
  )

  async function load() {
    const requestId = ++loadRequestId
    loading.value = true
    loadError.value = ''
    saveError.value = ''
    savedMessage.value = ''
    try {
      const { response, data } = await loadSettings()
      if (requestId !== loadRequestId) return
      if (response.status === 200 && hasWindow(data)) {
        windowWeeks.value = data.repetitionWindowWeeks
        confirmedWindowWeeks.value = data.repetitionWindowWeeks
      } else if (response.status === 200) {
        // Тело успешного ответа не разобралось — значение не трогаем.
        loadError.value = SETTINGS_LOAD_ERROR
      } else {
        loadError.value = data?.error || SETTINGS_LOAD_ERROR
      }
    } catch {
      if (requestId === loadRequestId) loadError.value = SETTINGS_LOAD_ERROR
    } finally {
      if (requestId === loadRequestId) loading.value = false
    }
  }

  async function save() {
    const sentWeeks = windowWeeks.value
    const requestId = ++saveRequestId
    saving.value = true
    saveError.value = ''
    savedMessage.value = ''
    try {
      const { response, data } = await saveSettings({ repetitionWindowWeeks: sentWeeks })
      if (requestId !== saveRequestId) return
      if (response.status !== 200) {
        saveError.value = data?.error || SETTINGS_SAVE_ERROR
        return
      }
      if (!hasWindow(data)) {
        // Ответ не разобрался: результат сохранения неизвестен, ввод сохраняем.
        saveError.value = SETTINGS_SAVE_ERROR
        return
      }
      // Подтверждённое значение всегда отражает ответ сервера; показанное
      // значение не перетирает правку, сделанную после отправки.
      confirmedWindowWeeks.value = data.repetitionWindowWeeks
      if (windowWeeks.value === sentWeeks) {
        windowWeeks.value = data.repetitionWindowWeeks
        savedMessage.value = 'Настройки сохранены.'
      }
    } catch {
      if (requestId === saveRequestId) saveError.value = SETTINGS_SAVE_ERROR
    } finally {
      if (requestId === saveRequestId) saving.value = false
    }
  }

  return {
    windowWeeks,
    confirmedWindowWeeks,
    dirty,
    loading,
    saving,
    loadError,
    saveError,
    savedMessage,
    load,
    save
  }
}
