export const DAYS = [
  { code: 0, label: 'Пн' },
  { code: 1, label: 'Вт' },
  { code: 2, label: 'Ср' },
  { code: 3, label: 'Чт' },
  { code: 4, label: 'Пт' },
  { code: 5, label: 'Сб' },
  { code: 6, label: 'Вс' }
]

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

export function mondayOf(date) {
  const d = new Date(date.getFullYear(), date.getMonth(), date.getDate())
  const offset = (d.getDay() + 6) % 7
  d.setDate(d.getDate() - offset)
  return d
}

export function addDays(date, days) {
  return new Date(date.getFullYear(), date.getMonth(), date.getDate() + days)
}

export function weekDays(monday) {
  return DAYS.map((_, index) => addDays(monday, index))
}

export function toIso(date) {
  const y = date.getFullYear()
  const m = String(date.getMonth() + 1).padStart(2, '0')
  const d = String(date.getDate()).padStart(2, '0')
  return `${y}-${m}-${d}`
}

export function parseIso(value) {
  const [y, m, d] = value.split('-').map(Number)
  return new Date(y, m - 1, d)
}

export function weekRangeLabel(monday) {
  const end = addDays(monday, 6)
  return `${monday.getDate()} ${MONTHS_GENITIVE[monday.getMonth()]} — ${end.getDate()} ${MONTHS_GENITIVE[end.getMonth()]} ${end.getFullYear()}`
}