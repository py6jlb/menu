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

// Конечный справочник категорий продуктов. Коды должны совпадать с
// RecipeCatalog.IngredientCategories на бэкенде; порядок задаёт порядок отделов.
export const CATEGORIES = [
  { code: 'vegetables', label: 'Овощи, фрукты, зелень', icon: '🥦' },
  { code: 'meat', label: 'Мясо, птица', icon: '🥩' },
  { code: 'fish', label: 'Рыба, морепродукты', icon: '🐟' },
  { code: 'dairy', label: 'Молочные продукты, яйца', icon: '🥛' },
  { code: 'bakery', label: 'Хлеб, выпечка', icon: '🍞' },
  { code: 'groceries', label: 'Бакалея', icon: '🌾' },
  { code: 'sauces_spices', label: 'Соусы, специи, приправы', icon: '🧂' },
  { code: 'oils_vinegar', label: 'Масла, уксус', icon: '🫒' },
  { code: 'frozen', label: 'Замороженные продукты', icon: '🧊' },
  { code: 'sweets_snacks', label: 'Сладости, снеки, орехи', icon: '🍫' },
  { code: 'drinks', label: 'Напитки', icon: '☕' }
]

// Пустая категория: не выбирается в форме, но группирует продукты в конце списка покупок.
export const OTHER_CATEGORY = { code: '', label: 'Прочее', icon: '🧺' }

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

const DIET_CODES = DIETS.map((d) => d.code)

export function dietLabel(code) {
  return DIETS.find((d) => d.code === code)?.label || code
}

/**
 * Отбрасывает пустые значения и повторы без учёта регистра, сохраняя порядок и
 * исходный вид первой встреченной метки.
 */
function uniqueByLower(values) {
  const result = []
  const seen = new Set()
  for (const value of values) {
    const trimmed = String(value ?? '').trim()
    if (!trimmed) continue
    const key = trimmed.toLowerCase()
    if (seen.has(key)) continue
    seen.add(key)
    result.push(trimmed)
  }
  return result
}

/**
 * Разделяет сохранённые метки диеты на стандартные коды (чекбоксы) и произвольные
 * метки (свободный ввод). Коды из `DIETS` попадают в `selected`, всё остальное —
 * в `custom` без потерь и без сведения к стандартному варианту.
 */
export function splitDiets(values) {
  const codes = new Set(DIET_CODES)
  const selected = []
  const custom = []

  for (const value of uniqueByLower(values || [])) {
    if (codes.has(value)) selected.push(value)
    else custom.push(value)
  }

  return { selected, custom }
}

/** Объединяет выбранные стандартные коды и произвольные метки в один набор без дублей. */
export function combineDiets(diets, text) {
  return uniqueByLower([...(diets || []), ...parseList(text)])
}

export function unitLabel(code) {
  return UNITS.find((u) => u.code === code)?.label || code
}

export function seasonLabel(code) {
  return SEASONS.find((s) => s.code === code)?.label || code
}

/** Подпись категории продукта; пустая категория — «Прочее», неизвестный код — «Прочее». */
export function categoryLabel(code) {
  return CATEGORIES.find((c) => c.code === code)?.label || OTHER_CATEGORY.label
}

/** Иконка категории продукта; неизвестный код — иконка «Прочего». */
export function categoryIcon(code) {
  return CATEGORIES.find((c) => c.code === code)?.icon || OTHER_CATEGORY.icon
}

export const PHOTO_TYPES = ['image/jpeg', 'image/png', 'image/webp', 'image/gif']
export const PHOTO_ACCEPT = PHOTO_TYPES.join(',')
export const PHOTO_MAX_BYTES = 5 * 1024 * 1024
export const PHOTO_MAX_LABEL = '5 МБ'
export const PHOTO_TYPES_LABEL = 'JPEG, PNG, WebP или GIF'
export const PHOTO_MAX_DIMENSION = 8000
export const PHOTO_MAX_PIXELS = 25000000
export const PHOTO_DIMENSIONS_LABEL = '8000 пикселей по стороне и 25 мегапикселей'

/** Проверка файла до отправки; серверная валидация остаётся окончательной. */
export function validatePhotoFile(file) {
  if (!file) return ''
  if (!PHOTO_TYPES.includes(file.type)) {
    return `Файл должен быть изображением: ${PHOTO_TYPES_LABEL}.`
  }
  if (file.size > PHOTO_MAX_BYTES) {
    return `Размер фото не должен превышать ${PHOTO_MAX_LABEL}.`
  }
  return ''
}

/**
 * Проверка фактических размеров изображения (в пикселях) до отправки.
 * Ограничения совпадают с серверными; окончательное решение за сервером.
 */
export function validatePhotoDimensions(width, height) {
  const w = Number(width)
  const h = Number(height)
  if (!Number.isFinite(w) || !Number.isFinite(h) || w <= 0 || h <= 0) {
    return 'Не удалось определить размеры изображения. Загрузите другой файл.'
  }
  if (w > PHOTO_MAX_DIMENSION || h > PHOTO_MAX_DIMENSION || w * h > PHOTO_MAX_PIXELS) {
    return `Изображение слишком большое: допустимо не более ${PHOTO_DIMENSIONS_LABEL}.`
  }
  return ''
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

const FIELD_LABELS = {
  name: 'Название',
  description: 'Описание',
  cookTimeMinutes: 'Время приготовления',
  servings: 'Порции',
  difficulty: 'Сложность',
  calories: 'Калорийность',
  steps: 'Шаги',
  tags: 'Теги',
  seasonality: 'Сезонность',
  diet: 'Диеты',
  ingredients: 'Ингредиенты'
}

const INGREDIENT_FIELD_LABELS = {
  name: 'название',
  amount: 'количество',
  unit: 'единица измерения',
  note: 'примечание',
  category: 'категория'
}

/**
 * Человекочитаемая подпись пути поля из серверной ошибки ввода, чтобы причина
 * была привязана к конкретному вводу, а не только к форме целиком.
 */
export function recipeFieldLabel(field) {
  if (!field) return ''
  const match = /^ingredients\[(\d+)\](?:\.(\w+))?$/.exec(field)
  if (match) {
    const base = `Ингредиент ${Number(match[1]) + 1}`
    const part = match[2] ? INGREDIENT_FIELD_LABELS[match[2]] || match[2] : ''
    return part ? `${base}: ${part}` : base
  }
  return FIELD_LABELS[field] || field
}