/** Дни недели: код — индекс от понедельника; порядок задаёт порядок колонок. */
export const DAYS = [
  { code: 0, label: 'Пн' },
  { code: 1, label: 'Вт' },
  { code: 2, label: 'Ср' },
  { code: 3, label: 'Чт' },
  { code: 4, label: 'Пт' },
  { code: 5, label: 'Сб' },
  { code: 6, label: 'Вс' }
]

/** Приёмы пищи дня в порядке отображения; коды совпадают с бэкендом. */
export const MEALS = [
  { code: 'breakfast', label: 'Завтрак' },
  { code: 'snack_1', label: 'Перекус' },
  { code: 'lunch', label: 'Обед' },
  { code: 'snack_2', label: 'Перекус' },
  { code: 'dinner', label: 'Ужин' }
]

const MONTHS_GENITIVE = [
  'января', 'февраля', 'марта', 'апреля', 'мая', 'июня',
  'июля', 'августа', 'сентября', 'октября', 'ноября', 'декабря'
]

/** Понедельник недели, содержащей `date` (день 0 — понедельник). */
export function mondayOf(date) {
  const d = new Date(date.getFullYear(), date.getMonth(), date.getDate())
  const offset = (d.getDay() + 6) % 7
  d.setDate(d.getDate() - offset)
  return d
}

/** Сдвиг даты на `days` календарных дней без учёта времени суток. */
export function addDays(date, days) {
  return new Date(date.getFullYear(), date.getMonth(), date.getDate() + days)
}

/** Семь дат недели от понедельника `monday`, в порядке дней `DAYS`. */
export function weekDays(monday) {
  return DAYS.map((_, index) => addDays(monday, index))
}

/** Дата в формате `YYYY-MM-DD` по локальному календарю (без сдвига часового пояса). */
export function toIso(date) {
  const y = date.getFullYear()
  const m = String(date.getMonth() + 1).padStart(2, '0')
  const d = String(date.getDate()).padStart(2, '0')
  return `${y}-${m}-${d}`
}

/** Дата из строки `YYYY-MM-DD` в локальном календаре. */
export function parseIso(value) {
  const [y, m, d] = value.split('-').map(Number)
  return new Date(y, m - 1, d)
}

/** Подпись недели «день месяц — день месяц год» по понедельнику `monday`. */
export function weekRangeLabel(monday) {
  const end = addDays(monday, 6)
  return `${monday.getDate()} ${MONTHS_GENITIVE[monday.getMonth()]} — ${end.getDate()} ${MONTHS_GENITIVE[end.getMonth()]} ${end.getFullYear()}`
}

/** Подпись дня недели по индексу; неизвестный индекс — «День N». */
export function dayLabel(day) {
  return DAYS[day]?.label || `День ${day + 1}`
}

/** Подпись приёма пищи по коду; неизвестный код возвращается как есть. */
export function mealLabel(code) {
  return MEALS.find((m) => m.code === code)?.label || code
}