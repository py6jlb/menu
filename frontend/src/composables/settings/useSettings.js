import { ref, computed } from 'vue'
import { getSettings, updateSettings } from '../../api/settings'
import { DEFAULT_WINDOW_WEEKS } from '../../constants/settings'
import { LOAD_FAILED, SAVE_FAILED } from '../../constants/messages'
import { useRequestTracker } from '../common/useRequestTracker'

function hasWindow(data) {
  return data != null && Number.isInteger(data.repetitionWindowWeeks)
}

/**
 * Личные настройки: загрузка, правка и сохранение без побочных эффектов
 * на клиенте. Каждый ответ привязан к своему ключу (загрузка/сохранение),
 * поэтому устаревший ответ не подменяет более новую правку. Ошибка сети или
 * разбора завершает pending, сохраняет введённое значение и допускает повтор.
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

  const tracker = useRequestTracker()

  const dirty = computed(
    () => confirmedWindowWeeks.value !== null && windowWeeks.value !== confirmedWindowWeeks.value
  )

  async function load() {
    const token = tracker.begin('load')
    loading.value = true
    loadError.value = ''
    saveError.value = ''
    savedMessage.value = ''
    try {
      const { response, data } = await loadSettings()
      if (!tracker.isCurrent(token)) return
      if (response.status === 200 && hasWindow(data)) {
        windowWeeks.value = data.repetitionWindowWeeks
        confirmedWindowWeeks.value = data.repetitionWindowWeeks
      } else if (response.status === 200) {
        // Тело успешного ответа не разобралось — значение не трогаем.
        loadError.value = LOAD_FAILED
      } else {
        loadError.value = data?.error || LOAD_FAILED
      }
    } catch {
      if (tracker.isCurrent(token)) loadError.value = LOAD_FAILED
    } finally {
      if (tracker.isCurrent(token)) loading.value = false
    }
  }

  async function save() {
    const sentWeeks = windowWeeks.value
    const token = tracker.begin('save')
    saving.value = true
    saveError.value = ''
    savedMessage.value = ''
    try {
      const { response, data } = await saveSettings({ repetitionWindowWeeks: sentWeeks })
      if (!tracker.isCurrent(token)) return
      if (response.status !== 200) {
        saveError.value = data?.error || SAVE_FAILED
        return
      }
      if (!hasWindow(data)) {
        // Ответ не разобрался: результат сохранения неизвестен, ввод сохраняем.
        saveError.value = SAVE_FAILED
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
      if (tracker.isCurrent(token)) saveError.value = SAVE_FAILED
    } finally {
      if (tracker.isCurrent(token)) saving.value = false
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
