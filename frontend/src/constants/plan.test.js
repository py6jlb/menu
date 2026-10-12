import { describe, it, expect } from 'vitest'
import { dayLabel, mealLabel } from './plan'

describe('dayLabel', () => {
  it('возвращает подпись известного дня недели', () => {
    expect(dayLabel(0)).toBe('Пн')
    expect(dayLabel(6)).toBe('Вс')
  })

  it('для неизвестного дня отдаёт номер с запасной подписью', () => {
    expect(dayLabel(7)).toBe('День 8')
    expect(dayLabel(-1)).toBe('День 0')
  })
})

describe('mealLabel', () => {
  it('возвращает подпись известного приёма пищи', () => {
    expect(mealLabel('breakfast')).toBe('Завтрак')
    expect(mealLabel('dinner')).toBe('Ужин')
  })

  it('для неизвестного кода возвращает сам код', () => {
    expect(mealLabel('brunch')).toBe('brunch')
  })

  it('для пустого кода возвращает сам код', () => {
    expect(mealLabel('')).toBe('')
    expect(mealLabel(undefined)).toBe(undefined)
  })
})
