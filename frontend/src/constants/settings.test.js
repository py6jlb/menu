import { describe, it, expect } from 'vitest'
import {
  DEFAULT_WINDOW_WEEKS,
  MAX_WINDOW_WEEKS,
  MIN_WINDOW_WEEKS,
  isValidWindowWeeks
} from './settings'

describe('isValidWindowWeeks', () => {
  it('принимает целые значения в границах', () => {
    expect(isValidWindowWeeks(MIN_WINDOW_WEEKS)).toBe(true)
    expect(isValidWindowWeeks(DEFAULT_WINDOW_WEEKS)).toBe(true)
    expect(isValidWindowWeeks(MAX_WINDOW_WEEKS)).toBe(true)
  })

  it('отклоняет значения вне границ и дробные', () => {
    expect(isValidWindowWeeks(MIN_WINDOW_WEEKS - 1)).toBe(false)
    expect(isValidWindowWeeks(MAX_WINDOW_WEEKS + 1)).toBe(false)
    expect(isValidWindowWeeks(2.5)).toBe(false)
  })

  it('отклоняет пустое и нечисловое значение', () => {
    expect(isValidWindowWeeks('')).toBe(false)
    expect(isValidWindowWeeks(undefined)).toBe(false)
    expect(isValidWindowWeeks('4')).toBe(false)
  })
})
