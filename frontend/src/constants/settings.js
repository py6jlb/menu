export const DEFAULT_WINDOW_WEEKS = 3
export const MIN_WINDOW_WEEKS = 1
export const MAX_WINDOW_WEEKS = 52

export function isValidWindowWeeks(value) {
  return Number.isInteger(value) && value >= MIN_WINDOW_WEEKS && value <= MAX_WINDOW_WEEKS
}
