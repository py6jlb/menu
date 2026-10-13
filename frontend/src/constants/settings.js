/** Допустимый диапазон окна повторяемости в неделях; значение по умолчанию — 3. */
export const DEFAULT_WINDOW_WEEKS = 3
export const MIN_WINDOW_WEEKS = 1
export const MAX_WINDOW_WEEKS = 52

/** Проверка окна повторяемости: целое число в допустимом диапазоне. */
export function isValidWindowWeeks(value) {
  return Number.isInteger(value) && value >= MIN_WINDOW_WEEKS && value <= MAX_WINDOW_WEEKS
}
