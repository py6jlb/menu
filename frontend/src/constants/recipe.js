export const UNITS = [
  { code: 'g', label: 'г' },
  { code: 'kg', label: 'кг' },
  { code: 'ml', label: 'мл' },
  { code: 'l', label: 'л' },
  { code: 'pcs', label: 'шт' },
  { code: 'glass', label: 'стакан' },
  { code: 'tbsp', label: 'ст. ложка' },
  { code: 'tsp', label: 'ч. ложка' },
  { code: 'pinch', label: 'щепотка' }
]

export const SEASONS = [
  { code: 'winter', label: 'Зима' },
  { code: 'spring', label: 'Весна' },
  { code: 'summer', label: 'Лето' },
  { code: 'autumn', label: 'Осень' }
]

export const DIETS = [
  { code: 'vegetarian', label: 'Вегетарианское' },
  { code: 'gluten_free', label: 'Безглютеновое' },
  { code: 'lean', label: 'Постное' },
  { code: 'keto', label: 'Кетогенное' }
]

export function unitLabel(code) {
  return UNITS.find((u) => u.code === code)?.label || code
}

export function seasonLabel(code) {
  return SEASONS.find((s) => s.code === code)?.label || code
}

export function parseList(value) {
  return (value || '')
    .split(',')
    .map((item) => item.trim())
    .filter((item) => item.length > 0)
}

export function joinList(items) {
  return (items || []).join(', ')
}